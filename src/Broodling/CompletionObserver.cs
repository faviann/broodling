namespace Broodling;

/// <summary>
/// Retains completed native results with no caller waiting. Each scan, at startup and then every
/// <see cref="Cadence"/>, finds correlated HTTP Attempts that still hold authority and consumes each
/// through the exact-Attempt <see cref="BroodlingStore.WaitAsync"/>, in its own store session. It never
/// prepares, dispatches or stops, and it continues while the installation is paused.
/// </summary>
public sealed class CompletionObserver(BroodlingApplication application, string storePath)
{
    /// <summary>The scan interval, and so the longest pause before a failed wait is retried.</summary>
    internal static readonly TimeSpan Cadence = TimeSpan.FromSeconds(15);

    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    private int scans;
    /// <summary>Completed discovery passes, so tests can tell a scan has happened.</summary>
    internal int Scans => Volatile.Read(ref scans);

    /// <summary>Observe until cancelled. Cancellation detaches every wait; no run is stopped or abandoned.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var observing = new Dictionary<string, Task>();
        try
        {
            while (true)
            {
                foreach (var ended in observing.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray())
                    observing.Remove(ended);
                foreach (var attemptId in Discover())
                    if (!observing.ContainsKey(attemptId))
                        observing[attemptId] = Task.Run(() => ObserveAsync(attemptId, cancellationToken));
                Interlocked.Increment(ref scans);
                await Task.Delay(Cadence, Clock, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        await Task.WhenAll(observing.Values);
    }

    private IReadOnlyList<string> Discover()
    {
        try
        {
            using var store = application.OpenStore(storePath);
            return store.ObservableAttempts();
        }
        catch (Exception) { return []; } // Storage may be briefly unavailable; the next scan reads again.
    }

    private async Task ObserveAsync(string attemptId, CancellationToken cancellationToken)
    {
        try
        {
            using var store = application.OpenStore(storePath);
            try { await store.WaitAsync(attemptId, null, cancellationToken); }
            // Only a terminal result read from the run is refused: reading it again returns the same result.
            // A retained-submission conflict before contact, such as a changed release pin, is retried.
            catch (ReceiptRefused refusal) { store.RefuseCompletion(attemptId, refusal.Message); }
        }
        // Native failure has already recorded abandonment. Any other failure leaves the Attempt
        // eligible, so the next scan retries it; configuration can be fixed without a restart.
        // Reporting these failures belongs to the host that attaches the observer (#120).
        catch (Exception) { }
    }
}
