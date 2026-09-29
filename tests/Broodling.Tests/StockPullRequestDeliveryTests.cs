using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;
using static Broodling.Tests.StockDirectTargetTests;

namespace Broodling.Tests;

/// <summary>
/// The approved asset's pull-request delivery contract (<c>builtin.git-delivery.pr@2</c>, <c>v2</c>/<c>pr</c>/<c>ready</c>,
/// the <c>Consider</c> feedback policy) on the unmodified native in the actual DirectTarget image, driven only
/// through the application. Each case scripts the controlled forge (tests/fixtures/stock-target/gh) and asserts
/// native's observable behavior: what it asks the forge, what the delivery repair node receives, and the
/// run's end. It records the native and asset identities with native's forge requests in its output.
/// Provider, forge and PR receipt are controlled: this is not a live GitHub or semantic-quality result.
/// </summary>
public sealed class StockPullRequestDeliveryTests
{
    [Test]
    public async Task ReadyDespiteAFailingOptionalCheck()
    {
        await using var delivery = await ScriptedDelivery.StartAsync(new()
        {
            ["protection"] = new JsonObject { ["requiredStatusCheckContexts"] = new JsonArray("ci") },
            ["checks"] = new JsonArray(new JsonArray(Check("ci", true, "SUCCESS"), Check("lint", false, "FAILURE", 2))),
        });
        var completion = await delivery.ReadyAsync();
        // GitHub reports it UNSTABLE; only required checks gate readiness.
        await Assert.That(delivery.States()).IsEqualTo("UNSTABLE");
        await Assert.That(delivery.Repairs(completion.AcceptedRevision)).IsEmpty();
        await delivery.RecordAsync("ready: optional failing check does not block");
    }

    [Test]
    public async Task ReadyWhileRequiredHumanApprovalIsStillMissing()
    {
        await using var delivery = await ScriptedDelivery.StartAsync(new()
        {
            ["protection"] = new JsonObject { ["requiredApprovingReviewCount"] = 1 },
            ["reviewDecision"] = "REVIEW_REQUIRED",
        });
        var completion = await delivery.ReadyAsync();
        // Blocked only by the missing approval under a rule native's approval handoff permits.
        await Assert.That(delivery.States()).IsEqualTo("BLOCKED");
        await Assert.That(delivery.Repairs(completion.AcceptedRevision)).IsEmpty();
        await delivery.RecordAsync("ready: handed off without the required human approval");
    }

    /// <summary>Readiness native must keep waiting for: nothing it observes lets it hand off.</summary>
    public static IEnumerable<Func<PendingCase>> PendingScenarios() =>
    [
        () => new("required check in progress", new JsonObject
        {
            ["protection"] = new JsonObject { ["requiredStatusCheckContexts"] = new JsonArray("ci") },
            ["checks"] = new JsonArray(new JsonArray(Check("ci", true, null))),
        }),
        () => new("required check missing", new JsonObject
        {
            ["protection"] = new JsonObject { ["requiredStatusCheckContexts"] = new JsonArray("ci") },
        }),
        () => new("approval missing where conversation resolution is required", new JsonObject
        {
            ["protection"] = new JsonObject { ["requiredApprovingReviewCount"] = 1, ["requiresConversationResolution"] = true },
            ["reviewDecision"] = "REVIEW_REQUIRED",
        }),
    ];

