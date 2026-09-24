using System.Text.Json;
using System.Text.Json.Nodes;
using Broodling.Host;
using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

internal sealed class CompletionFixture : IDisposable
{
    internal AttemptFixture Git { get; } = new();
    internal BroodlingStore Store { get; }
    internal AttemptRecord Attempt { get; }
    internal ControlledTransport Transport { get; } = new();
    internal NativeProfile Profile { get; }
    internal CompletionFixture()
    {
        Store = Git.State.Open();
        Git.Git("remote", "add", "origin", "https://github.com/acme/widget.git");
        var status = Store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()], ContractIngressTests.Propose,
            [new("pr", "Open PR", "pull_request", "main")]);
        Attempt = Store.ProvisionAttempt(Store.AdmitAttempt(status.Revision.ContractRevisionId, Git.Repository, Git.Workspaces).AttemptId);
        Profile = new(Path.Combine(Git.State.Root, "native"), directOrigin: "http://127.0.0.1:8123");
        Transport.Wait = (_, run, _) => Task.FromResult(new NativeResult(run, true, Receipt(), null));
    }
    internal Task<NativeSubmission> Dispatch() => Store.DispatchAsync(Attempt.AttemptId, Profile, Transport,
        new("test-only-token", NativeProfile.GatewayBaseUrl, "test-only-gateway"));
    internal static JsonElement Receipt(string id = "50") => JsonSerializer.SerializeToElement(new
    {
        version = "v1", mode = "pr", outcome = "opened", repository = "acme/widget", targetBranch = "main",
        headRevision = new string('b', 40), pullRequestId = id
    });
    internal Task<AttemptCompletion> Wait() => Store.WaitAsync(Attempt.AttemptId, Transport);
    public void Dispose() { Store.Dispose(); Git.Dispose(); }
}

public sealed class AttemptCompletionTests
{
    [Test]
    [Arguments("0")]
    [Arguments("000050")]
    [Arguments("9999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999")]
    public async Task DigitStringsAreNotNumbersAndRetainedCompletionNeedsNoWorkspaceOrNative(string pr)
    {
        using var fixture = new CompletionFixture();
        var submitted = await fixture.Dispatch();
        Directory.Move(fixture.Attempt.Allocation.WorktreePath, fixture.Attempt.Allocation.WorktreePath + "-unavailable");
        fixture.Transport.Wait = async (locator, run, _) =>
        {
            await Assert.That(locator).IsEqualTo(submitted.Locator);
            await Assert.That(run).IsEqualTo(submitted.RunId);
            return new(run, true, CompletionFixture.Receipt(pr), null);
        };
        var completed = await fixture.Wait();
        await Assert.That(completed.Outcome).IsEqualTo("SUCCEEDED");
        await Assert.That(completed.AcceptedRevision).IsEqualTo(new string('b', 40));
        await Assert.That(completed.DeliveryReceipt.GetProperty("pullRequestId").GetString()).IsEqualTo(pr);
        await Assert.That(fixture.Store.CurrentAttempt(completed.WorkUnitId)).IsNull();
        fixture.Transport.Wait = (_, _, _) => throw new Exception("Native unavailable");
        using var reopened = fixture.Git.State.Open();
        // Terminal replay is observation: an unrelated SQLite writer must not block it.
        using var writer = fixture.Git.State.Connect();
        using var held = writer.BeginTransaction(deferred: false);
        await Assert.That(await reopened.WaitAsync(completed.AttemptId, fixture.Transport)).IsEqualTo(completed);
        await Assert.That(reopened.FindCompletion(completed.AttemptId)).IsEqualTo(completed);
        await Assert.That(reopened.FindCompletion("unknown-attempt")).IsNull();
        await Assert.That(fixture.Transport.WaitCalls).IsEqualTo(1);
    }

