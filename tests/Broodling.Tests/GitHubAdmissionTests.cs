using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class GitHubAdmissionTests
{
    private static WorkReference Reference => WorkReference.Parse("Acme/Widget", 75);
    private static RequiredEffect Effect => new("deliver", "Open the selected PR.", "pull_request", "main");

    private static byte[] Issue() => """
        {"number":75,"node_id":"I_example75","html_url":"https://github.com/ACME/WIDGET/issues/75",
         "repository_url":"https://api.github.com/repos/Acme/Widget","title":"Keep the request",
         "body":"See https://example.test/decision and #76. Do not fetch either.",
         "comments_url":"https://api.github.com/repos/acme/widget/issues/75/comments","comments":3}

        """u8.ToArray();

    [Test]
    public async Task AcquisitionReadsOnlyNamedIssuePreservesExactResponseAndPinsStableIdentity()
    {
        var bytes = Issue();
        using var gh = new GhFixture(bytes);
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var reviewed = new ReviewedIssueProposal(bytes);
        var status = await store.AdmitGitHubAsync(Reference, reviewed.Propose, [Effect], constructedBy: "caller", source: gh.Source);
        await Assert.That(status.Decision!.Admitted).IsTrue();
        await Assert.That(status.WorkUnit.IssueIdentity).IsEqualTo("I_example75");
        await Assert.That(status.WorkUnit.RepositoryIdentity).IsNull();
        await Assert.That(status.Sources.Count).IsEqualTo(1);
        var captured = status.Sources.Single();
        await Assert.That(captured.Content.SequenceEqual(bytes)).IsTrue();
        await Assert.That(captured.Origin).IsEqualTo("broodling_policy");
        await Assert.That(captured.EntitledBy).IsEqualTo("broodling_policy");
        await Assert.That(captured.MediaType).IsEqualTo("application/json");
        await Assert.That(DateTimeOffset.TryParse(captured.RetrievedAt, out _)).IsTrue();
        await Assert.That(status.Revision.Contract.RequiredEffects.Single()).IsEqualTo(Effect);
        await Assert.That(status.Revision.ConstructedBy).IsEqualTo("caller");
        using var issue = JsonDocument.Parse(bytes);
        await Assert.That(status.Revision.Contract.Criteria.Single().Statement).IsEqualTo(
            issue.RootElement.GetProperty("title").GetString() + "\n\n" + issue.RootElement.GetProperty("body").GetString());
        await Assert.That(gh.Calls.SequenceEqual(new[]
        {
            "CALL", "api", "--hostname", "github.com", "--method", "GET", "--header",
            "Accept: application/vnd.github+json", "--header", "X-GitHub-Api-Version: 2022-11-28", "/repos/acme/widget/issues/75"
        })).IsTrue();
        await Assert.That(Reference.IssueIdentity).IsNull();
    }

    [Test]
    public async Task ResponseIdentityAndShapeFailuresLeaveNoEntitledOrAdmittedMaterial()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var mutations = new (string Field, JsonNode? Value)[]
        {
            ("number", 76), ("number", "75"), ("number", true),
            ("html_url", "https://github.com/acme/other/issues/75"),
            ("html_url", Reference.IssueLocator + "?comment=1"),
            ("html_url", Reference.IssueLocator + "#comment"),
            ("repository_url", "https://api.github.com/repos/other/widget"),
            ("pull_request", null), ("title", " "), ("title", 1),
            ("body", new JsonObject()), ("node_id", null), ("node_id", " ")
        };
        var invalid = new List<byte[]> { "{"u8.ToArray(), "[]"u8.ToArray(), "null"u8.ToArray(), new byte[] { 255 } };
        foreach (var (field, value) in mutations)
        {
            var issue = JsonNode.Parse(Issue())!;
            issue[field] = value;
            invalid.Add(Encoding.UTF8.GetBytes(issue.ToJsonString()));
        }
        var missing = JsonNode.Parse(Issue())!.AsObject();
        missing.Remove("body");
        invalid.Add(Encoding.UTF8.GetBytes(missing.ToJsonString()));
        foreach (var bytes in invalid)
        {
            using var gh = new GhFixture(bytes);
            await Assert.That(async () => await store.AdmitGitHubAsync(Reference, ContractIngressTests.Propose, [], source: gh.Source))
                .Throws<GitHubSourceError>();
        }
        await Assert.That(store.FindWorkUnit(Reference)).IsNull();
        await Assert.That(store.History(Reference).Count).IsEqualTo(0);
    }

    [Test]
    public async Task NullBodyAndKnownPinsConvergeWhileConflictingSuppliedOrRetainedPinsRefuse()
    {
        var issue = JsonNode.Parse(Issue())!;
        issue["body"] = null;
        var bytes = Encoding.UTF8.GetBytes(issue.ToJsonString());
        using var gh = new GhFixture(bytes);
        using var fixture = new StoreFixture();
        using (var store = fixture.Initialize())
        {
            var pinned = WorkReference.Parse("acme/widget", 75, repositoryIdentity: "repo-id", issueIdentity: "I_example75");
            var result = await store.AdmitGitHubAsync(pinned, new ReviewedIssueProposal(bytes).Propose, [], source: gh.Source);
            await Assert.That(result.Revision.Contract.Criteria.Single().Statement).IsEqualTo("Keep the request\n\n");
            await Assert.That(result.WorkUnit.RepositoryIdentity).IsEqualTo("repo-id");
        }
        using var reopened = fixture.Open();
        await Assert.That(async () => await reopened.AdmitGitHubAsync(
            WorkReference.Parse("acme/widget", 75, issueIdentity: "other"), ContractIngressTests.Propose, [], source: gh.Source))
            .Throws<GitHubSourceError>();
        await Assert.That(async () => await reopened.AdmitGitHubAsync(
            WorkReference.Parse("acme/widget", 75, repositoryIdentity: "other"), ContractIngressTests.Propose, [], source: gh.Source))
            .Throws<WorkUnitIdentityConflict>();
        issue["node_id"] = "I_recreated75";
        gh.SetResponse(Encoding.UTF8.GetBytes(issue.ToJsonString()));
        await Assert.That(async () => await reopened.AdmitGitHubAsync(Reference, ContractIngressTests.Propose, [], source: gh.Source))
            .Throws<WorkUnitIdentityConflict>();
        await Assert.That(reopened.History(Reference).Count).IsEqualTo(1);
        await Assert.That(reopened.ListEntitledSources(Reference.WorkUnitId).Count).IsEqualTo(1);
    }

    [Test]
    public async Task SupplementarySourcesRequireCallerGrantsBeforeAcquisitionAndReuseAdmissionChecks()
    {
        using var gh = new GhFixture(Issue());
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        foreach (var source in new[]
        {
            new SourceSubmission("referenced_document", "caller://decision", []),
            new("referenced_document", "caller://decision", [], origin: "broodling_policy", entitlement: new("caller", "claimed")),
            new("referenced_document", "caller://decision", [], entitlement: new("broodling_policy", "claimed"))
        })
            await Assert.That(async () => await store.AdmitGitHubAsync(Reference, ContractIngressTests.Propose, [], [source], source: gh.Source))
                .Throws<SourceNotEntitled>();
        await Assert.That(gh.Calls.Count).IsEqualTo(0);
        var supplement = ContractIngressTests.Supplement;
        var first = await store.AdmitGitHubAsync(Reference, ContractIngressTests.Propose, [Effect], [supplement], source: gh.Source);
        var replay = await store.AdmitGitHubAsync(Reference, input => new(input.WorkUnit.WorkUnitId,
            input.SourceAttribution.Reverse().ToArray(), [new("acceptance", "Preserve the complete request.")],
            requiredEffects: input.RequiredEffects, constructedBy: input.ConstructedBy), [Effect], [supplement], source: gh.Source);
        await Assert.That(replay.Revision.ContractRevisionId).IsEqualTo(first.Revision.ContractRevisionId);
        await Assert.That(first.Sources.Single(source => source.Kind == "referenced_document").Content.SequenceEqual(supplement.Content)).IsTrue();
        await Assert.That(async () => await store.AdmitGitHubAsync(Reference, input => new(input.WorkUnit.WorkUnitId,
            input.SourceAttribution, [new("c", "Changed authority.")], constructedBy: input.ConstructedBy), [Effect], source: gh.Source))
            .Throws<InvalidContractProposal>();
        await Assert.That(store.History(Reference).Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExactReviewedBytesRefuseEvenEquivalentJsonOrMetadataChangesWithoutAmendingEarlierAuthority()
    {
        var original = Issue();
        using var gh = new GhFixture(original);
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var reviewed = new ReviewedIssueProposal(original);
        var first = await store.AdmitGitHubAsync(Reference, reviewed.Propose, [Effect], source: gh.Source);
        original[0] = 0; // The retained caller pin is a copy, not a mutable authorization buffer.
        foreach (var variant in new[] { "format", "body", "metadata" })
        {
            var issue = JsonNode.Parse(Issue())!;
            if (variant == "body") issue["body"] = "Deploy it to production too.";
            if (variant == "metadata") issue["comments"] = 4;
            var changed = Encoding.UTF8.GetBytes(issue.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            gh.SetResponse(changed);
            await Assert.That(async () => await store.AdmitGitHubAsync(Reference, reviewed.Propose, [Effect], source: gh.Source))
                .Throws<InvalidContractProposal>();
            await Assert.That(store.History(Reference).Count).IsEqualTo(1);
        }
        await Assert.That(store.Status(first.Revision.ContractRevisionId).Sources.Single().Content.SequenceEqual(Issue())).IsTrue();
        await Assert.That(store.ListEntitledSources(first.WorkUnit.WorkUnitId).Count).IsEqualTo(4);
        gh.SetResponse(Issue());
        var again = await store.AdmitGitHubAsync(Reference, reviewed.Propose, [Effect], source: gh.Source);
        await Assert.That(again.Revision.ContractRevisionId).IsEqualTo(first.Revision.ContractRevisionId);
        await Assert.That(async () => await store.AdmitGitHubAsync(Reference, reviewed.Propose, [Effect],
            [ContractIngressTests.Supplement], source: gh.Source)).Throws<InvalidContractProposal>();
    }

    [Test]
    public async Task RetainedRealIssueFixturesAdmitOrPreserveTheirUnsatisfiedPrerequisiteWithoutExecution()
    {
        using var fixture = new StoreFixture();
        AdmissionStatus accepted;
        AdmissionStatus rejected;
        var bytes82 = File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "issue-82.json"));
        var bytes75 = File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "issue-75.json"));
        Contract Propose(ContractProposalInput input)
        {
            using var issue = JsonDocument.Parse(input.Sources.Single().Content);
            var body = issue.RootElement.GetProperty("body").GetString()!;
            var criteria = new[] { new Criterion("completion", body.Split("## Completion\n\n", 2)[1]) };
            var prerequisite = input.WorkUnit.IssueNumber == 75
                ? new[] { new Prerequisite("readiness", body.Split("**Start condition:** ", 2)[1].Split('\n')[0], false) } : [];
            return new(input.WorkUnit.WorkUnitId, input.SourceAttribution, criteria, prerequisites: prerequisite,
                requiredEffects: input.RequiredEffects, constructedBy: input.ConstructedBy);
        }
        using (var store = fixture.Initialize())
        {
            using var gh82 = new GhFixture(bytes82);
            using var gh75 = new GhFixture(bytes75);
            accepted = await store.AdmitGitHubAsync(WorkReference.Parse("faviann/broodling", 82), Propose, [Effect], source: gh82.Source);
            rejected = await store.AdmitGitHubAsync(WorkReference.Parse("faviann/broodling", 75), Propose, [Effect], source: gh75.Source);
            await Assert.That(accepted.Decision!.Admitted).IsTrue();
            await Assert.That(rejected.Decision!.Admitted).IsFalse();
            await Assert.That(gh75.Calls.Count(line => line == "CALL")).IsEqualTo(1);
        }
        using var reopened = fixture.Open();
        using var replayGh = new GhFixture(bytes82);
        var replay = await reopened.AdmitGitHubAsync(WorkReference.Parse("faviann/broodling", 82), Propose, [Effect], source: replayGh.Source);
        await Assert.That(replay.Revision.ContractRevisionId).IsEqualTo(accepted.Revision.ContractRevisionId);
        await Assert.That(replay.Decision!.DecidedAt).IsEqualTo(accepted.Decision!.DecidedAt);
        var retained = reopened.Status(rejected.Revision.ContractRevisionId);
        await Assert.That(retained.Sources.Single().Content.SequenceEqual(bytes75)).IsTrue();
        await Assert.That(retained.Decision!.Findings.Single().Code).IsEqualTo("unsatisfied_prerequisite");
        await Assert.That(retained.Decision.Findings.Single().PreservedObligation)
            .IsEqualTo(retained.Revision.Contract.Prerequisites.Single().Statement);
        var changed = JsonNode.Parse(bytes82)!;
        changed["comments"] = 99;
        replayGh.SetResponse(Encoding.UTF8.GetBytes(changed.ToJsonString()));
        var next = await reopened.AdmitGitHubAsync(WorkReference.Parse("faviann/broodling", 82), Propose, [Effect], source: replayGh.Source);
        await Assert.That(next.Revision.SupersedesRevisionId).IsEqualTo(accepted.Revision.ContractRevisionId);
        await Assert.That(reopened.Status(accepted.Revision.ContractRevisionId).Sources.Single().Content.SequenceEqual(bytes82)).IsTrue();
    }

    [Test]
    public async Task AwaitingAcquisitionDoesNotLetCallerMutateFrozenEffectAuthority()
    {
        using var gh = new GhFixture(Issue(), waitForGate: true);
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var effects = new[] { Effect };
        var pending = store.AdmitGitHubAsync(Reference, ContractIngressTests.Propose, effects, source: gh.Source);
        effects[0] = Effect with { TargetBranch = "unauthorized" };
        gh.Release();
        var status = await pending;
        await Assert.That(status.Revision.Contract.RequiredEffects.Single().TargetBranch).IsEqualTo("main");
    }

    [Test]
    public async Task AcquisitionFailuresHideDiagnosticsAndUnsupportedHostsDoNotLaunchGh()
    {
        using var gh = new GhFixture(Issue(), exitCode: 1);
        foreach (var source in new[] { gh.Source, new GitHubIssueSource(System.IO.Path.Combine(gh.Root, "missing-sensitive-sentinel")) })
        {
            try { await source.AcquireAsync(Reference); throw new Exception("Expected acquisition refusal."); }
            catch (GitHubSourceError error)
            {
                await Assert.That(error.ToString().Contains("sensitive-sentinel", StringComparison.Ordinal)).IsFalse();
                await Assert.That(error.InnerException).IsNull();
            }
        }
        var calls = gh.Calls.Count;
        await Assert.That(async () => await gh.Source.AcquireAsync(WorkReference.Parse("gitlab.com/acme/widget", 75)))
            .Throws<GitHubSourceError>();
        await Assert.That(gh.Calls.Count).IsEqualTo(calls);
    }

    [Test]
    public async Task CancelledAcquisitionCreatesNoAuthorityAndCanBeRetried()
    {
        using var gh = new GhFixture(Issue(), waitForGate: true);
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        using var cancellation = new CancellationTokenSource();
        var pending = store.AdmitGitHubAsync(Reference, ContractIngressTests.Propose, [], source: gh.Source,
            cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.That(async () => await pending).Throws<OperationCanceledException>();
        await Assert.That(store.FindWorkUnit(Reference)).IsNull();
        gh.Release();
        var status = await store.AdmitGitHubAsync(Reference, ContractIngressTests.Propose, [], source: gh.Source);
        await Assert.That(status.Decision!.Admitted).IsTrue();
    }

    private sealed class GhFixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("broodling-gh-").FullName;
        internal GitHubIssueSource Source { get; }
        internal IReadOnlyList<string> Calls => File.Exists(System.IO.Path.Combine(Root, "calls"))
            ? File.ReadAllLines(System.IO.Path.Combine(Root, "calls")) : [];

        internal GhFixture(byte[] content, bool waitForGate = false, int exitCode = 0)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The supported acquisition profile is Linux.");
            SetResponse(content);
            var executable = System.IO.Path.Combine(Root, "gh");
            File.WriteAllText(executable, "#!/bin/sh\nset -eu\n"
                + $"printf '%s\\n' CALL \"$@\" >> '{Root}/calls'\n"
                + (waitForGate ? $"while [ ! -e '{Root}/gate' ]; do sleep 0.01; done\n" : "")
                + $"cat '{Root}/response'\n"
                + $"printf '%s\\n' 'sensitive-sentinel' >&2\nexit {exitCode}\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Source = new(executable);
        }

        internal void SetResponse(byte[] content) => File.WriteAllBytes(System.IO.Path.Combine(Root, "response"), content);
        internal void Release() => File.WriteAllText(System.IO.Path.Combine(Root, "gate"), "ready");
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
