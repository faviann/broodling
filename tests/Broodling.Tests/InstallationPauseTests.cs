using System.Diagnostics;
using Broodling.Host;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class InstallationPauseTests
{
    [Test]
    public async Task PausePersistsAcrossReopenBlocksAdmissionAndDispatchUntilExplicitRelease()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        var paused = fixture.Store.PauseInstallation();

        await Assert.That(paused.IsPaused).IsTrue();
        await Assert.That(paused.InFlightInitiationDrained).IsTrue();
        await Assert.That(() => fixture.Store.AdmitSources(ContractIngressTests.Reference,
            [ContractIngressTests.Primary("paused admission"u8.ToArray())], ContractIngressTests.Propose, ContractIngressTests.PullRequest))
            .Throws<InstallationPaused>();
        await Assert.That(async () => await fixture.Store.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials()))
            .Throws<InstallationPaused>();
        await Assert.That(target.Connections).IsEqualTo(0);

        using (var reopened = fixture.Git.State.Open())
        {
            await Assert.That(reopened.GetInstallationStatus().IsPaused).IsTrue();
            await Assert.That(reopened.ReleaseInstallation().IsPaused).IsFalse();
            var submitted = await reopened.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials());
            await Assert.That(submitted.State).IsEqualTo("correlated");
        }
    }

    [Test]
    public async Task OperatorCommandsExposePauseStatusAndExplicitRelease()
    {
        using var fixture = new StoreFixture();
        using (fixture.Initialize()) { }
        var output = new StringWriter();
        var error = new StringWriter();

        await Assert.That(StoreCommands.Run(["pause-installation", fixture.Path], fixture.Application, output, error)).IsEqualTo(0);
        await Assert.That(output.ToString()).Contains("\"isPaused\":true");
        output.GetStringBuilder().Clear();
        await Assert.That(StoreCommands.Run(["installation-status", fixture.Path], fixture.Application, output, error)).IsEqualTo(0);
        await Assert.That(output.ToString()).Contains("\"isPaused\":true");
        output.GetStringBuilder().Clear();
        await Assert.That(StoreCommands.Run(["release-installation", fixture.Path], fixture.Application, output, error)).IsEqualTo(0);
        await Assert.That(output.ToString()).Contains("\"isPaused\":false");
    }

    [Test]
    public async Task PauseRetainsDispatchedStateAcrossTheSendUntilLateCorrelation()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueSubmit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        target.Submit = async body =>
        {
            started.TrySetResult();
            await continueSubmit.Task;
            return target.Accept(body);
        };

        var pending = fixture.Store.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials());
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var observer = fixture.Git.State.Open();
            var paused = observer.PauseInstallation();
            await Assert.That(paused.IsPaused).IsTrue();
            await Assert.That(paused.UnresolvedDispatches).IsEqualTo(1);
            await Assert.That(paused.InFlightInitiationDrained).IsFalse();
            await Assert.That(async () => await observer.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials()))
                .Throws<InstallationPaused>();

            continueSubmit.SetResult();
            await Assert.That((await pending).State).IsEqualTo("correlated");
            var drained = await SettledStatus(observer);
            await Assert.That(drained.IsPaused).IsTrue();
            await Assert.That(drained.UnresolvedDispatches).IsEqualTo(0);
            await Assert.That(drained.InFlightInitiationDrained).IsTrue();
        }
        finally
        {
            continueSubmit.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await Assert.That(target.Runs.Count).IsEqualTo(1);
    }

    [Test]
    public async Task PauseRefusesInFlightAdmissionAtItsDecision()
    {
        using var fixture = new StoreFixture();
        using var admissionStore = fixture.Initialize();
        var proposerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProposer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var freshProposerCalls = 0;
        var admission = Task.Run(() => admissionStore.AdmitSources(ContractIngressTests.Reference,
            [ContractIngressTests.Primary()], input =>
            {
                proposerStarted.SetResult();
                releaseProposer.Task.GetAwaiter().GetResult();
                return ContractIngressTests.Propose(input);
            }, ContractIngressTests.PullRequest));

        try
        {
            await proposerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var observer = fixture.Open();
            var paused = observer.PauseInstallation();
            await Assert.That(paused.IsPaused).IsTrue();
            await Assert.That(paused.InFlightInitiationDrained).IsTrue();

            await Assert.That(() => observer.AdmitSources(ContractIngressTests.Reference,
                [ContractIngressTests.Primary("fresh admission"u8.ToArray())], _ =>
                {
                    Interlocked.Increment(ref freshProposerCalls);
                    return ContractIngressTests.Propose(_);
                }, ContractIngressTests.PullRequest)).Throws<InstallationPaused>();
            await Assert.That(freshProposerCalls).IsEqualTo(0);

            releaseProposer.SetResult();
            await Assert.That(async () => await admission.WaitAsync(TimeSpan.FromSeconds(10))).Throws<InstallationPaused>();
            var retained = observer.History(ContractIngressTests.Reference).Single();
            await Assert.That(retained.Decision).IsNull();

            observer.ReleaseInstallation();
            await Assert.That(observer.Admit(retained.Revision.ContractRevisionId).Admitted).IsTrue();
        }
        finally
        {
            releaseProposer.TrySetResult();
            try { await admission.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (InstallationPaused) { }
        }
    }

    [Test]
    public async Task PausedInstallationStillPermitsCorrelatedRecoveryAndResultCapture()
    {
        using var fixture = new CompletionFixture();
        var submitted = await fixture.Dispatch();
        var paused = fixture.Store.PauseInstallation();
        await Assert.That(paused.IsPaused).IsTrue();

        var invocation = new Invocation(fixture.Store, new InvocationTarget(fixture.Origin));
        var resumed = await invocation.ResumeAsync(fixture.Attempt.ContractRevisionId);
        await Assert.That(resumed.Submissions.Single().RunId).IsEqualTo(submitted.RunId);
        await Assert.That(fixture.Target.Connections).IsEqualTo(0);

        var completion = await fixture.Wait();
        await Assert.That(completion.Outcome).IsEqualTo("SUCCEEDED");
        await Assert.That((await SettledStatus(fixture.Store)).InFlightInitiationDrained).IsTrue();
    }

    [Test]
    public async Task PausedInstallationBlocksFreshAllocationAndPreparation()
    {
        using var fixture = new HttpFixture();
        var paused = fixture.Store.PauseInstallation();

        await Assert.That(() => fixture.Store.AdmitHttpAttempt(fixture.Attempt.ContractRevisionId, fixture.Git.Repository, "main"))
            .Throws<InstallationPaused>();
        await Assert.That(() => fixture.Prepare()).Throws<InstallationPaused>();
        await Assert.That(fixture.Store.FindSubmission(fixture.Attempt.AttemptId)).IsNull();
        await Assert.That(paused.IsPaused).IsTrue();
    }

    [Test]
    public async Task PausedInstallationAllowsExplicitReplacementAllocationAndPreparation()
    {
        using var fixture = new HttpFixture();
        var store = fixture.Store;
        var original = fixture.Attempt;
        fixture.Prepare();
        await ReplacementTests.SafeRetire(store, original);
        store.PauseInstallation();

        var successor = store.AdmitRetry(original.AttemptId, "paused-replacement");
        var prepared = store.PrepareRetry(original.AttemptId, "paused-replacement");
        await Assert.That(prepared.AttemptId).IsEqualTo(successor.AttemptId);
        await Assert.That(prepared.State).IsEqualTo("prepared");
        await Assert.That(async () => await store.DispatchHttpAsync(successor.AttemptId, HttpDispatchTests.Credentials()))
            .Throws<InstallationPaused>();
        await Assert.That(store.GetInstallationStatus().IsPaused).IsTrue();
    }

    [Test]
    public async Task InterruptedDispatchRetainsUncertaintyWithoutActiveInitiationUntilReplayCorrelatesIt()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        target.Submit = _ => Task.FromResult((503, """{"code":"target.unavailable","message":"unavailable"}"""));
        await Assert.That(async () => await fixture.Store.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials()))
            .Throws<NativeTransportError>();
        target.Submit = body => Task.FromResult(target.Accept(body));

        using var reopened = fixture.Git.State.Open();
        reopened.PauseInstallation();
        var paused = await SettledStatus(reopened);
        await Assert.That(paused.UnresolvedDispatches).IsEqualTo(1);
        await Assert.That(paused.InFlightInitiationDrained).IsTrue();
        await Assert.That(async () => await reopened.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials()))
            .Throws<InstallationPaused>();

        reopened.ReleaseInstallation();
        fixture.Git.State.Execute("CREATE TRIGGER correlation_failure BEFORE UPDATE ON native_submissions WHEN NEW.state = 'correlated' BEGIN SELECT RAISE(ABORT, 'correlation failure'); END;");
        try
        {
            await Assert.That(async () => await reopened.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials()))
                .Throws<Microsoft.Data.Sqlite.SqliteException>();
            var unresolved = await SettledStatus(reopened);
            await Assert.That(unresolved.UnresolvedDispatches).IsEqualTo(1);
            await Assert.That(unresolved.InFlightInitiationDrained).IsTrue();
        }
        finally
        {
            fixture.Git.State.Execute("DROP TRIGGER correlation_failure;");
        }

        var correlated = await reopened.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials());
        await Assert.That(correlated.RunId).IsEqualTo(prepared.IntendedRunId);
        await Assert.That(target.Runs.Count).IsEqualTo(1);
        reopened.PauseInstallation();
        var drained = await SettledStatus(reopened);
        await Assert.That(drained.UnresolvedDispatches).IsEqualTo(0);
        await Assert.That(drained.InFlightInitiationDrained).IsTrue();
    }

    [Test]
    public async Task FailedReleaseLeavesPausePersistedAcrossReopen()
    {
        using var fixture = new StoreFixture();
        using (var store = fixture.Initialize())
        {
            store.PauseInstallation();
            fixture.Execute("CREATE TRIGGER release_failure BEFORE UPDATE ON installation_control WHEN NEW.admission_dispatch_paused = 0 BEGIN SELECT RAISE(ABORT, 'release failure'); END;");
            await Assert.That(() => store.ReleaseInstallation()).Throws<Microsoft.Data.Sqlite.SqliteException>();
            fixture.Execute("DROP TRIGGER release_failure;");
        }

        using var reopened = fixture.Open();
        await Assert.That(reopened.GetInstallationStatus().IsPaused).IsTrue();
        await Assert.That(reopened.ReleaseInstallation().IsPaused).IsFalse();
    }

    /// <summary>
    /// A concurrent fork in this test host briefly shares every open lock description until its
    /// exec closes it, so one undrained reading is conservative and may be transient.
    /// </summary>
    internal static async Task<InstallationStatus> SettledStatus(BroodlingStore store)
    {
        var clock = Stopwatch.StartNew();
        var status = store.GetInstallationStatus();
        while (!status.InFlightInitiationDrained && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20);
            status = store.GetInstallationStatus();
        }
        return status;
    }
}
