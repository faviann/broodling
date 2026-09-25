namespace Broodling;

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
    internal static async Task<NativeResult> WaitAsync(NativeRunBinding run, TimeProvider clock, CancellationToken caller)
    {
        using var setup = DirectTargetBudget.Start(DirectTargetLimits.WaitSetup, clock, caller);
        await using var session = await DirectTargetSession.OpenAsync(run, setup);
        var first = await session.StatusAsync(setup);
        using var pacing = setup.Fresh(Timeout.InfiniteTimeSpan);
        return await session.TerminalAsync(first, pacing, DirectTargetLimits.WaitRead);
    }
}
