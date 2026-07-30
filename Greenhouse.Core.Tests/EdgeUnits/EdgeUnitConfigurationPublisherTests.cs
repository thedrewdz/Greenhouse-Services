using System.Text.Json;
using Greenhouse.Core.EdgeUnits;
using Greenhouse.Core.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Greenhouse.Core.Tests.EdgeUnits;

/// <summary>
/// Covers the runtime configuration publish contract and its bounded retry budget. The policy is
/// scaled down to milliseconds so the retry and timeout paths run without real waiting; the
/// canonical 8s/3-attempt/1s-2s values are asserted separately.
/// </summary>
public class EdgeUnitConfigurationPublisherTests
{
    private const string DeviceId = "1ADD5912AF61";
    private static readonly DateTime SeenAt = new(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc);

    private static readonly ConfigurationPublishPolicy FastPolicy = new(
        TimeSpan.FromMilliseconds(50),
        new[] { TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(5) });

    private static EdgeUnit Mapped() => new(
        DeviceId,
        "GH-Edge-" + DeviceId,
        "East Sensor Unit",
        "Zone A",
        MappingVersion: 3,
        MappingStatuses.PublishPending,
        SeenAt,
        SeenAt,
        TopologyDriftDetectedAt: SeenAt,
        Slots: new[]
        {
            new EdgeUnitSlot(0, "0x25", SlotRoles.Sensor, "moisture", "Bed A Moisture", SeenAt),
            new EdgeUnitSlot(4, "0x51", SlotRoles.Actuator, "pump", null, SeenAt),
        });