    [Test]
    [MethodDataSource(nameof(PendingScenarios))]
    public async Task PendingReadinessWaitsUntilExplicitlyStoppedAndObservationInventsNoReceipt(PendingCase pending)
    {
        await using var delivery = await ScriptedDelivery.StartAsync(pending.Scenario);
        var (store, attempt) = (delivery.Store, delivery.AttemptId);
        // Native keeps polling at its 20-second interval without handing off.
        await delivery.TraceUntilAsync(trace => trace.Count(request => request.IsReadiness) >= 2);
        await Assert.That(delivery.States().Split(',').Distinct()).IsEquivalentTo(new[] { "BLOCKED" });
        await Assert.That(await store.ObserveAsync(attempt, null) is NativeObservation.Available
            { Progress: { Phase: "running", ActiveNodes: ["deliver"] } }).IsTrue();
        // An observer giving up neither completes nor abandons the Attempt.
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            await Assert.That(async () => await store.WaitAsync(attempt, null, timeout.Token)).Throws<OperationCanceledException>();
        await Assert.That(store.FindCompletion(attempt)).IsNull();
        await Assert.That(store.GetAttempt(attempt).Abandonment).IsNull();

        var stop = await Assert.That(async () => await store.StopAsync(attempt, "stop pending delivery", null)).Throws<CessationUnconfirmed>();
        await Assert.That(stop!.NativeStopRequested).IsTrue();
        await Assert.That(store.GetAttempt(attempt).Abandonment!.Reason).IsEqualTo("stop pending delivery");
        await Assert.That(store.FindCompletion(attempt)).IsNull();
        await Assert.That((await delivery.Target.LedgerAsync(delivery.RunId)).Failure).IsEqualTo("force_stopped");
        await delivery.RecordAsync($"pending ({pending.Name}) until stopped: force_stopped, no completion");
    }

    [Test]
    public async Task BehindHeadIsAdvancedByTheForgeThenReady()
    {
        // Main moves on without conflict once the PR opens; the branch rule requires up-to-date branches, so
        // GitHub reports the head behind.
        await using var delivery = await ScriptedDelivery.StartAsync(new()
        {
            ["onOpen"] = Change("UPSTREAM.md", "upstream change\n"),
            ["requireUpToDate"] = true,
        });
        var completion = await delivery.ReadyAsync();
        var readiness = delivery.Readiness();
        await Assert.That((string?)readiness[0].Observed!["mergeStateStatus"]).IsEqualTo("BEHIND");
        await Assert.That((string?)readiness[^1].Observed!["mergeStateStatus"]).IsEqualTo("CLEAN");
        var update = delivery.Target.Trace().Where(request => request.IsHeadUpdate).ToList();
        await Assert.That(update).Count().IsEqualTo(1);
        // The accepted head is the forge's own update of the published head with main, adopted by native.
        var accepted = completion.AcceptedRevision;
        await Assert.That((string?)update[0].Observed!["head"]).IsEqualTo(accepted);
        await Assert.That(delivery.Forge("rev-parse", accepted + "^1")).IsEqualTo((string?)readiness[0].Observed!["head"]);
        await Assert.That(delivery.Forge("rev-parse", accepted + "^2")).IsEqualTo(delivery.Forge("rev-parse", "main"));
        await Assert.That(delivery.Repairs(accepted)).IsEmpty();
        await delivery.RecordAsync("ready after the forge advanced the behind head");
    }

    [Test]
    public async Task FailedRequiredCheckIsRepairedThenReady()
    {
        await using var delivery = await ScriptedDelivery.StartAsync(new()
        {
            ["protection"] = new JsonObject { ["requiredStatusCheckContexts"] = new JsonArray("ci") },
            ["checks"] = new JsonArray(new JsonArray(Check("ci", true, "FAILURE", 101)), new JsonArray(Check("ci", true, "SUCCESS", 102))),
        });
        var completion = await delivery.ReadyAsync();
        var repairs = delivery.Repairs(completion.AcceptedRevision);
        await Assert.That(repairs).Count().IsEqualTo(1);
        var repair = repairs[0];
        await Assert.That((string?)repair["outcome"]).IsEqualTo("ci_failed");
        // The repair receives the failed check and the tail of its job log.
        await Assert.That((string)repair["deliveryFeedback"]!).Contains("ci concluded FAILURE")
            .And.Contains("##[error]job 101 failed");
        var readiness = delivery.Readiness();
        await Assert.That(readiness.Select(request => (string?)request.Observed!["head"]).Distinct()).Count().IsEqualTo(2);
        await Assert.That((string?)readiness[^1].Observed!["head"]).IsEqualTo(completion.AcceptedRevision);
        await delivery.RecordAsync("ready after ci_failed repair");
    }

