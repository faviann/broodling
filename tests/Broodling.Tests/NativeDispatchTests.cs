using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class NativeDispatchTests
{
    [Test]
    public async Task FrozenTaskCarriesExactTextBinaryContractAndOriginalB1AcrossNewCaptureAndReopen()
    {
        using var fixture = new NativeFixture();
        var text = "Complete authority — café\r\nNo newline normalization.\r\n";
        NativeSubmission prepared;
        AttemptRecord attempt;
        using (var store = fixture.Git.State.Open())
        {
            var status = store.AdmitSources(ContractIngressTests.Reference,
                [ContractIngressTests.Primary(Encoding.UTF8.GetBytes(text)), ContractIngressTests.Supplement], ContractIngressTests.Propose, []);
            attempt = store.ProvisionAttempt(store.AdmitAttempt(status.Revision.ContractRevisionId, fixture.Git.Repository, fixture.Git.Workspaces).AttemptId);
            prepared = store.PrepareSubmission(attempt.AttemptId, fixture.Profile);
            var request = JsonNode.Parse(prepared.RequestJson)!;
            var task = (string)request["task"]!;
            var authority = JsonNode.Parse(task[(task.IndexOf("\n\n", StringComparison.Ordinal) + 2)..])!;
            await Assert.That(authority["comparisonBase"]!.GetValue<string>()).IsEqualTo(fixture.Git.Head);
            await Assert.That(JsonNode.DeepEquals(authority["contract"], JsonNode.Parse(status.Revision.CanonicalBytes))).IsTrue();
            var sources = authority["admittedInstructions"]!.AsArray();
            await Assert.That((string)sources.Single(value => (string?)value!["kind"] == "primary_issue")!["content"]!).IsEqualTo(text);
            var binary = sources.Single(value => (string?)value!["kind"] == "referenced_document")!;
            await Assert.That((string)binary["encoding"]!).IsEqualTo("base64");
            await Assert.That(Convert.FromBase64String((string)binary["content"]!).SequenceEqual(ContractIngressTests.Supplement.Content)).IsTrue();
            store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary("newer source"u8.ToArray())], ContractIngressTests.Propose, []);
        }
        using var reopened = fixture.Git.State.Open();
        var transport = new ControlledTransport { Submit = request =>
        {
            if (request != prepared.RequestJson) throw new Exception("Replay changed request bytes");
            return Task.FromResult("run-original");
        } };
        var correlated = await reopened.DispatchAsync(attempt.AttemptId, fixture.Profile, transport);
        await Assert.That(correlated.RequestJson).IsEqualTo(prepared.RequestJson);
        await Assert.That(correlated.SubmissionKey).IsEqualTo(prepared.SubmissionKey);
        await Assert.That(correlated.RunId).IsEqualTo("run-original");
        await Assert.That(correlated.RequestJson.Contains("AUTH_NEVER_PERSIST")).IsFalse();
    }

    [Test]
    public async Task CommittedIntentReleasesWriterAndLateAcknowledgmentRetainsCorrelationWithoutAuthority()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var issue = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        store.AssociateIssueSubmission(issue.SubmissionId, fixture.Git.RevisionId);
        var attempt = fixture.Provision(store);
        var stopCalls = new List<(NativeLocator Locator, string RunId)>();
        var cancellationStop = new ControlledTransport { Stop = (_, _, _) =>
            throw new Exception("The unresolved dispatch intent has no native run to stop") };
        var cancellationCessation = (CessationUnconfirmed?)null;
        var cancellationCommitted = false;
        var transport = new ControlledTransport { Submit = async _ =>
        {
            using var independent = fixture.Git.State.Open();
            await Assert.That(independent.FindSubmission(attempt.AttemptId)!.State).IsEqualTo("dispatched");
            independent.ResolveWorkUnit(WorkReference.Parse("unrelated/project", 123));
            try
            {
                await independent.CancelIssueSubmissionAsync(issue.SubmissionId,
                    "during external acknowledgment", cancellationStop);
            }
            catch (CessationUnconfirmed error)
            {
                cancellationCessation = error;
            }
            await Assert.That(cancellationCessation).IsNotNull();
            await Assert.That(cancellationCessation!.NativeStopRequested).IsFalse();
            var cancelled = independent.GetIssueSubmission(issue.SubmissionId);
            await Assert.That(cancelled.State).IsEqualTo("cancelled");
            await Assert.That(cancelled.Cancellation!.AttemptId).IsEqualTo(attempt.AttemptId);
            await Assert.That(independent.GetAttempt(attempt.AttemptId).Abandonment!.Reason)
                .IsEqualTo("during external acknowledgment");
            cancellationCommitted = true;
            return "late-native-run";
        }, Stop = (locator, runId, _) =>
        {
            if (!cancellationCommitted) throw new Exception("Native stop ran before cancellation committed");
            stopCalls.Add((locator, runId));
            return Task.FromResult(new NativeResult(runId, true, default, null));
        } };
        StaleAttempt? stale = null;
        try { await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport); }
        catch (StaleAttempt error) { stale = error; }
        await Assert.That(stale).IsNotNull();
        await Assert.That(stale!.NativeStopRequested).IsTrue();
        using var reopened = fixture.Git.State.Open();
        var recovered = reopened.GetIssueSubmission(issue.SubmissionId);
        await Assert.That(recovered.Cancellation!.AttemptId).IsEqualTo(attempt.AttemptId);
        await Assert.That(reopened.FindSubmission(attempt.AttemptId)!.RunId).IsEqualTo("late-native-run");
        await Assert.That(reopened.GetAttempt(attempt.AttemptId).IsCurrent).IsFalse();
        await Assert.That(async () => await reopened.DispatchAsync(attempt.AttemptId, fixture.Profile, transport)).Throws<StaleAttempt>();
        await Assert.That(transport.Calls).IsEqualTo(1);
        await Assert.That(stopCalls.Count).IsEqualTo(1);
        await Assert.That(stopCalls[0].RunId).IsEqualTo("late-native-run");
        await Assert.That(stopCalls[0].Locator).IsEqualTo(reopened.FindSubmission(attempt.AttemptId)!.Locator);
    }

    [Test]
    public async Task MissingEnclosureStopDoesNotClaimNativeStopWasRequested()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var transport = new ControlledTransport
        {
            Submit = _ => Task.FromResult("known-run"),
            Stop = (_, _, _) => throw new InvalidOperationException("Stop must not be reached")
        };
        await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport);
        Directory.Delete(attempt.Allocation.Enclosure, recursive: true);

        CessationUnconfirmed? cessation = null;
        try { await store.StopAsync(attempt.AttemptId, "missing enclosure", transport); }
        catch (CessationUnconfirmed error) { cessation = error; }
        await Assert.That(cessation).IsNotNull();
        await Assert.That(cessation!.NativeStopRequested).IsFalse();
        await Assert.That(transport.StopCalls).IsEqualTo(0);
    }

    [Test]
    public async Task LostAcknowledgmentThenAbandonmentNeverReplaysToDiscoverRun()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var transport = new ControlledTransport { Submit = _ => throw new NativeTransportError() };
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport)).Throws<NativeTransportError>();
        store.AbandonAttempt(attempt.AttemptId, "lost acknowledgment");
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport)).Throws<StaleAttempt>();
        await Assert.That(transport.Calls).IsEqualTo(1);
        await Assert.That(store.FindSubmission(attempt.AttemptId)!.State).IsEqualTo("dispatched");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConcurrentCallersConvergeOrRefuseDifferentIdentities(bool different)
    {
        using var fixture = new NativeFixture();
        using var first = fixture.Git.State.Open();
        var attempt = fixture.Provision(first);
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = "";
        var slow = new ControlledTransport { Submit = request => { captured = request; return gate.Task; } };
        var pending = first.DispatchAsync(attempt.AttemptId, fixture.Profile, slow);
        using var second = fixture.Git.State.Open();
        var fast = new ControlledTransport { Submit = request =>
        {
            if (request != captured) throw new Exception("Concurrent request differs");
            return Task.FromResult("one-native-run");
        } };
        var result = await second.DispatchAsync(attempt.AttemptId, fixture.Profile, fast);
        gate.SetResult(different ? "other-run" : "one-native-run");
        if (different) await Assert.That(async () => await pending).Throws<SubmissionConflict>();
        else await Assert.That((await pending).RunId).IsEqualTo(result.RunId);
        await Assert.That(first.FindSubmission(attempt.AttemptId)!.RunId).IsEqualTo("one-native-run");
    }

    [Test]
    [Arguments("head", "old-run", true)]
    [Arguments("dirty", "old-run", false)]
    [Arguments("unchanged", "old-run", false)]
    [Arguments("head", "", false)]
    public async Task ConflictRecoveryRequiresOwnedHeadDriftAndPublicIdentity(string mutation, string conflictId, bool recover)
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var transport = new ControlledTransport { Submit = _ => throw new NativeTransportError() };
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport)).Throws<NativeTransportError>();
        if (mutation is "head" or "dirty") File.WriteAllText(Path.Combine(attempt.Allocation.WorktreePath, "original.txt"), "candidate progress\n");
        if (mutation == "head")
        {
            AttemptFixture.RunGit(attempt.Allocation.WorktreePath, "add", ".");
            AttemptFixture.RunGit(attempt.Allocation.WorktreePath, "commit", "-m", "native owned progress");
        }
        transport.Submit = _ => throw new SubmissionConflict("Indistinguishable native error", conflictId);
        if (recover) await Assert.That((await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport)).RunId).IsEqualTo(conflictId);
        else
        {
            await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport)).Throws<SubmissionConflict>();
            await Assert.That(store.FindSubmission(attempt.AttemptId)!.State).IsEqualTo("blocked");
            await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport)).Throws<SubmissionConflict>();
            await Assert.That(transport.Calls).IsEqualTo(2);
        }
    }

    [Test]
    [Arguments("head")]
    [Arguments("dirty")]
    [Arguments("checkout-policy")]
    public async Task PreparedRequestsStillRequireCleanSupportedB1(string mutation)
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        store.PrepareSubmission(attempt.AttemptId, fixture.Profile);
        if (mutation == "checkout-policy") fixture.Git.Git("config", "core.autocrlf", "true");
        else
        {
            File.WriteAllText(Path.Combine(attempt.Allocation.WorktreePath, "original.txt"), "changed");
            if (mutation == "head")
            {
                AttemptFixture.RunGit(attempt.Allocation.WorktreePath, "add", ".");
                AttemptFixture.RunGit(attempt.Allocation.WorktreePath, "commit", "-m", "changed");
            }
        }
        var transport = new ControlledTransport();
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport)).Throws<BroodlingException>();
        await Assert.That(transport.Calls).IsEqualTo(0);
        await Assert.That(store.FindSubmission(attempt.AttemptId)!.State).IsEqualTo("prepared");
    }

    [Test]
    [Arguments("origin")]
    [Arguments("marker")]
    [Arguments("branch")]
    [Arguments("missing")]
    public async Task DispatchedReplayRefusesChangedOwnershipOrSourceWithoutReprovisioning(string mutation)
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var transport = new ControlledTransport { Submit = _ => throw new NativeTransportError() };
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport)).Throws<NativeTransportError>();
        switch (mutation)
        {
            case "origin": fixture.Git.Git("remote", "set-url", "origin", "https://github.com/other/repo.git"); break;
            case "marker": File.WriteAllText(Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.MarkerName), "foreign\n"); break;
            case "branch": AttemptFixture.RunGit(attempt.Allocation.WorktreePath, "checkout", "--detach"); break;
            case "missing": Directory.Delete(attempt.Allocation.WorktreePath, true); break;
        }
        var invocation = new Invocation(store, new InvocationTarget.Local(fixture.Git.Workspaces, fixture.Profile, transport));
        await Assert.That(async () => await invocation.ResumeAsync(attempt.ContractRevisionId)).Throws<BroodlingException>();
        await Assert.That(transport.Calls).IsEqualTo(1);
        await Assert.That(() => store.ProvisionAttempt(attempt.AttemptId)).Throws<SubmissionNotReady>();
        if (mutation == "missing") await Assert.That(Directory.Exists(attempt.Allocation.WorktreePath)).IsFalse();
    }

    [Test]
    public async Task CorrelationTransactionFailureRetainsIntentAndReopenedReplayUsesIdenticalKey()
    {
        using var fixture = new NativeFixture();
        AttemptRecord attempt;
        NativeSubmission dispatched;
        using (var store = fixture.Git.State.Open())
        {
            attempt = fixture.Provision(store);
            fixture.Git.State.Execute("CREATE TRIGGER fault_correlate BEFORE UPDATE ON native_submissions WHEN NEW.state = 'correlated' BEGIN SELECT RAISE(ABORT, 'controlled correlation fault'); END;");
            await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile, new ControlledTransport())).Throws<SqliteException>();
            dispatched = store.FindSubmission(attempt.AttemptId)!;
            await Assert.That(dispatched.State).IsEqualTo("dispatched");
            fixture.Git.State.Execute("DROP TRIGGER fault_correlate");
        }
        using var reopened = fixture.Git.State.Open();
        var result = await reopened.DispatchAsync(attempt.AttemptId, fixture.Profile, new ControlledTransport());
        await Assert.That(result.RequestJson).IsEqualTo(dispatched.RequestJson);
        await Assert.That(result.SubmissionKey).IsEqualTo(dispatched.SubmissionKey);
        using var again = fixture.Git.State.Open();
        await Assert.That(again.FindSubmission(attempt.AttemptId)).IsEqualTo(result);
        await Assert.That(() => fixture.Git.State.Execute("UPDATE native_submissions SET state = 'dispatched'" )).Throws<SqliteException>();
        await Assert.That(() => fixture.Git.State.Execute("DELETE FROM native_submissions" )).Throws<SqliteException>();
    }
}