    private sealed class Harness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(10));
        private readonly Task _pump;

        public Harness(ConfigurationPublishPolicy? policy = null)
        {
            Units.Units[DeviceId] = Mapped();
            Publisher = new EdgeUnitConfigurationPublisher(
                Messaging,
                Units,
                Onboarding,
                policy ?? FastPolicy,
                TimeProvider.System,
                NullLogger<EdgeUnitConfigurationPublisher>.Instance);

            Messaging.Subscribe(EdgeUnitTopics.ConfigurationRoot, Publisher.HandleAcknowledgementAsync);
            _pump = Publisher.RunAsync(_cts.Token);
        }

        public FakeMessagingService Messaging { get; } = new();

        public FakeEdgeUnitRepository Units { get; } = new();

        public RecordingOnboardingWorkflow Onboarding { get; } = new();

        public EdgeUnitConfigurationPublisher Publisher { get; }

        /// <summary>Replies to each publish as the Edge Unit would, using <paramref name="build"/>.</summary>
        public void RespondWith(Func<JsonElement, string> build) =>
            Messaging.OnPublish = async (_, payload) =>
            {
                using var document = JsonDocument.Parse(payload);
                await Messaging.DeliverAsync(
                    EdgeUnitTopics.ConfigurationAck(DeviceId),
                    build(document.RootElement));
            };

        public async Task WaitForStatusAsync(string status)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (Units.StatusUpdates.Any(u => u.Status == status))
                {
                    return;
                }

                await Task.Delay(10);
            }

            throw new TimeoutException(
                $"Mapping status '{status}' was never reached. Saw: {string.Join(", ", Units.StatusUpdates.Select(u => u.Status))}.");
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                await _pump;
            }
            catch (OperationCanceledException)
            {
                // Expected on teardown.
            }

            _cts.Dispose();
        }
    }

    private static string SuccessAck(JsonElement published) =>
        $"{{\"schema_version\":1,\"message_id\":{published.GetProperty("message_id").GetInt32()}," +
        $"\"device_id\":\"{DeviceId}\",\"mapping_version\":{published.GetProperty("mapping_version").GetInt32()}," +
        "\"result\":\"success\",\"error_code\":0,\"error_message\":\"\"}";

    private static string ErrorAck(JsonElement published, int errorCode) =>
        $"{{\"schema_version\":1,\"message_id\":{published.GetProperty("message_id").GetInt32()}," +
        $"\"device_id\":\"{DeviceId}\",\"mapping_version\":{published.GetProperty("mapping_version").GetInt32()}," +
        $"\"result\":\"error\",\"error_code\":{errorCode},\"error_message\":\"rejected\"}}";

    [Fact]
    public void The_default_policy_matches_the_canonical_retry_budget()
    {
        var policy = ConfigurationPublishPolicy.Default;

        Assert.Equal(TimeSpan.FromSeconds(8), policy.AckTimeout);
        Assert.Equal(3, policy.MaxAttempts);
        Assert.Equal(
            new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) },
            policy.RetryDelays);
    }

    [Fact]
    public async Task Publishes_the_canonical_configuration_payload()
    {
        await using var harness = new Harness();
        harness.RespondWith(SuccessAck);

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.InitialRegistration);
        await harness.WaitForStatusAsync(MappingStatuses.Acknowledged);

        var (topic, payload) = Assert.Single(harness.Messaging.Published);
        Assert.Equal("ghcfg/wr-" + DeviceId, topic);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(DeviceId, root.GetProperty("device_id").GetString());
        Assert.Equal(3, root.GetProperty("mapping_version").GetInt32());
        Assert.Equal("initial_registration", root.GetProperty("mapping_reason").GetString());
        Assert.Equal("East Sensor Unit", root.GetProperty("unit_name").GetString());
        Assert.Equal("Zone A", root.GetProperty("location").GetString());

        var slots = root.GetProperty("slots").EnumerateArray().ToArray();
        Assert.Equal(2, slots.Length);
        Assert.Equal(0, slots[0].GetProperty("slot_id").GetInt32());
        Assert.Equal("sensor", slots[0].GetProperty("role").GetString());
        Assert.Equal("0x25", slots[0].GetProperty("i2c_address").GetString());
        Assert.Equal("moisture", slots[0].GetProperty("capability").GetString());
        Assert.Equal("Bed A Moisture", slots[0].GetProperty("label").GetString());
        // label is optional; it is omitted rather than sent as null.
        Assert.False(slots[1].TryGetProperty("label", out _));
    }

    [Fact]
    public async Task A_successful_ack_acknowledges_the_mapping_and_clears_drift()
    {
        await using var harness = new Harness();
        harness.RespondWith(SuccessAck);

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.InitialRegistration);
        await harness.WaitForStatusAsync(MappingStatuses.Acknowledged);

        Assert.Equal(
            new[] { MappingStatuses.Published, MappingStatuses.Acknowledged },
            harness.Units.StatusUpdates.Select(u => u.Status));
        Assert.True(harness.Units.StatusUpdates[^1].ClearedDrift);
        Assert.False(harness.Units.Units[DeviceId].HasTopologyDrift);
        Assert.Equal(DeviceId, Assert.Single(harness.Onboarding.MappingCompleted));
    }

    [Fact]
    public async Task An_ack_timeout_retries_the_full_budget_then_fails()
    {
        await using var harness = new Harness();
        // No response at all: every attempt must time out.

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.TopologyChange);
        await harness.WaitForStatusAsync(MappingStatuses.Failed);

        Assert.Equal(3, harness.Messaging.Published.Count);

        // Retries reuse the same message_id and mapping_version so the unit sees a resend.
        var identifiers = harness.Messaging.Published
            .Select(m =>
            {
                using var document = JsonDocument.Parse(m.Payload);
                return (
                    MessageId: document.RootElement.GetProperty("message_id").GetInt32(),
                    Version: document.RootElement.GetProperty("mapping_version").GetInt32());
            })
            .Distinct()
            .ToArray();

        Assert.Single(identifiers);
        Assert.False(harness.Units.StatusUpdates[^1].ClearedDrift);
    }

    [Theory]
    [InlineData(ConfigurationErrorCodes.UnsupportedSchemaVersion)]
    [InlineData(ConfigurationErrorCodes.DeviceIdMismatch)]
    [InlineData(ConfigurationErrorCodes.InvalidMappingPayload)]
    [InlineData(ConfigurationErrorCodes.MappingVersionConflict)]
    public async Task A_non_retryable_error_stops_after_the_first_attempt(int errorCode)
    {
        await using var harness = new Harness();
        harness.RespondWith(published => ErrorAck(published, errorCode));

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.TopologyChange);
        await harness.WaitForStatusAsync(MappingStatuses.Failed);

        Assert.Single(harness.Messaging.Published);
    }

    [Fact]
    public async Task An_internal_apply_error_is_retried_up_to_the_budget()
    {
        await using var harness = new Harness();
        harness.RespondWith(published => ErrorAck(published, ConfigurationErrorCodes.InternalApplyError));

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.TopologyChange);
        await harness.WaitForStatusAsync(MappingStatuses.Failed);

        Assert.Equal(3, harness.Messaging.Published.Count);
    }

    [Fact]
    public async Task An_internal_apply_error_that_then_succeeds_is_acknowledged()
    {
        await using var harness = new Harness();
        var attempt = 0;
        harness.RespondWith(published => ++attempt == 1
            ? ErrorAck(published, ConfigurationErrorCodes.InternalApplyError)
            : SuccessAck(published));

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.TopologyChange);
        await harness.WaitForStatusAsync(MappingStatuses.Acknowledged);

        Assert.Equal(2, harness.Messaging.Published.Count);
    }

    [Fact]
    public async Task An_ack_that_does_not_correlate_is_ignored()
    {
        await using var harness = new Harness();
        harness.RespondWith(published =>
            $"{{\"schema_version\":1,\"message_id\":{published.GetProperty("message_id").GetInt32() + 1}," +
            $"\"device_id\":\"{DeviceId}\",\"mapping_version\":99," +
            "\"result\":\"success\",\"error_code\":0,\"error_message\":\"\"}");

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.TopologyChange);
        await harness.WaitForStatusAsync(MappingStatuses.Failed);

        Assert.DoesNotContain(harness.Units.StatusUpdates, u => u.Status == MappingStatuses.Acknowledged);
    }

    [Fact]
    public async Task A_duplicate_ack_is_processed_exactly_once()
    {
        await using var harness = new Harness();
        harness.Messaging.OnPublish = async (_, payload) =>
        {
            using var document = JsonDocument.Parse(payload);
            var ack = SuccessAck(document.RootElement);
            var topic = EdgeUnitTopics.ConfigurationAck(DeviceId);
            await harness.Messaging.DeliverAsync(topic, ack);
            await harness.Messaging.DeliverAsync(topic, ack);
        };

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.InitialRegistration);
        await harness.WaitForStatusAsync(MappingStatuses.Acknowledged);

        Assert.Single(harness.Units.StatusUpdates, u => u.Status == MappingStatuses.Acknowledged);
        Assert.Single(harness.Onboarding.MappingCompleted);
    }

    [Fact]
    public async Task A_configuration_write_echoed_back_is_not_treated_as_an_ack()
    {
        await using var harness = new Harness();
        harness.Messaging.OnPublish = async (topic, payload) =>
        {
            // A broker that echoes the Main Unit's own write must not satisfy the ack wait.
            await harness.Messaging.DeliverAsync(topic, payload);
        };

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.InitialRegistration);
        await harness.WaitForStatusAsync(MappingStatuses.Failed);

        Assert.DoesNotContain(harness.Units.StatusUpdates, u => u.Status == MappingStatuses.Acknowledged);
    }

    [Fact]
    public async Task A_malformed_ack_does_not_break_the_pump()
    {
        await using var harness = new Harness();
        harness.RespondWith(_ => "not json");

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.InitialRegistration);
        await harness.WaitForStatusAsync(MappingStatuses.Failed);

        Assert.Equal(3, harness.Messaging.Published.Count);
    }

    [Fact]
    public async Task A_publish_that_throws_spends_an_attempt_and_still_reaches_a_terminal_status()
    {
        await using var harness = new Harness();
        // An offline broker: MQTTnet throws rather than queueing.
        harness.Messaging.OnPublish = (_, _) => throw new InvalidOperationException("not connected");

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.InitialRegistration);
        await harness.WaitForStatusAsync(MappingStatuses.Failed);

        // The whole budget is spent, and the unit never strands at publish-pending.
        Assert.Equal(3, harness.Messaging.Published.Count);
        Assert.DoesNotContain(harness.Units.StatusUpdates, u => u.Status == MappingStatuses.Acknowledged);
    }

    [Fact]
    public async Task A_publish_that_throws_once_then_succeeds_is_acknowledged()
    {
        await using var harness = new Harness();
        var attempt = 0;
        harness.Messaging.OnPublish = async (_, payload) =>
        {
            if (++attempt == 1)
            {
                throw new InvalidOperationException("not connected");
            }

            using var document = JsonDocument.Parse(payload);
            await harness.Messaging.DeliverAsync(
                EdgeUnitTopics.ConfigurationAck(DeviceId),
                SuccessAck(document.RootElement));
        };

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.InitialRegistration);
        await harness.WaitForStatusAsync(MappingStatuses.Acknowledged);

        Assert.Equal(2, harness.Messaging.Published.Count);
    }

    [Fact]
    public async Task A_failing_publish_does_not_stop_the_pump_for_other_units()
    {
        await using var harness = new Harness();
        harness.Units.Units["2BEEF0000001"] = Mapped() with { DeviceId = "2BEEF0000001" };
        harness.Messaging.OnPublish = (topic, _) => topic.EndsWith(DeviceId, StringComparison.Ordinal)
            ? throw new InvalidOperationException("not connected")
            : Task.CompletedTask;

        harness.Publisher.RequestPublish(DeviceId, MappingReasons.InitialRegistration);
        harness.Publisher.RequestPublish("2BEEF0000001", MappingReasons.InitialRegistration);

        await harness.WaitForStatusAsync(MappingStatuses.Failed);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline
               && !harness.Messaging.Published.Any(m => m.Topic.EndsWith("2BEEF0000001", StringComparison.Ordinal)))
        {
            await Task.Delay(10);
        }

        Assert.Contains(harness.Messaging.Published, m => m.Topic == "ghcfg/wr-2BEEF0000001");
    }

    [Fact]
    public async Task An_unknown_device_publishes_nothing()
    {
        await using var harness = new Harness();

        harness.Publisher.RequestPublish("UNKNOWN", MappingReasons.InitialRegistration);
        await Task.Delay(100);

        Assert.Empty(harness.Messaging.Published);
    }
}