    [Test]
    public async Task ConflictIsMaterializedRepairedThenReady()
    {
        // Main changes the candidate's file once the PR opens, so the published head conflicts.
        await using var delivery = await ScriptedDelivery.StartAsync(new() { ["onOpen"] = Change("README.md", "upstream README\n") });
        var completion = await delivery.ReadyAsync();
        var readiness = delivery.Readiness();
        await Assert.That((string?)readiness[0].Observed!["mergeStateStatus"]).IsEqualTo("DIRTY");
        var repairs = delivery.Repairs(completion.AcceptedRevision);
        await Assert.That(repairs).Count().IsEqualTo(1);
        var repair = repairs[0];
        await Assert.That((string?)repair["outcome"]).IsEqualTo("conflict");
        await Assert.That((string)repair["deliveryFeedback"]!).Contains("conflictedPaths=[\"README.md\"]");
        // The repaired head integrates the conflicting main.
        await Assert.That(delivery.Forge("merge-base", "main", completion.AcceptedRevision)).IsEqualTo(delivery.Forge("rev-parse", "main"));
        await delivery.RecordAsync("ready after conflict repair");
    }

    [Test]
    public async Task RepairIsBoundedByTheTenIterationLimit()
    {
        await using var delivery = await ScriptedDelivery.StartAsync(new()
        {
            ["protection"] = new JsonObject { ["requiredStatusCheckContexts"] = new JsonArray("ci") },
            ["checks"] = new JsonArray(new JsonArray(Check("ci", true, "FAILURE", 101))),
        });
        var abandonment = await delivery.FailsAsync();
        await Assert.That(abandonment).IsEqualTo("Zeroshot run failed: native_failed");
        // The stock loop's limit, not a deadline, ended the run: ten deliveries, each followed by a repair.
        var ledger = await delivery.Target.LedgerAsync(delivery.RunId);
        await Assert.That(ledger.Failure).IsEqualTo("change_attempts_exhausted");
        await Assert.That((ledger.Executions["deliver"], ledger.Executions["delivery_repair"])).IsEqualTo((10, 10));
        // Each delivery assessed a new repaired head whose required check failed; the next delivery published
        // each repair but the last.
        var readiness = delivery.Readiness();
        await Assert.That(readiness).Count().IsEqualTo(10);
        await Assert.That(readiness.Select(request => (string?)request.Observed!["head"]).Distinct()).Count().IsEqualTo(10);
        var repairs = delivery.Repairs(delivery.Forge("rev-parse", "refs/heads/" + delivery.RunBranch()));
        await Assert.That(string.Join(",", repairs.Select(repair => (string?)repair["outcome"]))).IsEqualTo(string.Join(",", Enumerable.Repeat("ci_failed", 9)));
        await delivery.RecordAsync("change_attempts_exhausted after ten ci_failed deliveries, no completion");
    }

