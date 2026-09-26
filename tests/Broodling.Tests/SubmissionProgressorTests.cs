using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;
using ControlledGateway = Broodling.Tests.BundledProposerTests.ControlledGateway;
using Gateway = Broodling.Tests.IssueSubmissionPreparationTests.Gateway;
using PreparationFixture = Broodling.Tests.RepositoryPreparationTests.RepositoryPreparationFixture;

namespace Broodling.Tests;

/// <summary>
/// Automatic progression with no caller over real capture, Git and SQLite, with controlled GitHub and model
/// gateway peers and the loopback stock-target stand-in. Preparation checkpoints, dispatch and acknowledgement
/// races and completion belong to their owners' tests; these cover discovery, independence, retry cadence and
/// limit, stopping, the host process and shutdown and restart.
/// </summary>
public sealed class SubmissionProgressorTests
{
    private static readonly GitHubRepositoryCredentials GitHub = new("configured-token");
    private const string Proposal = """{"criteria": [{"criterionId": "export", "statement": "Export the data."}]}""";
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [Test]
    public async Task ProgressionInTheHostProcessHoldsNoWriterAndShutdownLeavesItsDispatchForExactReplay()
    {
        await using var target = new StockTarget();
        using var fixture = new PreparationFixture();
        fixture.SetIssue(12, RequestAdmissionTests.Request());
        var submissionId = Submit(fixture, 12).Single();
        using var store = fixture.State.Open();
        var gateway = Proposing();
        using var sent = new SemaphoreSlim(0);
        target.Submit = _ =>
        {
            sent.Release();
            return new TaskCompletionSource<(int, string)>().Task;
        };
        var (app, client) = await HttpReadTests.Start(fixture.State.Path);
        using var __ = client;
        // Attached the way the host's lifetime would attach it.
        var first = new Service(fixture, target, gateway, lifetime: app.Lifetime.ApplicationStopping);

        await Assert.That(await sent.WaitAsync(Bound)).IsTrue();
        var attemptId = store.GetIssueSubmission(submissionId).AttemptIds.Single();
        // While progression waits on the target, the same process answers reads and another session can write.
        await Assert.That((await client.GetAsync("/health").WaitAsync(Bound)).StatusCode).IsEqualTo(HttpStatusCode.OK);
        var read = JsonNode.Parse(await (await client.GetAsync($"/submissions/{submissionId}").WaitAsync(Bound)).Content.ReadAsStringAsync())!;
        await Assert.That((string)read["state"]!).IsEqualTo("admitted");
        using (var writer = fixture.State.Connect())
        using (var transaction = writer.BeginTransaction(deferred: false))
            transaction.Rollback();
        await Assert.That(first.Progressor.Progress().Single().State).IsEqualTo(SubmissionProgress.Progressing);

        await app.StopAsync();
        await first.Running.WaitAsync(Bound);
        await app.DisposeAsync();
        // Shutdown detached the send: the dispatch stays unresolved and the Attempt keeps its authority.
        await Assert.That(store.RequireCurrentAttempt(attemptId).ResourceKind).IsEqualTo(AttemptRecord.Http);
        await Assert.That(store.FindSubmission(attemptId)!.State).IsEqualTo("dispatched");
        await Assert.That(store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(1);
        store.PauseInstallation();
        var reads = fixture.ReadGhPaths().Length;
        target.Submit = body => Task.FromResult(target.Accept(body));

        await using var restarted = new Service(fixture, target, gateway, credentials: "rotated");
        // The replay is an initiating path, so it waits for the pause to be released.
        await restarted.ScanUntil(() => restarted.Progressor.Progress() is [{ State: SubmissionProgress.Waiting, Code: "installation_paused" }]);
        await Assert.That(target.Bodies.Count).IsEqualTo(1);
        store.ReleaseInstallation();
        await restarted.ScanUntil(() => Correlated(fixture, submissionId));
        var credentialReads = restarted.CredentialReads;
        for (var scan = 0; scan < 3; scan++) await restarted.Scan();

        // The same frozen request and key, sent with the current rotated credentials.
        await Assert.That(target.Bodies.Count).IsEqualTo(2);
        await Assert.That(JsonNode.DeepEquals(Frozen(target.Bodies[0]), Frozen(target.Bodies[1]))).IsTrue();
        await Assert.That((string)target.Bodies[1]["githubToken"]!).IsEqualTo("github-canary-rotated");
        await Assert.That(store.FindSubmission(attemptId)!.RunId).IsEqualTo((string)target.Bodies[0]["runId"]!);
        // Native checks out exact retained B1 itself; the Attempt owns no client execution workspace.
        var b1 = store.GetRequestBundle(submissionId).Repository!.StartingCommit;
        await Assert.That((string)target.Bodies[0]["submission"]!["source"]!["revision"]!).IsEqualTo(b1);
        var attempt = store.GetAttempt(attemptId);
        await Assert.That(attempt.B1.CommitOid).IsEqualTo(b1);
        await Assert.That(attempt.WorktreeAllocation).IsNull();
        await Assert.That(store.GetIssueSubmission(submissionId).AttemptIds.Count).IsEqualTo(1);
        // Neither capture nor the proposal ran again, and correlated work is left to the completion observer.
        await Assert.That(gateway.Contexts.Count).IsEqualTo(1);
        await Assert.That(fixture.ReadGhPaths().Length).IsEqualTo(reads);
        await Assert.That(restarted.CredentialReads).IsEqualTo(credentialReads);
        await Assert.That(restarted.Progressor.Progress()).IsEmpty();
        await Assert.That(target.Messages).IsEmpty();
        await Assert.That(first.Stops.IsEmpty && restarted.Stops.IsEmpty).IsTrue();
    }

    [Test]
    public async Task SubmissionsAcceptedWhileRunningProgressPastOneWaitingOnItsModelCallAndShutdownDetachesIt()
    {
        await using var target = new StockTarget();
        using var fixture = new PreparationFixture();
        fixture.SetIssue(12, RequestAdmissionTests.Request());
        fixture.SetIssue(13, "## Request\n<!-- broodling-request:v1 -->\nAdd JSON export.\n");
        var blocked = Submit(fixture, 12).Single();
        using var store = fixture.State.Open();
        using var entered = new SemaphoreSlim(0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interrupted = false;
        var gateway = new Gateway(async (context, cancellationToken) =>
        {
            if (context.Contains("CSV"))
            {
                entered.Release();
                try { await release.Task.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { Volatile.Write(ref interrupted, true); throw; }
            }
            return ControlledGateway.Final(Proposal);
        });
        await using var service = new Service(fixture, target, gateway);
        await Assert.That(await entered.WaitAsync(Bound)).IsTrue();

        var later = store.SubmitIssue("https://github.com/acme/widget/issues/13").SubmissionId;
        await service.ScanUntil(() => Correlated(fixture, later));
        await Assert.That(store.GetIssueSubmission(blocked).ContractRevisionId).IsNull();
        // Only the blocked submission is still in progress once the other's operation has returned.
        await Until(() => service.Progressor.Progress().Select(entry => entry.SubmissionId).SequenceEqual([blocked]));

        // The host ends the preparer's lifetime before the service's token: the interrupted proposal only detaches.
        service.StopPreparer();
        await Until(() => Volatile.Read(ref interrupted));
        for (var scan = 0; scan < 3; scan++) await service.Scan();
        await Assert.That(service.Stops).IsEmpty();
        await service.DisposeAsync();

        release.SetResult();
        await using var restarted = new Service(fixture, target, gateway);
        await Until(() => Correlated(fixture, blocked));
        await Assert.That(target.Runs.Count).IsEqualTo(2);
        await Assert.That(restarted.Stops).IsEmpty();
    }

    [Test]
    public async Task TemporaryFailuresRetryAtABoundedCadenceUntilTheLimitWhileThePauseOnlyWaits()
    {
        await using var target = new StockTarget();
        using var fixture = new PreparationFixture();
        fixture.SetIssue(12, RequestAdmissionTests.Request());
        var submissionId = Submit(fixture, 12).Single();
        using var store = fixture.State.Open();
        var clock = new FakeTimeProvider();
        var calls = new ConcurrentQueue<DateTimeOffset>();
        var available = false;
        var gateway = new Gateway((_, _) =>
        {
            calls.Enqueue(clock.GetUtcNow());
            return Task.FromResult(Volatile.Read(ref available) ? ControlledGateway.Final(Proposal)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("overloaded") });
        });

        await using (var service = new Service(fixture, target, gateway, clock: clock))
        {
            SubmissionProgress? Entry() => service.Progressor.Progress().SingleOrDefault();
            await service.ScanUntil(() => Entry() is { State: SubmissionProgress.Waiting, Failures: 2 });
            var waiting = Entry()!;
            await Assert.That(waiting.Code).IsEqualTo("contract_proposer_error");
            await Assert.That(waiting.Retryable).IsTrue();
            await Assert.That(waiting.RetryAt >= calls.Last() + TimeSpan.FromSeconds(30)).IsTrue();

            // Paused, nothing is proposed and the retry limit is not used up.
            store.PauseInstallation();
            await service.ScanUntil(() => Entry() is { State: SubmissionProgress.Waiting, Code: "installation_paused" });
            for (var scan = 0; scan < 2 * SubmissionProgressor.RetryLimit; scan++) await service.Scan();
            await Until(() => Entry()?.State == SubmissionProgress.Waiting);
            await Assert.That(Entry()!.Code).IsEqualTo("installation_paused");
            await Assert.That(Entry()!.Failures).IsEqualTo(2);
            await Assert.That(calls.Count).IsEqualTo(2);

            store.ReleaseInstallation();
            await service.ScanUntil(() => Entry()?.State == SubmissionProgress.Stopped);
            for (var scan = 0; scan < 3; scan++) await service.Scan();

            var stopped = Entry()!;
            await Assert.That(stopped).IsEqualTo(new SubmissionProgress(submissionId, SubmissionProgress.Stopped,
                SubmissionProgress.Preparation, "contract_proposer_error", waiting.Message, true, SubmissionProgressor.RetryLimit));
            await Assert.That(service.Stops.Single()).IsEqualTo((stopped, (Exception?)null));
            var times = calls.ToArray();
            await Assert.That(times.Length).IsEqualTo(SubmissionProgressor.RetryLimit);
            // Each retry waits at least twice as long as the one before, up to the maximum delay.
            for (var retry = 1; retry < times.Length; retry++)
                await Assert.That(times[retry] - times[retry - 1]).IsGreaterThanOrEqualTo(TimeSpan.FromTicks(Math.Min(
                    SubmissionProgressor.Cadence.Ticks << (retry - 1), SubmissionProgressor.MaximumRetryDelay.Ticks)));
        }

        // The stop belongs to that process; a restart discovers the unchanged submission again.
        Volatile.Write(ref available, true);
        await using var restarted = new Service(fixture, target, gateway);
        await Until(() => Correlated(fixture, submissionId));
    }

    [Test]
    public async Task ADeterministicConflictStopsAndEndedWorkIsNeitherProgressedNorReplaced()
    {
        await using var target = new StockTarget();
        target.Submit = _ => Task.FromResult((409, StockTarget.Conflict));
        using var fixture = new PreparationFixture();
        fixture.SetIssue(12, RequestAdmissionTests.Request());
        fixture.SetIssue(13, "## Request\n<!-- broodling-request:v1 -->\nAdd JSON export.\n");
        var ids = Submit(fixture, 12, 13, 14);
        var (conflicting, abandoned, cancelled) = (ids[0], ids[1], ids[2]);
        using var store = fixture.State.Open();
        await store.CaptureRequestBundleAsync(abandoned, fixture.RepositoryRoot, GitHub, new GitHubIssueSource(fixture.Gh), fixture.Source);
        store.AdmitRequestBundle(abandoned, ContractIngressTests.Propose, "caller");
        store.AbandonAttempt(store.AdmitHttpAttempt(abandoned).AttemptId, "Ended by its operator.");
        await store.CancelIssueSubmissionAsync(cancelled, "No longer wanted.");
        var proposals = 0;
        var gateway = new Gateway((_, _) => Task.FromResult(Interlocked.Increment(ref proposals) == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("overloaded") }
            : ControlledGateway.Final(Proposal)));

        await using var service = new Service(fixture, target, gateway);
        await service.ScanUntil(() => !service.Stops.IsEmpty);
        var credentialReads = service.CredentialReads;
        for (var scan = 0; scan < 3; scan++) await service.Scan();

        var (stop, failure) = service.Stops.Single();
        await Assert.That(stop.SubmissionId).IsEqualTo(conflicting);
        await Assert.That(stop.State).IsEqualTo(SubmissionProgress.Stopped);
        await Assert.That(stop.Stage).IsEqualTo(SubmissionProgress.Continuation);
        await Assert.That(stop.Code).IsEqualTo("submission_conflict");
        await Assert.That(stop.Retryable).IsFalse();
        await Assert.That(stop.Failures).IsEqualTo(1);
        await Assert.That(failure).IsNull();
        // Nothing runs again and nothing is replaced: no send, no operation and the Attempt keeps its authority.
        await Assert.That(target.Bodies.Count).IsEqualTo(1);
        await Assert.That(service.CredentialReads).IsEqualTo(credentialReads);
        await Assert.That(service.Progressor.Progress()).IsEmpty();
        await Assert.That(store.RequireCurrentAttempt(store.GetIssueSubmission(conflicting).AttemptIds.Single()).Retry).IsNull();
        var ended = store.GetAttempt(store.GetIssueSubmission(abandoned).AttemptIds.Single());
        await Assert.That(ended.Abandonment).IsNotNull();
        await Assert.That(store.FindSubmission(ended.AttemptId)).IsNull();
        await Assert.That(store.GetIssueSubmission(cancelled).State).IsEqualTo("cancelled");
        await Assert.That(fixture.ReadGhPaths().Any(path => path.EndsWith("/issues/14", StringComparison.Ordinal))).IsFalse();
        await Assert.That(gateway.Contexts.Count).IsEqualTo(2);
    }

    [Test]
    public async Task WorkAbandonedDuringItsSendLeavesWithoutAStop()
    {
        await using var target = new StockTarget();
        using var fixture = new PreparationFixture();
        fixture.SetIssue(12, RequestAdmissionTests.Request());
        var submissionId = Submit(fixture, 12).Single();
        using var sent = new SemaphoreSlim(0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        target.Submit = async body =>
        {
            sent.Release();
            await release.Task;
            return target.Accept(body);
        };
        await using var service = new Service(fixture, target, Proposing());
        await Assert.That(await sent.WaitAsync(Bound)).IsTrue();

        using var store = fixture.State.Open();
        var attemptId = store.GetIssueSubmission(submissionId).AttemptIds.Single();
        store.AbandonAttempt(attemptId, "Ended while its send was held.");
        release.SetResult();

        // The late acknowledgement fails the operation as stale; the abandonment it lost to stands instead.
        await Until(() => store.FindSubmission(attemptId) is { State: "correlated" } && service.Progressor.Progress().Count == 0);
        for (var scan = 0; scan < 3; scan++) await service.Scan();
        await Assert.That(service.Stops).IsEmpty();
        await Assert.That(store.GetAttempt(attemptId).Abandonment!.Reason).IsEqualTo("Ended while its send was held.");
    }

    [Test]
    public async Task ACredentialProviderThatFailsStopsTheSubmissionForAttention()
    {
        await using var target = new StockTarget();
        using var fixture = new PreparationFixture();
        var submissionId = Submit(fixture, 12).Single();
        var failure = new OperationCanceledException("The secret source timed out.");

        await using var service = new Service(fixture, target, Proposing(), provider: () => throw failure);
        await Until(() => !service.Stops.IsEmpty);
        var reads = service.CredentialReads;
        for (var scan = 0; scan < 3; scan++) await service.Scan();

        await Assert.That(service.Stops.Single()).IsEqualTo((new SubmissionProgress(submissionId, SubmissionProgress.Stopped,
            SubmissionProgress.Preparation, "unexpected_failure", "Progression failed unexpectedly.", false, 1), (Exception?)failure));
        await Assert.That(service.CredentialReads).IsEqualTo(reads);
        await Assert.That(fixture.ReadGhPaths()).IsEmpty();
    }

    [Test]
    public async Task EarlierUnboundAssociationsAreNeverDiscovered()
    {
        // Authentic pre-#111 state: completed bundles associated with an admitted and an undecided unbound Contract.
        await using var target = new StockTarget();
        using var fixture = new PreparationFixture();
        StoreLifecycleTests.Restore(fixture.State.Path, "application-v1-unbound-association.sql");
        var gateway = Proposing();

        await using var service = new Service(fixture, target, gateway);
        for (var scan = 0; scan < 3; scan++) await service.Scan();

        await Assert.That(service.CredentialReads).IsEqualTo(0);
        await Assert.That(service.Progressor.Progress()).IsEmpty();
        await Assert.That(service.Stops).IsEmpty();
        await Assert.That(gateway.Contexts).IsEmpty();
        await Assert.That(target.Connections).IsEqualTo(0);
    }

    private static string[] Submit(PreparationFixture fixture, params long[] issues)
    {
        using var store = fixture.State.Initialize();
        return issues.Select(issue => store.SubmitIssue("https://github.com/acme/widget/issues/" + issue).SubmissionId).ToArray();
    }

    private static Gateway Proposing() => new((_, _) => Task.FromResult(ControlledGateway.Final(Proposal)));

    private static bool Correlated(PreparationFixture fixture, string submissionId)
    {
        using var store = fixture.State.Open();
        return store.GetIssueSubmission(submissionId).AttemptIds is [var attemptId]
            && store.FindSubmission(attemptId) is { State: "correlated" };
    }

    /// <summary>A sent body without its ephemeral credentials.</summary>
    private static JsonObject Frozen(JsonObject body)
    {
        var frozen = body.DeepClone().AsObject();
        frozen.Remove("connections");
        frozen.Remove("githubToken");
        return frozen;
    }

    private static Task Until(Func<bool> condition) => ProvisioningProcessTests.WaitUntil(condition);

    /// <summary>A progressor and its preparer over the fixture, whose scan interval advances only when a test says so.</summary>
    private sealed class Service : IAsyncDisposable
    {
        private readonly FakeTimeProvider clock;
        private readonly CancellationTokenSource cancellation = new();
        private readonly CancellationTokenSource preparerLifetime = new();
        private int credentialReads;
        internal SubmissionProgressor Progressor { get; }
        internal Task Running { get; }
        internal ConcurrentQueue<(SubmissionProgress Progress, Exception? Failure)> Stops { get; } = new();
        internal int CredentialReads => Volatile.Read(ref credentialReads);

        internal Service(PreparationFixture fixture, StockTarget target, HttpMessageHandler gateway, string credentials = "current",
            CancellationToken? lifetime = null, FakeTimeProvider? clock = null, Func<ProgressionCredentials>? provider = null)
        {
            this.clock = clock ?? new FakeTimeProvider();
            var preparer = new IssueSubmissionPreparer(fixture.State.Application, fixture.State.Path, fixture.RepositoryRoot,
                lifetime ?? preparerLifetime.Token)
            {
                IssueSource = new GitHubIssueSource(fixture.Gh), RepositorySource = fixture.Source, Gateway = gateway
            };
            Progressor = new SubmissionProgressor(fixture.State.Application, fixture.State.Path, null,
                new InvocationTarget.Direct(target.Origin.GetLeftPart(UriPartial.Authority)), preparer, () =>
                {
                    Interlocked.Increment(ref credentialReads);
                    return provider?.Invoke() ?? new(GitHub, new GatewayCredentials(NativeProfile.GatewayBaseUrl, "gateway-key"),
                        HttpDispatchTests.Credentials(credentials));
                }, (progress, failure) => Stops.Enqueue((progress, failure))) { Clock = this.clock };
            Running = Progressor.RunAsync(lifetime ?? cancellation.Token);
        }

        internal void StopPreparer() => preparerLifetime.Cancel();

        /// <summary>Return once another discovery pass has run, re-advancing if the loop was not yet parked.</summary>
        internal Task Scan()
        {
            var before = Progressor.Scans;
            return Until(() =>
            {
                if (Progressor.Scans > before) return true;
                clock.Advance(SubmissionProgressor.Cadence);
                return false;
            });
        }

        internal async Task ScanUntil(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!condition())
            {
                if (timer.Elapsed > Bound) throw new TimeoutException("The progressor did not settle.");
                await Scan();
            }
        }

        /// <summary>Ends the preparer's lifetime first, as the Generic Host does, then the service's token.</summary>
        public async ValueTask DisposeAsync()
        {
            preparerLifetime.Cancel();
            cancellation.Cancel();
            await Running;
        }
    }
}
