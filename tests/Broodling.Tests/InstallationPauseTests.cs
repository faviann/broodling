using Broodling.Host;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class InstallationPauseTests
{
    [Test]
    public async Task PausePersistsAcrossReopenBlocksAdmissionAndDispatchUntilExplicitRelease()
    {
        using var fixture = new NativeFixture();
        AttemptRecord attempt;
        using (var store = fixture.Git.State.Open())
        {
            attempt = fixture.Provision(store);
            store.PrepareSubmission(attempt.AttemptId, fixture.Profile);
            var paused = store.PauseInstallation();

            await Assert.That(paused.IsPaused).IsTrue();
            await Assert.That(paused.InFlightInitiationDrained).IsTrue();
            await Assert.That(() => store.AdmitSources(ContractIngressTests.Reference,
                [ContractIngressTests.Primary("paused admission"u8.ToArray())], ContractIngressTests.Propose, []))
                .Throws<InstallationPaused>();
            await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile, new ControlledTransport()))
                .Throws<InstallationPaused>();
        }

        using var reopened = fixture.Git.State.Open();
        await Assert.That(reopened.GetInstallationStatus().IsPaused).IsTrue();
        var released = reopened.ReleaseInstallation();
        await Assert.That(released.IsPaused).IsFalse();
        var submitted = await reopened.DispatchAsync(attempt.AttemptId, fixture.Profile, new ControlledTransport());
        await Assert.That(submitted.State).IsEqualTo("correlated");
    }

    [Test]
    public async Task OperatorCommandsExposePauseStatusAndExplicitRelease()
    {
        using var fixture = new NativeFixture();
        var output = new StringWriter();
        var error = new StringWriter();
        var path = fixture.Git.State.Path;

        await Assert.That(StoreCommands.Run(["pause-installation", path], fixture.Git.State.Application, output, error)).IsEqualTo(0);
        await Assert.That(output.ToString()).Contains("\"isPaused\":true");
        output.GetStringBuilder().Clear();
        await Assert.That(StoreCommands.Run(["installation-status", path], fixture.Git.State.Application, output, error)).IsEqualTo(0);
        await Assert.That(output.ToString()).Contains("\"isPaused\":true");
        output.GetStringBuilder().Clear();
        await Assert.That(StoreCommands.Run(["release-installation", path], fixture.Git.State.Application, output, error)).IsEqualTo(0);
        await Assert.That(output.ToString()).Contains("\"isPaused\":false");
    }

    [Test]
    public async Task PauseRetainsDispatchEvidenceAcrossExternalCallUntilLateCorrelation()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueSubmit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ControlledTransport
        {
            Submit = async (_, _) =>
            {
                started.SetResult();
                return await continueSubmit.Task;
            }
        };

        var pending = store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var observer = fixture.Git.State.Open();
            var paused = observer.PauseInstallation();
            await Assert.That(paused.IsPaused).IsTrue();
            await Assert.That(paused.OutstandingDispatches).IsEqualTo(1);
            await Assert.That(paused.UnresolvedDispatches).IsEqualTo(1);
            await Assert.That(paused.InFlightInitiationDrained).IsFalse();
            await Assert.That(async () => await observer.DispatchAsync(attempt.AttemptId, fixture.Profile, new ControlledTransport()))
                .Throws<InstallationPaused>();

            continueSubmit.SetResult("late-run");
            await Assert.That((await pending).State).IsEqualTo("correlated");
            var drained = observer.GetInstallationStatus();
            await Assert.That(drained.IsPaused).IsTrue();
            await Assert.That(drained.InFlightInitiationDrained).IsTrue();
        }
        finally
        {
            continueSubmit.TrySetResult("test-cleanup-run");
            try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException) when (!pending.IsCompleted) { }
        }
    }

    [Test]
    public async Task PauseObservesInFlightAdmissionAndLetsItDrainAfterPause()
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
            }, []));

        try
        {
            await proposerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var observer = fixture.Open();
            var paused = observer.PauseInstallation();
            await Assert.That(paused.IsPaused).IsTrue();
            await Assert.That(paused.OutstandingAdmissions).IsEqualTo(1);
            await Assert.That(paused.InFlightInitiationDrained).IsFalse();

            await Assert.That(() => observer.AdmitSources(ContractIngressTests.Reference,
                [ContractIngressTests.Primary("fresh admission"u8.ToArray())], _ =>
                {
                    Interlocked.Increment(ref freshProposerCalls);
                    return ContractIngressTests.Propose(_);
                }, [])).Throws<InstallationPaused>();
            await Assert.That(freshProposerCalls).IsEqualTo(0);

            releaseProposer.SetResult();
            var completed = await admission.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(completed.Decision!.Admitted).IsTrue();
            var drained = observer.GetInstallationStatus();
            await Assert.That(drained.IsPaused).IsTrue();
            await Assert.That(drained.OutstandingAdmissions).IsEqualTo(0);
            await Assert.That(drained.InFlightInitiationDrained).IsTrue();
        }
        finally
        {
            releaseProposer.TrySetResult();
            await admission.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Test]
    public async Task PausedInstallationStillPermitsCorrelatedRecoveryAndResultCapture()
    {
        using var fixture = new CompletionFixture();
        var submitted = await fixture.Dispatch();
        var paused = fixture.Store.PauseInstallation();
        await Assert.That(paused.IsPaused).IsTrue();

        var invocation = new Invocation(fixture.Store, fixture.Git.Workspaces, fixture.Profile, fixture.Transport);
        var resumed = await invocation.ResumeAsync(fixture.Attempt.ContractRevisionId);
        await Assert.That(resumed.Submissions.Single().RunId).IsEqualTo(submitted.RunId);
        await Assert.That(fixture.Transport.Calls).IsEqualTo(1);

        var completion = await fixture.Wait();
        await Assert.That(completion.Outcome).IsEqualTo("SUCCEEDED");
        await Assert.That(fixture.Store.GetInstallationStatus().InFlightInitiationDrained).IsTrue();
    }

    [Test]
    public async Task PausedInstallationBlocksFreshPreparation()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var original = fixture.Git.Admit(store, revision: "main");
        var paused = store.PauseInstallation();

        await Assert.That(() => store.AdmitAttempt(original.ContractRevisionId, fixture.Git.Repository, fixture.Git.Workspaces, "main"))
            .Throws<InstallationPaused>();
        await Assert.That(() => store.ProvisionAttempt(original.AttemptId)).Throws<InstallationPaused>();
        await Assert.That(() => store.PrepareSubmission(original.AttemptId, fixture.Profile)).Throws<InstallationPaused>();

        await Assert.That(paused.IsPaused).IsTrue();
    }

    [Test]
    public async Task PausedInstallationAllowsExplicitReplacementAllocationAndPreparation()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var original = fixture.Git.Admit(store, revision: "main");
        await SafeRetire(store, original);
        store.PauseInstallation();

        var successor = store.AdmitRetry(original.AttemptId, "paused-replacement", fixture.Git.Workspaces, fixture.Profile);
        var prepared = store.PrepareRetry(original.AttemptId, "paused-replacement", fixture.Git.Workspaces, fixture.Profile);
        await Assert.That(prepared.State).IsEqualTo("prepared");
        await Assert.That(async () => await store.DispatchAsync(successor.AttemptId, fixture.Profile, new ControlledTransport()))
            .Throws<InstallationPaused>();
        await Assert.That(store.GetInstallationStatus().IsPaused).IsTrue();
    }

    [Test]
    public async Task PausedInstallationRetainsUnresolvedInitiationAfterReopenWithoutOwnerReaping()
    {
        using var fixture = new NativeFixture();
        AttemptRecord attempt;
        using (var store = fixture.Git.State.Open())
        {
            attempt = fixture.Provision(store);
            await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile,
                new ControlledTransport { Submit = (_, _) => throw new NativeTransportError() }))
                .Throws<NativeTransportError>();
            await Assert.That(store.GetInstallationStatus().InFlightInitiationDrained).IsFalse();
        }

        using var reopened = fixture.Git.State.Open();
        var status = reopened.PauseInstallation();
        await Assert.That(status.IsPaused).IsTrue();
        await Assert.That(status.OutstandingDispatches).IsEqualTo(1);
        await Assert.That(status.UnresolvedDispatches).IsEqualTo(1);
        await Assert.That(status.InFlightInitiationDrained).IsFalse();
        await Assert.That(reopened.GetInstallationStatus().InFlightInitiationDrained).IsFalse();
        await Assert.That(async () => await reopened.DispatchAsync(attempt.AttemptId, fixture.Profile, new ControlledTransport()))
            .Throws<InstallationPaused>();
        await Assert.That(reopened.ReleaseInstallation().IsPaused).IsFalse();
        await Assert.That((await reopened.DispatchAsync(attempt.AttemptId, fixture.Profile, new ControlledTransport())).State)
            .IsEqualTo("correlated");
        await Assert.That(reopened.GetInstallationStatus().OutstandingDispatches).IsEqualTo(1);
    }

    [Test]
    public async Task FailedReleaseLeavesPausePersistedAcrossReopen()
    {
        using var fixture = new NativeFixture();
        using (var store = fixture.Git.State.Open())
        {
            store.PauseInstallation();
            fixture.Git.State.Execute("CREATE TRIGGER release_failure BEFORE UPDATE ON installation_control WHEN NEW.admission_dispatch_paused = 0 BEGIN SELECT RAISE(ABORT, 'release failure'); END;");
            await Assert.That(() => store.ReleaseInstallation()).Throws<Microsoft.Data.Sqlite.SqliteException>();
            fixture.Git.State.Execute("DROP TRIGGER release_failure;");
        }

        using var reopened = fixture.Git.State.Open();
        await Assert.That(reopened.GetInstallationStatus().IsPaused).IsTrue();
        await Assert.That(reopened.ReleaseInstallation().IsPaused).IsFalse();
    }

    [Test]
    public async Task FailedInitiationCleanupLeavesOutstandingEvidence()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        fixture.Git.State.Execute("CREATE TRIGGER initiation_cleanup_failure BEFORE DELETE ON installation_initiations BEGIN SELECT RAISE(ABORT, 'cleanup failure'); END;");
        _ = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()], ContractIngressTests.Propose, []);
        await Assert.That(store.GetInstallationStatus().InFlightInitiationDrained).IsFalse();
        fixture.Git.State.Execute("DROP TRIGGER initiation_cleanup_failure;");
        await Assert.That(store.GetInstallationStatus().OutstandingAdmissions).IsEqualTo(1);
    }

    private static async Task SafeRetire(BroodlingStore store, AttemptRecord attempt)
    {
        store.AbandonAttempt(attempt.AttemptId, "maintenance test");
        await store.StopAsync(attempt.AttemptId, "maintenance test", new ControlledTransport());
        store.RetireAttempt(attempt.AttemptId);
    }
}