    [Test]
    public async Task NewAndEditedFeedbackTriggerRepairButUnchangedFeedbackDoesNotRetrigger()
    {
        var feedback = new JsonObject
        {
            // Discussion comments over two pages, a review summary without a body and an inline comment.
            ["issueComments"] = new JsonArray(
                new JsonArray(Comment(1, "2026-09-01T00:00:00Z", "Please describe the controlled forge.")),
                new JsonArray(Comment(2, "2026-09-01T00:01:00Z", "Second-page discussion."))),
            ["reviews"] = new JsonArray(new JsonArray(new JsonObject
            {
                ["id"] = 10, ["state"] = "CHANGES_REQUESTED", ["submitted_at"] = "2026-09-01T00:02:00Z", ["commit_id"] = null,
                ["user"] = new JsonObject { ["login"] = "reviewer" }, ["body"] = "",
            })),
            ["reviewComments"] = new JsonArray(new JsonArray(new JsonObject
            {
                ["id"] = 20, ["updated_at"] = "2026-09-01T00:03:00Z", ["user"] = new JsonObject { ["login"] = "reviewer" },
                ["body"] = "Inline: tighten this line.", ["path"] = "README.md", ["line"] = 1, ["original_line"] = 1,
                ["commit_id"] = null, ["in_reply_to_id"] = null,
            })),
        };
        var protection = new JsonObject { ["requiredStatusCheckContexts"] = new JsonArray("ci") };
        await using var delivery = await ScriptedDelivery.StartAsync(new()
        {
            ["protection"] = protection.DeepClone(),
            ["feedback"] = feedback.DeepClone(),
            ["checks"] = new JsonArray(new JsonArray(Check("ci", true, null))),
        });
        // Once native reads the repaired head's readiness it has passed the unchanged feedback; edit one comment.
        await delivery.TraceUntilAsync(trace => trace.Any(request => request.IsReadiness));
        feedback["issueComments"]![0]![0] = Comment(1, "2026-09-01T01:00:00Z", "Please describe the controlled forge (edited).");
        delivery.Target.Script(new()
        {
            ["protection"] = protection.DeepClone(),
            ["feedback"] = feedback.DeepClone(),
            ["checks"] = new JsonArray(new JsonArray(Check("ci", true, "SUCCESS"))),
        });
        var completion = await delivery.ReadyAsync();

        var repairs = delivery.Repairs(completion.AcceptedRevision);
        await Assert.That(string.Join(",", repairs.Select(repair => (string?)repair["outcome"]))).IsEqualTo("repair_required,repair_required");
        var (first, second) = ((string)repairs[0]["deliveryFeedback"]!, (string)repairs[1]["deliveryFeedback"]!);
        foreach (var item in new[] { "issue_comment:1", "issue_comment:2", "review:10", "review_comment:20" })
            await Assert.That(first).Contains($"\"feedbackId\": \"{item}\"");
        await Assert.That(first).Contains("Review requested changes without a written summary.").And.Contains("path=README.md line=1");
        // Only the edited item returns; the unchanged ones never retrigger in this live run.
        await Assert.That(second).Contains("\"feedbackId\": \"issue_comment:1\"").And.Contains("(edited)");
        foreach (var item in new[] { "issue_comment:2", "review:10", "review_comment:20" })
            await Assert.That(second).DoesNotContain(item);
        // Every delivery read the feedback; the second delivery's unchanged read went on to assess readiness.
        var trace = delivery.Target.Trace().ToList();
        var reads = trace.Select((request, index) => (request, index)).Where(read => read.request.Argv[1].EndsWith("/issues/1/comments?per_page=100", StringComparison.Ordinal)).ToList();
        await Assert.That(reads.Count).IsGreaterThanOrEqualTo(4);
        await Assert.That(trace.FindIndex(request => request.IsReadiness)).IsGreaterThan(reads[1].index);
        await delivery.RecordAsync("ready after repairs for new and edited feedback only");
    }

    [Test]
    public async Task ForeignPullRequestIdentityIsRefused()
    {
        // The forge answers the created PR with its head in another repository.
        await using var delivery = await ScriptedDelivery.StartAsync(new() { ["foreignHead"] = "mallory/widget" });
        await Assert.That(await delivery.FailsAsync()).IsEqualTo("Zeroshot run failed: native_failed");
        // A refusal, not a repair: the one delivery ended the run.
        var ledger = await delivery.Target.LedgerAsync(delivery.RunId);
        await Assert.That(ledger.Failure).IsEqualTo("delivery_failed");
        await Assert.That((ledger.Executions["deliver"], ledger.Executions.ContainsKey("delivery_repair"))).IsEqualTo((1, false));
        var trace = delivery.Target.Trace();
        await Assert.That(trace.Count(request => request.Argv.Contains("POST"))).IsEqualTo(1);
        // Native neither assessed readiness nor read feedback for a PR it could not bind exactly.
        await Assert.That(trace.Any(request => request.IsReadiness || request.Argv[1].Contains("/comments", StringComparison.Ordinal))).IsFalse();
        await delivery.RecordAsync("refused foreign PR identity: delivery_failed, no completion");
    }

    public sealed record PendingCase(string Name, JsonObject Scenario)
    {
        public override string ToString() => Name;
    }

    internal static JsonObject Check(string name, bool required, string? conclusion, int? job = null) => new()
    {
        ["name"] = name, ["required"] = required, ["conclusion"] = conclusion, ["job"] = job,
    };

    private static JsonObject Change(string path, string content) => new() { ["path"] = path, ["content"] = content };

    private static JsonObject Comment(int id, string updated, string body) => new()
    {
        ["id"] = id, ["updated_at"] = updated, ["user"] = new JsonObject { ["login"] = "reviewer" }, ["body"] = body,
    };
}

