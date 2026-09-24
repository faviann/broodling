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
    private static readonly TimeSpan ObservationBound = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Read the exact correlated run's progress through its retained locator and run ID, without
    /// credentials or writes. Null means no run is correlated, so nothing is observed or dispatched.
    /// </summary>
    public async Task<NativeObservation?> ObserveAsync(string attemptId, INativeTransport transport,
        TimeSpan? bound = null, CancellationToken cancellationToken = default)
    {
        GetAttempt(attemptId);
        if (FindSubmission(attemptId) is not { RunId: { } runId } submitted) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(bound ?? ObservationBound);
        try
        {
            var progress = await transport.StatusAsync(submitted.Locator, runId, deadline.Token);
            return new NativeObservation.Available(DateTimeOffset.UtcNow, progress);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NativeObservation.Unavailable(DateTimeOffset.UtcNow, "timeout");
        }
        catch (NativeTransportError error) { return new NativeObservation.Unavailable(DateTimeOffset.UtcNow, error.Kind); }
        catch (UnsupportedRuntime error) { return new NativeObservation.Unavailable(DateTimeOffset.UtcNow, error.Code); }
    }
}