    [Test]
    public async Task IncompleteForeignAndWrongTypeReceiptsRefuseWithoutLosingAuthority()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        var invalid = new List<string> { "null", "[]", "true", "42", "\"receipt\"", "{}" };
        var valid = JsonNode.Parse(CompletionFixture.Receipt().GetRawText())!.AsObject();
        foreach (var field in valid.Select(pair => pair.Key).ToArray())
        {
            var missing = valid.DeepClone().AsObject(); missing.Remove(field); invalid.Add(missing.ToJsonString());
            foreach (var value in new[] { "null", "1", "false", "[]", "{}" })
            {
                var changed = valid.DeepClone().AsObject(); changed[field] = JsonNode.Parse(value); invalid.Add(changed.ToJsonString());
            }
        }
        foreach (var (field, value) in new[] {
            ("version", "v2"), ("mode", "merge"), ("outcome", "closed"), ("repository", "other/project"),
            ("targetBranch", "release"), ("headRevision", fixture.Attempt.B1.CommitOid),
            ("headRevision", new string('A', 40)), ("headRevision", new string('g', 40)), ("headRevision", new string('b', 39)),
            ("pullRequestId", ""), ("pullRequestId", "-1"), ("pullRequestId", "+1"), ("pullRequestId", " 1"),
            ("pullRequestId", "1 "), ("pullRequestId", "١"), ("pullRequestId", "1.0"), ("pullRequestId", "1\0") })
        {
            var changed = valid.DeepClone().AsObject(); changed[field] = value; invalid.Add(changed.ToJsonString());
        }
        var extra = valid.DeepClone().AsObject(); extra["extra"] = "value"; invalid.Add(extra.ToJsonString());
        invalid.Add(valid.ToJsonString()[..^1] + ",\"version\":\"v1\"}");
        foreach (var json in invalid)
        {
            fixture.Transport.Wait = (_, run, _) => Task.FromResult(new NativeResult(run, true, JsonSerializer.Deserialize<JsonElement>(json), null));
            await Assert.That(async () => await fixture.Wait()).Throws<SubmissionConflict>();
            await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
            fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        }
    }

    [Test]
    public async Task UncorrelatedOrAbandonedAttemptsRefuseBeforeWaitAndForeignRunCannotAbandonOrComplete()
    {
        using var fixture = new CompletionFixture();
        await Assert.That(async () => await fixture.Wait()).Throws<SubmissionNotReady>();
        fixture.Store.PrepareSubmission(fixture.Attempt.AttemptId, fixture.Profile);
        await Assert.That(async () => await fixture.Wait()).Throws<SubmissionNotReady>();
        await Assert.That(fixture.Transport.WaitCalls).IsEqualTo(0);
        await fixture.Dispatch();
        foreach (var succeeded in new[] { true, false })
        {
            fixture.Transport.Wait = (_, _, _) => Task.FromResult(new NativeResult("foreign-run", succeeded, CompletionFixture.Receipt(), "runtime_lost"));
            await Assert.That(async () => await fixture.Wait()).Throws<SubmissionConflict>();
            fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        }
        fixture.Store.AbandonAttempt(fixture.Attempt.AttemptId, "operator decision");
        var calls = fixture.Transport.WaitCalls;
        await Assert.That(async () => await fixture.Wait()).Throws<StaleAttempt>();
        await Assert.That(fixture.Transport.WaitCalls).IsEqualTo(calls);
    }

    [Test]
    public async Task DetachingWaitIsRetryableButNativeFailureAbandonsWithoutCleanup()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        fixture.Transport.Wait = (_, _, ct) => Task.FromCanceled<NativeResult>(ct);
        await Assert.That(async () => await fixture.Store.WaitAsync(fixture.Attempt.AttemptId, fixture.Transport, cancellation.Token)).Throws<OperationCanceledException>();
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        fixture.Transport.Wait = (_, _, _) => throw new NativeTransportError();
        await Assert.That(async () => await fixture.Wait()).Throws<NativeTransportError>();
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        fixture.Transport.Wait = (_, run, _) => Task.FromResult(new NativeResult(run, false, default, "runtime_lost"));
        await Assert.That(async () => await fixture.Wait()).Throws<SubmissionNotReady>();
        await Assert.That(fixture.Store.GetAttempt(fixture.Attempt.AttemptId).Abandonment!.Reason).Contains("runtime_lost");
        await Assert.That(Directory.Exists(fixture.Attempt.Allocation.WorktreePath)).IsTrue();
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
    }

    [Test]
    public async Task AbandonmentWhileWaitingRejectsLateSuccessAndCompletionPreventsAbandonment()
    {
        using var fixture = new CompletionFixture();
        var submission = fixture.Store.SubmitIssue("https://github.com/acme/widget/issues/12");
        fixture.Store.AssociateIssueSubmission(submission.SubmissionId, fixture.Attempt.ContractRevisionId);
        await fixture.Dispatch();
        fixture.Transport.Wait = (_, run, _) =>
        {
            using var other = fixture.Git.State.Open();
            var stop = new ControlledTransport { Stop = (_, stopRun, _) =>
            {
                if (stopRun != run) throw new Exception("Cancellation stop used the wrong native run");
                return Task.FromResult(new NativeResult(stopRun, true, default, null));
            } };
            CessationUnconfirmed? cancellationStop = null;
            try
            {
                other.CancelIssueSubmissionAsync(submission.SubmissionId, "stop won", stop)
                    .GetAwaiter().GetResult();
            }
            catch (CessationUnconfirmed error)
            {
                cancellationStop = error;
            }
            if (cancellationStop is null || !cancellationStop.NativeStopRequested)
                throw new Exception("Cancellation did not commit exact abandonment before native stop");
            if (other.GetIssueSubmission(submission.SubmissionId).Cancellation!.AttemptId != fixture.Attempt.AttemptId)
                throw new Exception("Cancellation did not retain the exact Attempt");
            return Task.FromResult(new NativeResult(run, true, CompletionFixture.Receipt(), null));
        };
        await Assert.That(async () => await fixture.Wait()).Throws<StaleAttempt>();
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
        await Assert.That(fixture.Store.GetIssueSubmission(submission.SubmissionId).State).IsEqualTo("cancelled");
        using var winner = new CompletionFixture();
        var winnerSubmission = winner.Store.SubmitIssue("https://github.com/acme/widget/issues/12");
        winner.Store.AssociateIssueSubmission(winnerSubmission.SubmissionId, winner.Attempt.ContractRevisionId);
        await winner.Dispatch();
        await winner.Wait();
        await Assert.That(async () => await winner.Store.CancelIssueSubmissionAsync(winnerSubmission.SubmissionId, "too late"))
            .Throws<IssueSubmissionConflict>();
        await Assert.That(() => winner.Store.AbandonAttempt(winner.Attempt.AttemptId, "too late")).Throws<StaleAttempt>();
        await Assert.That(() => winner.Git.State.Execute($"INSERT INTO attempt_abandonments VALUES ('{winner.Attempt.AttemptId}', 'too late', 'now')")).Throws<SqliteException>();
    }

    [Test]
    public async Task FailedAuthorityUpdateRollsBackReceiptDispositionAndCurrentnessThenRetrySucceeds()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        // Fail after insertion, inside the completion trigger's authority update.
        fixture.Git.State.Execute("CREATE TRIGGER fixture_failure BEFORE UPDATE ON attempts BEGIN SELECT RAISE(ABORT, 'fixture write failure'); END;");
        await Assert.That(async () => await fixture.Wait()).Throws<SqliteException>();
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        fixture.Git.State.Execute("DROP TRIGGER fixture_failure");
        await Assert.That((await fixture.Wait()).Outcome).IsEqualTo("SUCCEEDED");
    }

    [Test]
    public async Task IndependentFinalizersConvergeAndNeverHoldWriterWhileAwaitingNative()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        using var other = fixture.Git.State.Open();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        fixture.Transport.Wait = async (_, run, _) =>
        {
            if (Interlocked.Increment(ref entered) == 2) ready.SetResult();
            await ready.Task;
            return new(run, true, CompletionFixture.Receipt(), null);
        };
        var both = await Task.WhenAll(fixture.Wait(), other.WaitAsync(fixture.Attempt.AttemptId, fixture.Transport));
        await Assert.That(both[0]).IsEqualTo(both[1]);
        await Assert.That(fixture.Transport.WaitCalls).IsEqualTo(2);
        await Assert.That(fixture.Store.Status(fixture.Attempt.ContractRevisionId).Completions.Count).IsEqualTo(1);
    }

    [Test]
    public async Task FrozenInvocationIsValidatedBeforeWaitAndAgainBeforeFinalWrite()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        var submitted = fixture.Store.FindSubmission(fixture.Attempt.AttemptId)!;
        fixture.Git.State.Execute("DROP TRIGGER submission_binding_stable");
        void Change(string request)
        {
            using var connection = fixture.Git.State.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE native_submissions SET request_json = $request";
            command.Parameters.AddWithValue("$request", request); command.ExecuteNonQuery();
        }
        var changed = JsonNode.Parse(submitted.RequestJson)!; changed["task"] = "Foreign Contract";
        Change(changed.ToJsonString());
        await Assert.That(async () => await fixture.Wait()).Throws<SubmissionConflict>();
        await Assert.That(fixture.Transport.WaitCalls).IsEqualTo(0);
        Change(submitted.RequestJson);
        fixture.Transport.Wait = (_, run, _) =>
        {
            Change(changed.ToJsonString());
            return Task.FromResult(new NativeResult(run, true, CompletionFixture.Receipt(), null));
        };
        await Assert.That(async () => await fixture.Wait()).Throws<SubmissionConflict>();
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
    }

    [Test]
    public async Task NoEffectSuccessCannotManufactureStableLocalResult()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var transport = new ControlledTransport { Wait = (_, run, _) => Task.FromResult(new NativeResult(run, true, JsonSerializer.Deserialize<JsonElement>("null"), null)) };
        await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport);
        await Assert.That(async () => await store.WaitAsync(attempt.AttemptId, transport)).Throws<SubmissionNotReady>();
        store.RequireCurrentAttempt(attempt.AttemptId);
        await Assert.That(store.FindCompletion(attempt.AttemptId)).IsNull();
    }
}