/// <summary>
/// One PR Attempt prepared and dispatched through the application to a fresh stock target whose controlled
/// forge follows a scenario, with the checks every case makes of the result.
/// </summary>
internal sealed class ScriptedDelivery : IAsyncDisposable
{
    /// <summary>Real-time guard for one run; exhaustion, the longest, takes about a minute.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(10);
    private readonly AttemptFixture git;
    /// <summary>The native serving this target, as read inside it.</summary>
    private readonly (string Version, string Sha256) native;
    private string result = "did not reach its expected end";
    internal StockDirectTarget Target { get; }
    internal BroodlingStore Store { get; }
    internal string AttemptId { get; }
    internal string RunId => Store.FindSubmission(AttemptId)!.RunId!;

    private ScriptedDelivery(AttemptFixture git, StockDirectTarget target, (string, string) native, BroodlingStore store, string attemptId) =>
        (this.git, Target, this.native, Store, AttemptId) = (git, target, native, store, attemptId);

    internal static async Task<ScriptedDelivery> StartAsync(JsonObject scenario)
    {
        var git = new AttemptFixture();
        StockDirectTarget? target = null;
        try
        {
            target = await StockDirectTarget.StartAsync(git.State.Root);
            target.Script(scenario);
            var (store, revision) = Admit(git, target);
            await target.PushAsync(git.Repository, "main");
            var status = await new Invocation(store, new InvocationTarget.Direct(target.Origin)).ResumeAsync(revision, git.Repository, git.Head, Credentials);
            await Assert.That(status.Submissions.Single().State).IsEqualTo("correlated");
            return new(git, target, await target.NativeAsync(), store, status.Attempts.Single().AttemptId);
        }
        catch
        {
            if (target is not null) await target.DisposeAsync();
            git.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Wait accepts native's <c>v2</c>/<c>pr</c>/<c>ready</c> receipt for the controlled PR and pins exactly the
    /// head native last assessed on the forge.
    /// </summary>
    internal async Task<AttemptCompletion> ReadyAsync()
    {
        using var budget = new CancellationTokenSource(Patience);
        var completion = await Store.WaitAsync(AttemptId, null, budget.Token);
        var receipt = JsonNode.Parse(completion.ReceiptJson)!;
        await Assert.That(string.Join(",", receipt.AsObject().Where(field => field.Key != "headRevision").Select(field => $"{field.Key}={field.Value}").Order()))
            .IsEqualTo("mode=pr,outcome=ready,pullRequestId=1,repository=acme/widget,targetBranch=main,version=v2");
        var accepted = completion.AcceptedRevision;
        await Assert.That((string?)receipt["headRevision"]).IsEqualTo(accepted);
        await Assert.That(git.Git("rev-parse", "refs/broodling/accepted/" + accepted).Trim()).IsEqualTo(accepted);
        await Assert.That(Forge("rev-parse", "refs/heads/" + RunBranch())).IsEqualTo(accepted);
        await Assert.That((string?)Readiness()[^1].Observed!["head"]).IsEqualTo(accepted);
        return completion;
    }

    /// <summary>The run fails through the application: the Attempt is abandoned with no completion. Returns the reason.</summary>
    internal async Task<string> FailsAsync()
    {
        using var budget = new CancellationTokenSource(Patience);
        await Assert.That(async () => await Store.WaitAsync(AttemptId, null, budget.Token)).Throws<SubmissionNotReady>();
        await Assert.That(Store.FindCompletion(AttemptId)).IsNull();
        return Store.GetAttempt(AttemptId).Abandonment!.Reason;
    }

    internal async Task<IReadOnlyList<ForgeRequest>> TraceUntilAsync(Func<IReadOnlyList<ForgeRequest>, bool> reached)
    {
        for (var poll = 0; poll < 1200; poll++)
        {
            var trace = Target.Trace();
            if (reached(trace)) return trace;
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        throw new TimeoutException("Native never made the expected forge requests:\n" + string.Join('\n', Target.Trace()));
    }

    internal IReadOnlyList<ForgeRequest> Readiness() => Target.Trace().Where(request => request.IsReadiness).ToList();

    /// <summary>The merge state of each readiness answer, in order.</summary>
    internal string States() => string.Join(",", Readiness().Select(request => (string?)request.Observed!["mergeStateStatus"]));

    /// <summary>What each delivery repair delivered in <paramref name="head"/> received, oldest first: the controlled provider appends it.</summary>
    internal List<JsonObject> Repairs(string head) =>
        Forge("ls-tree", "--name-only", head).Split('\n').Contains("delivery-repairs.jsonl")
            ? Forge("show", head + ":delivery-repairs.jsonl").Split('\n').Select(line => JsonNode.Parse(line)!.AsObject()).ToList()
            : [];

    internal string RunBranch() => Forge("for-each-ref", "--format=%(refname:lstrip=2)", "refs/heads/zeroshot/");

    internal string Forge(params string[] arguments) => AttemptFixture.RunGit(Target.Forge, arguments).Trim();

    /// <summary>
    /// Name the case's result for its record, after requiring the binding's native and approved asset and that the
    /// PR path requested no merge, made no request the controlled forge lacks and landed nothing on the target branch.
    /// </summary>
    internal async Task RecordAsync(string result)
    {
        this.result = result;
        await Assert.That(native).IsEqualTo((DirectTargetBinding.NativeVersion, DirectTargetBinding.NativeExecutableSha256));
        await Assert.That(Store.FindSubmission(AttemptId)!.AssetSha256).IsEqualTo(DirectTargetBinding.AssetSha256);
        var trace = Target.Trace();
        await Assert.That(trace.Where(request => request.IsMerge)).IsEmpty();
        await Assert.That(trace.Where(request => request.Unexpected)).IsEmpty();
        // Nor did anything reach the target branch but B1 and the forge's own scripted change.
        await Assert.That(Forge("log", "--format=%H %an", "main").Split('\n')
            .All(commit => commit.StartsWith(git.Head, StringComparison.Ordinal) || commit.EndsWith(" Forge", StringComparison.Ordinal))).IsTrue();
    }

    /// <summary>Each case's record, whether or not it passed: identities, result and native's forge requests.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            var (failure, executions) = await Target.LedgerAsync(RunId);
            Console.WriteLine($"native {native.Version} (linux-x64 sha256 {native.Sha256}); approved asset sha256 "
                + $"{Store.FindSubmission(AttemptId)!.AssetSha256}; run {RunId}: {result}; native failure {failure ?? "none"}, "
                + "node executions " + string.Join(", ", executions.Select(node => $"{node.Key}={node.Value}")));
            foreach (var request in Target.Trace()) Console.WriteLine("  gh " + request);
        }
        // The record is diagnostic: failing to make it must neither hide the case's own result nor skip cleanup.
        catch (Exception error) { Console.WriteLine("record unavailable: " + error.Message); }
        finally
        {
            Store.Dispose();
            await Target.DisposeAsync();
            git.Dispose();
        }
    }
}

/// <summary>One <c>gh</c> invocation as the controlled forge traced it: arguments with native's graphql documents named, and what it answered from.</summary>
internal sealed record ForgeRequest(string[] Argv, JsonObject? Observed, bool Unexpected)
{
    internal bool IsReadiness => Argv.Contains("query=<readiness policy query>");
    internal bool IsHeadUpdate => Argv.Contains("query=<updatePullRequestBranch mutation>");

    /// <summary>Any GitHub merge request: <c>gh pr merge</c>, the REST merge route or a merge/auto-merge/queue mutation.</summary>
    internal bool IsMerge => Argv is ["pr", "merge", ..] || Argv.Any(argument =>
        System.Text.RegularExpressions.Regex.IsMatch(argument, @"/pulls/\d+/merge\b|mergePullRequest|enablePullRequestAutoMerge|enqueuePullRequest"));

    public override string ToString() =>
        string.Join(' ', Argv.Select(argument => argument.Length > 100 ? argument[..100] + "..." : argument).Select(argument => argument.Replace('\n', ' ')))
        + (Observed is null ? "" : " -> " + Observed.ToJsonString()) + (Unexpected ? " [unexpected]" : "");
}
