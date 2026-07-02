using Greenhouse.Core.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Greenhouse.Mqtt.Tests;

public class MqttMessagingServiceTests
{
    private static MqttMessagingService Create(FakeMqttClientAdapter client) =>
        new(client, NullLogger<MqttMessagingService>.Instance);

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(condition(), "Condition was not met within the timeout.");
    }

    [Fact]
    public async Task PublishAsync_forwards_to_client()
    {
        var client = new FakeMqttClientAdapter();

        await Create(client).PublishAsync("gh/heartbeat", "{}");

        Assert.Contains(("gh/heartbeat", "{}"), client.Published);
    }

    [Fact]
    public async Task Subscriptions_registered_before_start_are_applied_after_connect()
    {
        var client = new FakeMqttClientAdapter();
        var service = Create(client);
        service.Subscribe("gh/+/telemetry", _ => Task.CompletedTask);

        await service.StartAsync(CancellationToken.None);

        await WaitForAsync(() => client.Subscribed.Contains("gh/+/telemetry"));
        Assert.True(client.IsConnected);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Matching_message_invokes_the_registered_handler()
    {
        var client = new FakeMqttClientAdapter();
        var service = Create(client);
        var received = new TaskCompletionSource<MessageEnvelope>();
        service.Subscribe("gh/+/telemetry", envelope =>
        {
            received.TrySetResult(envelope);
            return Task.CompletedTask;
        });
        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => client.Subscribed.Contains("gh/+/telemetry"));

        await client.RaiseMessageAsync("gh/edge-1/telemetry", "42");

        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("gh/edge-1/telemetry", envelope.Topic);
        Assert.Equal("42", envelope.Payload);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Non_matching_message_does_not_invoke_the_handler()
    {
        var client = new FakeMqttClientAdapter();
        var service = Create(client);
        var invocations = 0;
        service.Subscribe("gh/+/telemetry", _ =>
        {
            Interlocked.Increment(ref invocations);
            return Task.CompletedTask;
        });
        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => client.Subscribed.Contains("gh/+/telemetry"));

        await client.RaiseMessageAsync("gh/edge-1/heartbeat", "beat");
        await Task.Delay(50);

        Assert.Equal(0, invocations);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Unsubscribe_removes_the_handler()
    {
        var client = new FakeMqttClientAdapter();
        var service = Create(client);
        var invocations = 0;
        service.Subscribe("gh/#", _ =>
        {
            Interlocked.Increment(ref invocations);
            return Task.CompletedTask;
        });
        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => client.Subscribed.Contains("gh/#"));

        service.Unsubscribe("gh/#");
        await client.RaiseMessageAsync("gh/anything", "x");
        await Task.Delay(50);

        Assert.Equal(0, invocations);
        Assert.Contains("gh/#", client.Unsubscribed);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Reconnects_and_reapplies_subscriptions_after_disconnect()
    {
        var client = new FakeMqttClientAdapter();
        var service = Create(client);
        service.Subscribe("gh/#", _ => Task.CompletedTask);
        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => client.ConnectCount == 1 && client.Subscribed.Contains("gh/#"));

        client.ClearSubscribed();
        await client.RaiseDisconnectedAsync();

        await WaitForAsync(() => client.ConnectCount >= 2 && client.Subscribed.Contains("gh/#"));
        Assert.True(client.IsConnected);

        await service.StopAsync(CancellationToken.None);
    }
}
