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
    /// <summary>Controls DirectTarget budgets and poll pauses; tests substitute a controlled clock.</summary>
    internal TimeProvider DirectTargetClock { get; set; } = TimeProvider.System;

    /// <summary>
    /// Read the run's progress through retained facts, without credentials or writes. Null means no
    /// run can be addressed, so nothing is contacted. An HTTP record with dispatch intent but no
    /// acknowledgement is read by its intended ID; that read never establishes correlation.
    /// </summary>
    public Task<NativeObservation?> ObserveAsync(string attemptId, INativeReader? transport,
        CancellationToken cancellationToken = default) =>
        ObserveAsync(attemptId, transport, DirectTargetLimits.Progress, cancellationToken);

    internal async Task<NativeObservation?> ObserveAsync(string attemptId, INativeReader? transport, TimeSpan bound,
        CancellationToken cancellationToken)
    {
        GetAttempt(attemptId);
        var submission = FindSubmission(attemptId);
        var (run, identity) = submission switch
        {
            { Run: { } confirmed } => (confirmed, NativeRunIdentity.Confirmed),
            { Format: NativeSubmission.Http, State: "dispatched" } => (submission.Frozen.Run(submission.IntendedRunId!), NativeRunIdentity.Intended),
            _ => (null, NativeRunIdentity.Confirmed)
        };
        if (run is null) return null;
        try
        {
            NativeProgress progress;
            if (submission!.Format == NativeSubmission.Http)
            {
                using var budget = DirectTargetBudget.Start(bound, DirectTargetClock, cancellationToken);
                await using var session = await DirectTargetSession.OpenAsync(run, budget);
                progress = (await session.StatusAsync(budget)).Progress;
            }
            else
                progress = await (transport ?? throw new SubmissionNotReady("Observing a bridge run requires native transport."))
                    .StatusAsync(run, bound, cancellationToken);
            return new NativeObservation.Available(DateTimeOffset.UtcNow, identity, progress);
        }
        catch (NativeTransportError error) { return new NativeObservation.Unavailable(DateTimeOffset.UtcNow, identity, error.Kind); }
        catch (UnsupportedRuntime error) { return new NativeObservation.Unavailable(DateTimeOffset.UtcNow, identity, error.Code); }
    }
}
