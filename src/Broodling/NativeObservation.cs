namespace Broodling;

/// <summary>
/// One read of native progress at <see cref="ObservedAt"/>. It is never retained and never an
/// admission, abandonment or result fact; its age is its freshness. <see cref="Identity"/> says
/// whether the read addressed an intended or a confirmed run ID, so an available observation of an
/// intended run is never mistaken for acknowledgement.
/// </summary>
public abstract record NativeObservation(DateTimeOffset ObservedAt, NativeRunIdentity Identity)
{
    public sealed record Available(DateTimeOffset ObservedAt, NativeRunIdentity Identity, NativeProgress Progress)
        : NativeObservation(ObservedAt, Identity);

    /// <summary>Progress is unknown. This says nothing about whether execution failed.</summary>
    public sealed record Unavailable(DateTimeOffset ObservedAt, NativeRunIdentity Identity, string Reason)
        : NativeObservation(ObservedAt, Identity);
}

public sealed partial class BroodlingStore
{
    /// <summary>Controls DirectTarget budgets; tests substitute a controlled clock.</summary>
    internal TimeProvider DirectTargetClock { get; set; } = TimeProvider.System;

    /// <summary>
    /// Read the run's progress through retained facts, without credentials or writes. Null means no
    /// run can be addressed, so nothing is contacted. A record with dispatch intent but no
    /// acknowledgement is read by its intended ID; that read never establishes correlation.
    /// </summary>
    public Task<NativeObservation?> ObserveAsync(string attemptId, CancellationToken cancellationToken = default) =>
        ObserveAsync(attemptId, DirectTargetLimits.Progress, cancellationToken);

    internal async Task<NativeObservation?> ObserveAsync(string attemptId, TimeSpan bound, CancellationToken cancellationToken)
    {
        GetAttempt(attemptId);
        var (run, identity) = FindSubmission(attemptId) switch
        {
            { Run: { } confirmed } => (confirmed, NativeRunIdentity.Confirmed),
            { State: "dispatched" } submission => (submission.Frozen.Run(submission.IntendedRunId), NativeRunIdentity.Intended),
            _ => (null, NativeRunIdentity.Confirmed)
        };
        if (run is null) return null;
        try
        {
            var progress = await DirectTargetRun.ProgressAsync(run, directTargetRoot, bound, DirectTargetClock, cancellationToken);
            return new NativeObservation.Available(DateTimeOffset.UtcNow, identity, progress);
        }
        catch (NativeTransportError error) { return new NativeObservation.Unavailable(DateTimeOffset.UtcNow, identity, error.Kind); }
        catch (UnsupportedRuntime error) { return new NativeObservation.Unavailable(DateTimeOffset.UtcNow, identity, error.Code); }
    }
}
