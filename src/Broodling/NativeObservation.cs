namespace Broodling;

/// <summary>
/// One read of native progress at <see cref="ObservedAt"/>. It is never retained and never an
/// admission, abandonment or result fact; its age is its freshness.
/// </summary>
public abstract record NativeObservation(DateTimeOffset ObservedAt)
{
    public sealed record Available(DateTimeOffset ObservedAt, NativeProgress Progress) : NativeObservation(ObservedAt);

    /// <summary>Progress is unknown. This says nothing about whether execution failed.</summary>
    public sealed record Unavailable(DateTimeOffset ObservedAt, string Reason) : NativeObservation(ObservedAt);
}

public sealed partial class BroodlingStore
{
    /// <summary>
    /// Read the exact correlated run's progress through its retained locator and run ID, without
    /// credentials or writes. Null means no run is correlated, so nothing is observed or dispatched.
    /// </summary>
    public Task<NativeObservation?> ObserveAsync(string attemptId, INativeTransport transport,
        CancellationToken cancellationToken = default) =>
        ObserveAsync(attemptId, transport, TimeSpan.FromSeconds(10), cancellationToken);

    internal async Task<NativeObservation?> ObserveAsync(string attemptId, INativeTransport transport, TimeSpan bound,
        CancellationToken cancellationToken)
    {
        GetAttempt(attemptId);
        if (FindSubmission(attemptId) is not { RunId: { } runId } submitted) return null;
        try
        {
            var progress = await transport.StatusAsync(submitted.Locator, runId, bound, cancellationToken);
            return new NativeObservation.Available(DateTimeOffset.UtcNow, progress);
        }
        catch (NativeTransportError error) { return new NativeObservation.Unavailable(DateTimeOffset.UtcNow, error.Kind); }
        catch (UnsupportedRuntime error) { return new NativeObservation.Unavailable(DateTimeOffset.UtcNow, error.Code); }
    }
}
