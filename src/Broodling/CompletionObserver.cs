using Microsoft.Data.Sqlite;

namespace Broodling;

/// <summary>
/// Retains completed native results with no caller waiting. Each scan, at startup and then every
/// <see cref="Cadence"/>, finds correlated HTTP Attempts that still hold authority and consumes each
/// through the exact-Attempt <see cref="BroodlingStore.WaitAsync"/>, in its own store session. It never
/// prepares, dispatches or stops, and it continues while the installation is paused.
/// </summary>
/// <param name="directTargetRootCertificate">
/// The PEM root that explicit waits trust for HTTPS DirectTarget connections, passed to every session;
/// null deliberately selects system trust.
/// </param>
public sealed class CompletionObserver(BroodlingApplication application, string storePath, string? directTargetRootCertificate)
{
    /// <summary>The scan interval, and so the longest pause before a failed wait is retried.</summary>
    internal static readonly TimeSpan Cadence = TimeSpan.FromSeconds(15);

    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    private int scans;
    /// <summary>Completed discovery passes, so tests can tell a scan has happened.</summary>
    internal int Scans => Volatile.Read(ref scans);

    /// <summary>
    /// Observe until cancelled. Cancellation detaches every wait; no run is stopped or abandoned. An
    /// unexpected failure ends only its own Attempt's observation, which is not retried in this process,
    /// and the returned task then faults with every such failure once observation stops.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var observing = new Dictionary<string, (Task Wait, CancellationTokenSource Detach)>();
        try
        {
            while (true)
            {
                // A faulted wait stays attached, so an unexpected failure is surfaced rather than retried.
                foreach (var (attemptId, ended) in observing.Where(pair => pair.Value.Wait.IsCompletedSuccessfully).ToArray())
                {
                    observing.Remove(attemptId);
                    ended.Detach.Dispose();
                }
                // A failed read says nothing about eligibility, so it neither starts nor detaches a wait.
                if (Discover() is { } eligible)
                {
                    // Eligibility never returns once lost. A wait whose Attempt has lost it can no longer
                    // retain anything and would poll the target for the process lifetime, so detach it.
                    foreach (var (attemptId, active) in observing)
                        if (!eligible.Contains(attemptId)) active.Detach.Cancel();
                    foreach (var attemptId in eligible)
                        if (!observing.ContainsKey(attemptId))
                        {
                            var detach = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            observing[attemptId] = (Task.Run(() => ObserveAsync(attemptId, detach.Token)), detach);
                        }
                }
                Interlocked.Increment(ref scans);
                await Task.Delay(Cadence, Clock, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        var waits = Task.WhenAll(observing.Values.Select(active => active.Wait));
        try { await waits; }
        catch (Exception) when (waits.Exception is { } faults) { throw faults; }
        finally { foreach (var active in observing.Values) active.Detach.Dispose(); }
    }

    /// <summary>Eligible Attempt IDs, or null when the store is temporarily unreadable.</summary>
    private IReadOnlyList<string>? Discover()
    {
        try
        {
            using var store = application.OpenStore(storePath, directTargetRootCertificate);
            return store.ObservableAttempts();
        }
        catch (Exception failure) when (failure is StoreStateException or SqliteException) { return null; }
    }

    private async Task ObserveAsync(string attemptId, CancellationToken cancellationToken)
    {
        try
        {
            using var store = application.OpenStore(storePath, directTargetRootCertificate);
            try { await store.WaitAsync(attemptId, null, cancellationToken); }
            // Refused only from a terminal result read from the run, which every later read returns again.
            catch (Exception refusal) when (refusal is ReceiptRefused or AcceptedRevisionRefused)
            {
                store.RefuseCompletion(attemptId, refusal.Message);
            }
        }
        catch (Exception failure) when (failure is OperationCanceledException && cancellationToken.IsCancellationRequested
            || Retryable(failure)) { }
    }

    /// <summary>
    /// Failures a later scan may resolve, or that end observation because the Attempt left eligibility.
    /// Anything else is unexpected: it faults the observation instead of becoming a silent retry, and
    /// <see cref="RunAsync"/> surfaces it to the host that attaches the observer (#120).
    /// </summary>
    private static bool Retryable(Exception failure) => failure
        // Target unreachable, misconfigured or not yet serving the run, including an unreadable root.
        is NativeTransportError or UnsupportedRuntime
        // The accepted commit is not yet fetchable; a refused pin was handled above.
        or ResultRetentionError
        // Retained submission and this release's pins differ before contact; a rollback resolves it.
        or SubmissionConflict
        // Authority ended, including native failure; the next scan no longer selects the Attempt.
        or StaleAttempt or SubmissionNotReady
        // Storage unavailable or busy.
        or StoreStateException or SqliteException;
}
