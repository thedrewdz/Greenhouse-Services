using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Greenhouse.Core.Messaging;
using Greenhouse.Core.Onboarding;
using Microsoft.Extensions.Logging;

namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// Publishes accepted runtime mappings to <c>ghcfg/wr-{device_id}</c> and reconciles the Edge
/// Unit's acknowledgement on <c>ghcfg/ack-{device_id}</c>, applying the bounded retry budget.
/// </summary>
/// <remarks>
/// Requests are queued and drained by a single background pump so mapping updates never publish
/// concurrently for the same unit and the API can return before delivery completes. The pump is
/// started by the composition root, which also routes the shared <c>ghcfg/#</c> subscription
/// into <see cref="HandleAcknowledgementAsync"/>.
/// </remarks>
public sealed class EdgeUnitConfigurationPublisher : IEdgeUnitConfigurationPublisher
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IMessagingService _messaging;
    private readonly IEdgeUnitRepository _edgeUnits;
    private readonly IOnboardingWorkflow _onboarding;
    private readonly ConfigurationPublishPolicy _policy;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EdgeUnitConfigurationPublisher> _logger;

    private readonly Channel<PublishRequest> _queue = Channel.CreateUnbounded<PublishRequest>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, PendingAcknowledgement> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Seeded from the clock rather than a constant so message ids do not repeat across restarts.
    /// Correlation is <c>(message_id, mapping_version)</c>, and a fixed seed would let a retained
    /// or late ack from a previous process satisfy a fresh attempt's wait.
    /// </summary>
    private int _lastMessageId;

    public EdgeUnitConfigurationPublisher(
        IMessagingService messaging,
        IEdgeUnitRepository edgeUnits,
        IOnboardingWorkflow onboarding,
        ConfigurationPublishPolicy policy,
        TimeProvider timeProvider,
        ILogger<EdgeUnitConfigurationPublisher> logger)
    {
        _messaging = messaging;
        _edgeUnits = edgeUnits;
        _onboarding = onboarding;
        _policy = policy;
        _timeProvider = timeProvider;
        _logger = logger;

        // Keep it comfortably inside int range and monotonic within a process.
        _lastMessageId = (int)(timeProvider.GetUtcNow().ToUnixTimeSeconds() % 1_000_000) * 1_000;
    }

    public void RequestPublish(string deviceId, string mappingReason) =>
        _queue.Writer.TryWrite(new PublishRequest(deviceId, mappingReason));

    /// <summary>
    /// Drains queued publish requests until <paramref name="cancellationToken"/> is cancelled.
    /// Started once at host startup.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var request in _queue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await PublishAsync(request, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // One failed unit must never stop configuration delivery for the others.
                    _logger.LogWarning(
                        "Configuration publish for '{DeviceId}' failed: {Reason}",
                        request.DeviceId,
                        ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    /// <summary>
    /// Routes an inbound <c>ghcfg/#</c> message. Messages that are not acknowledgements, are not
    /// awaited, or do not correlate are ignored, which also makes a duplicate ack a no-op.
    /// </summary>
    public Task HandleAcknowledgementAsync(MessageEnvelope envelope)
    {
        var deviceId = EdgeUnitTopics.DeviceIdFromConfigurationAck(envelope.Topic);
        if (deviceId is null || !_pending.TryGetValue(deviceId, out var pending))
        {
            return Task.CompletedTask;
        }

        AcknowledgementDto? ack;
        try
        {
            ack = JsonSerializer.Deserialize<AcknowledgementDto>(envelope.Payload, SerializerOptions);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Malformed configuration ack on '{Topic}'.", envelope.Topic);
            return Task.CompletedTask;
        }

        if (ack is null)
        {
            return Task.CompletedTask;
        }

        // device_id in topic and payload must match, and the ack must correlate to the attempt
        // still in flight; anything else is stale or misrouted.
        if (!string.Equals(ack.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
            || ack.MessageId != pending.MessageId
            || ack.MappingVersion != pending.MappingVersion)
        {
            return Task.CompletedTask;
        }

        pending.Completion.TrySetResult(ack);
        return Task.CompletedTask;
    }

    private async Task PublishAsync(PublishRequest request, CancellationToken cancellationToken)
    {
        var unit = await _edgeUnits.GetAsync(request.DeviceId, cancellationToken);
        if (unit is null)
        {
            return;
        }

        var messageId = Interlocked.Increment(ref _lastMessageId);
        var payload = BuildPayload(unit, messageId, request.MappingReason);

        for (var attempt = 1; attempt <= _policy.MaxAttempts; attempt++)
        {
            var outcome = await AttemptAsync(unit, payload, messageId, attempt, cancellationToken);

            if (outcome is AttemptOutcome.Acknowledged)
            {
                await AcceptAsync(unit.DeviceId, cancellationToken);
                return;
            }

            if (outcome is AttemptOutcome.Rejected)
            {
                await FailAsync(unit.DeviceId, cancellationToken);
                return;
            }

            if (attempt < _policy.MaxAttempts)
            {
                await Task.Delay(_policy.RetryDelays[attempt - 1], _timeProvider, cancellationToken);
            }
        }

        await FailAsync(unit.DeviceId, cancellationToken);
    }

    /// <summary>
    /// One publish-and-await-ack attempt. Every failure mode that another attempt could plausibly
    /// fix — a transport error, an ack timeout, a retryable rejection — returns
    /// <see cref="AttemptOutcome.Retry"/> rather than throwing, so a single request can never
    /// escape the retry budget and leave the unit stranded at <c>publish-pending</c>.
    /// </summary>
    private async Task<AttemptOutcome> AttemptAsync(
        EdgeUnit unit,
        string payload,
        int messageId,
        int attempt,
        CancellationToken cancellationToken)
    {
        // Retries reuse the same message_id and mapping_version so the Edge Unit can
        // recognise the resend rather than treating it as a new update.
        var pending = new PendingAcknowledgement(messageId, unit.MappingVersion);
        _pending[unit.DeviceId] = pending;

        try
        {
            _logger.LogDebug(
                "Publishing configuration to '{DeviceId}' (message_id={MessageId}, mapping_version={MappingVersion}, attempt {Attempt}/{MaxAttempts}).",
                unit.DeviceId,
                messageId,
                unit.MappingVersion,
                attempt,
                _policy.MaxAttempts);

            try
            {
                await _messaging.PublishAsync(
                    EdgeUnitTopics.ConfigurationWrite(unit.DeviceId),
                    payload,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A transport failure — an offline broker, most likely — spends an attempt like
                // any other, and honours the backoff before the next one.
                _logger.LogWarning(
                    "Configuration publish to '{DeviceId}' failed on attempt {Attempt}/{MaxAttempts}: {Reason}",
                    unit.DeviceId,
                    attempt,
                    _policy.MaxAttempts,
                    ex.Message);

                return AttemptOutcome.Retry;
            }

            if (attempt == 1)
            {
                await _edgeUnits.UpdateMappingStatusAsync(
                    unit.DeviceId,
                    MappingStatuses.Published,
                    clearTopologyDrift: false,
                    cancellationToken);
            }

            var ack = await WaitForAcknowledgementAsync(pending, cancellationToken);
            if (ack is null)
            {
                _logger.LogWarning(
                    "Configuration ack timed out for '{DeviceId}' (message_id={MessageId}, mapping_version={MappingVersion}, attempt {Attempt}/{MaxAttempts}).",
                    unit.DeviceId,
                    messageId,
                    unit.MappingVersion,
                    attempt,
                    _policy.MaxAttempts);

                return AttemptOutcome.Retry;
            }

            if (IsSuccess(ack))
            {
                return AttemptOutcome.Acknowledged;
            }

            _logger.LogWarning(
                "Edge Unit '{DeviceId}' rejected configuration (message_id={MessageId}, mapping_version={MappingVersion}, error_code={ErrorCode}).",
                unit.DeviceId,
                messageId,
                unit.MappingVersion,
                ack.ErrorCode);

            return ConfigurationErrorCodes.IsRetryable(ack.ErrorCode)
                ? AttemptOutcome.Retry
                : AttemptOutcome.Rejected;
        }
        finally
        {
            _pending.TryRemove(new KeyValuePair<string, PendingAcknowledgement>(unit.DeviceId, pending));
        }
    }

    /// <summary>What one publish attempt concluded.</summary>
    private enum AttemptOutcome
    {
        /// <summary>The unit applied the mapping.</summary>
        Acknowledged,

        /// <summary>Another attempt may help, budget permitting.</summary>
        Retry,

        /// <summary>The unit refused in a way a resend cannot fix; stop now.</summary>
        Rejected,
    }

    private async Task<AcknowledgementDto?> WaitForAcknowledgementAsync(
        PendingAcknowledgement pending,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = Task.Delay(_policy.AckTimeout, _timeProvider, timeoutCts.Token);

        var completed = await Task.WhenAny(pending.Completion.Task, timeout);
        if (completed == pending.Completion.Task)
        {
            // Stop the timer so a long retry sequence does not accumulate pending delays.
            timeoutCts.Cancel();
            return await pending.Completion.Task;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private async Task AcceptAsync(string deviceId, CancellationToken cancellationToken)
    {
        // A successful ack is the only thing that clears the Drift Flag and makes the new
        // mapping the active one.
        await _edgeUnits.UpdateMappingStatusAsync(
            deviceId,
            MappingStatuses.Acknowledged,
            clearTopologyDrift: true,
            cancellationToken);

        await _onboarding.CompleteMappingAsync(deviceId, cancellationToken);
    }

    private Task FailAsync(string deviceId, CancellationToken cancellationToken) =>
        _edgeUnits.UpdateMappingStatusAsync(
            deviceId,
            MappingStatuses.Failed,
            clearTopologyDrift: false,
            cancellationToken);

    private static bool IsSuccess(AcknowledgementDto ack) =>
        string.Equals(ack.Result, "success", StringComparison.OrdinalIgnoreCase)
        && ack.ErrorCode == ConfigurationErrorCodes.Success;

    private static string BuildPayload(EdgeUnit unit, int messageId, string mappingReason)
    {
        var dto = new ConfigurationDto(
            SchemaVersion,
            messageId,
            unit.DeviceId,
            unit.MappingVersion,
            mappingReason,
            unit.UnitName ?? string.Empty,
            unit.Location ?? string.Empty,
            unit.Slots
                .OrderBy(slot => slot.SlotId)
                .Select(slot => new ConfigurationSlotDto(
                    slot.SlotId,
                    slot.Role ?? string.Empty,
                    slot.I2cAddress,
                    slot.Capability ?? string.Empty,
                    slot.Label))
                .ToArray());

        return JsonSerializer.Serialize(dto, SerializerOptions);
    }

    private sealed record PublishRequest(string DeviceId, string MappingReason);

    private sealed class PendingAcknowledgement(int messageId, int mappingVersion)
    {
        public int MessageId { get; } = messageId;

        public int MappingVersion { get; } = mappingVersion;

        public TaskCompletionSource<AcknowledgementDto> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record ConfigurationDto(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("message_id")] int MessageId,
        [property: JsonPropertyName("device_id")] string DeviceId,
        [property: JsonPropertyName("mapping_version")] int MappingVersion,
        [property: JsonPropertyName("mapping_reason")] string MappingReason,
        [property: JsonPropertyName("unit_name")] string UnitName,
        [property: JsonPropertyName("location")] string Location,
        [property: JsonPropertyName("slots")] IReadOnlyList<ConfigurationSlotDto> Slots);

    private sealed record ConfigurationSlotDto(
        [property: JsonPropertyName("slot_id")] int SlotId,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("i2c_address")] string I2cAddress,
        [property: JsonPropertyName("capability")] string Capability,
        [property: JsonPropertyName("label")] string? Label);

    private sealed record AcknowledgementDto(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("message_id")] int MessageId,
        [property: JsonPropertyName("device_id")] string? DeviceId,
        [property: JsonPropertyName("mapping_version")] int MappingVersion,
        [property: JsonPropertyName("result")] string? Result,
        [property: JsonPropertyName("error_code")] int ErrorCode,
        [property: JsonPropertyName("error_message")] string? ErrorMessage);
}
