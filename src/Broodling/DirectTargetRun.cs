namespace Broodling;

/// <summary>Whether a stop sent force: never, possibly without a confirmed outcome, or with a terminal result.</summary>
internal enum DirectTargetForce { NotSent, Uncertain, Terminal }

/// <summary>
/// A stop's transport outcome. <see cref="Reason"/> is a fixed error kind unless force reached
/// <see cref="DirectTargetForce.Terminal"/>. None proves physical cessation.
/// </summary>
internal sealed record DirectTargetStop(DirectTargetForce Force, string? Reason);

/// <summary>
/// Persistence-free DirectTarget run operations over one fresh session each. They decide only
/// budgets and sequencing; what a result or stop means belongs to the application.
/// </summary>
internal static class DirectTargetRun
{
    /// <summary>
    /// Setup and the immediate first read share 30 seconds; each later read has its own 10 seconds.
    /// There is no overall deadline, and the caller may stop observing at any time.
    /// </summary>
    internal static async Task<NativeResult> WaitAsync(NativeRunBinding run, string? rootCertificate, TimeProvider clock,
        CancellationToken caller)
    {
        using var setup = DirectTargetBudget.Start(DirectTargetLimits.WaitSetup, clock, caller);
        await using var session = await DirectTargetSession.OpenAsync(run, rootCertificate, setup);
        var first = await session.StatusAsync(setup);
        using var pacing = setup.Fresh(Timeout.InfiniteTimeSpan);
        return await session.TerminalAsync(first, pacing, DirectTargetLimits.WaitRead);
    }

    /// <summary>
    /// One 30-second budget covers discovery, setup, the intended-identity precheck, one force and any
    /// polling. An intended run is forced only after a valid matching status, even a finished one. A
    /// failure before force is <see cref="DirectTargetForce.NotSent"/>; from the force request on it is
    /// <see cref="DirectTargetForce.Uncertain"/>. Caller cancellation propagates.
    /// </summary>
    internal static async Task<DirectTargetStop> StopAsync(NativeRunBinding run, NativeRunIdentity identity, string? rootCertificate,
        TimeProvider clock, CancellationToken caller)
    {
        using var budget = DirectTargetBudget.Start(DirectTargetLimits.Stop, clock, caller);
        var force = DirectTargetForce.NotSent;
        try
        {
            await using var session = await DirectTargetSession.OpenAsync(run, rootCertificate, budget);
            if (identity == NativeRunIdentity.Intended) await session.StatusAsync(budget);
            force = DirectTargetForce.Uncertain; // Conservatively, from the attempt to send it.
            var forced = await session.ForceAsync(budget);
            if (forced.Result is null) await session.TerminalAsync(forced, budget, null);
            return new(DirectTargetForce.Terminal, null);
        }
        catch (NativeTransportError error) { return new(force, error.Kind); }
        catch (UnsupportedRuntime error) { return new(force, error.Code); }
    }
}
