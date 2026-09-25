using Broodling.Host;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class ContractIngressTests
{
    internal static WorkReference Reference => WorkReference.Parse("acme/widget", 12);
    internal static SourceSubmission Primary(byte[]? bytes = null) => new("primary_issue", Reference.IssueLocator,
        bytes ?? "The complete reviewed request.\n"u8.ToArray(), entitlement: new("caller", "Reviewed supplied issue bytes"));
    internal static SourceSubmission Supplement => new("referenced_document", "caller://decision", [0, 255, 13, 10],
        mediaType: "application/octet-stream", entitlement: new("caller", "Explicitly reviewed decision"));
    internal static Contract Propose(ContractProposalInput input) => new(input.WorkUnit.WorkUnitId,
        input.SourceAttribution, [new("acceptance", "Preserve the complete request.")],
        requiredEffects: input.RequiredEffects, constructedBy: input.ConstructedBy, requestBundle: input.BundleBinding);

    [Test]
    public async Task SuppliedAdmissionReopenAndOperatorInspectionRetainExactOldAndRejectedLineage()
    {
        using var fixture = new StoreFixture();
        AdmissionStatus first;
        AdmissionStatus rejected;
        using (var store = fixture.Initialize())
        {
            first = store.AdmitSources(Reference, [Primary(), Supplement], Propose, [], "caller");
            await Assert.That(first.Decision!.Admitted).IsTrue();
            // Both source order and attribution order are immaterial; effects and criteria retain their order.
            var replay = store.AdmitSources(Reference, [Supplement, Primary()], input => new(input.WorkUnit.WorkUnitId,
                input.SourceAttribution.Reverse().ToArray(), [new("acceptance", "Preserve the complete request.")],
                constructedBy: input.ConstructedBy), [], "caller");
            await Assert.That(replay.Revision.ContractRevisionId).IsEqualTo(first.Revision.ContractRevisionId);
            await Assert.That(replay.Revision.RecordedAt).IsEqualTo(first.Revision.RecordedAt);
            await Assert.That(replay.Decision!.DecidedAt).IsEqualTo(first.Decision.DecidedAt);
            rejected = store.AdmitSources(Reference, [Primary("Changed request: publish a release."u8.ToArray())], Propose,
                [new("release", "Publish the release unchanged.", "publication")], "caller");
            await Assert.That(rejected.Decision!.Admitted).IsFalse();
            await Assert.That(rejected.Revision.SupersedesRevisionId).IsEqualTo(first.Revision.ContractRevisionId);
        }
        using var reopened = fixture.Open();
        var history = reopened.History(Reference);
        await Assert.That(history.Select(status => status.Revision.RevisionNumber).SequenceEqual(new long[] { 1, 2 })).IsTrue();
        await Assert.That(history[0].Sources.Single(source => source.Kind == "primary_issue").Content.SequenceEqual(Primary().Content)).IsTrue();
        await Assert.That(history[0].Sources.Single(source => source.Kind == "referenced_document").Content.SequenceEqual(Supplement.Content)).IsTrue();
        await Assert.That(history[1].Decision!.Findings.Single().PreservedObligation).IsEqualTo("Publish the release unchanged.");
        await Assert.That(history[0].Revision.CanonicalBytes.SequenceEqual(first.Revision.CanonicalBytes)).IsTrue();
        var same = reopened.AdmitSources(Reference, [Primary(), Supplement], Propose, [], "caller");
        await Assert.That(same.Revision.ContractRevisionId).IsEqualTo(first.Revision.ContractRevisionId);
        await Assert.That(same.Revision.SupersedesRevisionId).IsNull();
        await Assert.That(reopened.History(Reference).Count).IsEqualTo(2);

        var output = new StringWriter();
        var error = new StringWriter();
        await Assert.That(StoreCommands.Run(["status", fixture.Path, first.Revision.ContractRevisionId], fixture.Application, output, error)).IsEqualTo(0);
        using var json = JsonDocument.Parse(output.ToString());
        await Assert.That(json.RootElement.GetProperty("revision").GetProperty("canonicalBytes").GetBytesFromBase64()
            .SequenceEqual(first.Revision.CanonicalBytes)).IsTrue();
        var binary = json.RootElement.GetProperty("sources").EnumerateArray().Single(source => source.GetProperty("kind").GetString() == "referenced_document");
        await Assert.That(binary.GetProperty("content").GetBytesFromBase64().SequenceEqual(Supplement.Content)).IsTrue();
        output.GetStringBuilder().Clear();
        await Assert.That(StoreCommands.Run(["history", fixture.Path, "ACME/widget.git", "#12"], fixture.Application, output, error)).IsEqualTo(0);
        using var historyJson = JsonDocument.Parse(output.ToString());
        await Assert.That(historyJson.RootElement.GetArrayLength()).IsEqualTo(2);
        await Assert.That(StoreCommands.Run(["status", fixture.Path, "missing"], fixture.Application, output, error)).IsEqualTo(1);
        await Assert.That(error.ToString().Contains("unknown_record")).IsTrue();
        await Assert.That(error.ToString().Contains(fixture.Path)).IsFalse();
    }

    [Test]
    public async Task SuppliedBytesRequireExactlyOnePrimaryAndExplicitCallerOriginAndGrant()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var invalid = new SourceSubmission[][]
        {
            [], [Supplement], [Primary(), Primary()],
            [new("primary_issue", Reference.IssueLocator, [])],
            [new("primary_issue", Reference.IssueLocator, [], origin: "broodling_policy", entitlement: new("caller", "claimed"))],
            [new("primary_issue", Reference.IssueLocator, [], entitlement: new("broodling_policy", "claimed"))],
            [Primary(), new("referenced_document", "doc", [], entitlement: new("caller", " "))],
            [new("primary_issue", Reference.IssueLocator, [], origin: "model_extraction", entitlement: new("caller", "claimed"))]
        };
        foreach (var sources in invalid)
            await Assert.That(() => store.AdmitSources(Reference, sources, Propose, [])).Throws<SourceNotEntitled>();
        await Assert.That(store.FindWorkUnit(Reference)).IsNull();
        await Assert.That(store.History(Reference).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ProposalCannotChangeWorkProducerOrAnyEffectFieldOrOrder()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var effects = new RequiredEffect[] { new("pr", "Open the reviewed PR.", "pull_request", "main"), new("push", "Push reviewed branch.", "push") };
        var changed = new RequiredEffect[][]
        {
            [], [effects[0]], [effects[0], effects[1], new("extra", "Deploy.", "deployment")],
            [effects[1], effects[0]],
            [effects[0] with { EffectId = "another" }, effects[1]],
            [effects[0] with { Statement = "Weakened statement." }, effects[1]],
            [effects[0] with { Kind = "merge" }, effects[1]],
            [effects[0] with { TargetBranch = "release" }, effects[1]]
        };
        foreach (var replacement in changed)
            await Assert.That(() => store.AdmitSources(Reference, [Primary()], input => new(input.WorkUnit.WorkUnitId,
                input.SourceAttribution, [new("c", "Outcome.")], requiredEffects: replacement,
                constructedBy: input.ConstructedBy), effects)).Throws<InvalidContractProposal>();
        foreach (var field in new[] { "work", "producer" })
            await Assert.That(() => store.AdmitSources(Reference, [Primary()], input => new(
                field == "work" ? "another-work" : input.WorkUnit.WorkUnitId, input.SourceAttribution,
                [new("c", "Outcome.")], constructedBy: field == "producer" ? "caller" : input.ConstructedBy), []))
                .Throws<InvalidContractProposal>();
        await Assert.That(store.History(Reference).Count).IsEqualTo(0);
        await Assert.That(store.ListEntitledSources(Reference.WorkUnitId).Count).IsEqualTo(1);
    }

    [Test]
    public async Task ProposalMustPinCompleteExactInputsWithoutOldForeignAddedOrDuplicateSources()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var work = store.ResolveWorkUnit(Reference);
        var older = store.EntitleSource(work.WorkUnitId, Primary("older"u8.ToArray()));
        var foreignWork = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 13));
        var foreign = store.EntitleSource(foreignWork.WorkUnitId, Supplement);
        foreach (var variant in new[] { "omit", "duplicate", "old", "foreign", "extra", "digest" })
            await Assert.That(() => store.AdmitSources(Reference, [Primary(), Supplement], input =>
            {
                var pins = input.SourceAttribution.ToArray();
                var replacement = variant switch
                {
                    "omit" => [pins[0]],
                    "duplicate" => new[] { pins[0], pins[1], pins[0] },
                    "old" => [new(older.SourceId, older.ContentSha256), pins[1]],
                    "foreign" => [pins[0], new(foreign.SourceId, foreign.ContentSha256)],
                    "extra" => [pins[0], pins[1], new(older.SourceId, older.ContentSha256)],
                    _ => new[] { pins[0] with { ContentSha256 = new string('0', 64) }, pins[1] }
                };
                return new(input.WorkUnit.WorkUnitId, replacement, [new("c", "Outcome.")], constructedBy: input.ConstructedBy);
            }, [])).Throws<SourceAttributionError>();
        await Assert.That(store.History(Reference).Count).IsEqualTo(0);
        foreach (var pin in new[] { new SourceAttribution(foreign.SourceId, foreign.ContentSha256), new("missing", "digest"), new(older.SourceId, "wrong") })
            await Assert.That(() => store.RecordContractRevision(new(work.WorkUnitId, [pin], [new("c", "Outcome.")]))).Throws<SourceAttributionError>();
    }

    [Test]
    public async Task MalformedProposalsAndThrowingProposerLeaveCapturedSourcesButNoRevision()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var badCriteria = new Criterion[][]
        {
            [new(" ", "Outcome.")], [new("c", " ")], [new("c", "First."), new("c", "Duplicate.")],
            [new("c", null!)], [new("c", "Outcome.", validationAction: null!)], [null!]
        };
        foreach (var criteria in badCriteria)
            await Assert.That(() => store.AdmitSources(Reference, [Primary()], input => new(input.WorkUnit.WorkUnitId,
                input.SourceAttribution, criteria, constructedBy: input.ConstructedBy), [])).Throws<InvalidContractProposal>();
        await Assert.That(() => store.AdmitSources(Reference, [Primary()], _ => null!, [])).Throws<InvalidContractProposal>();
        await Assert.That(() => store.AdmitSources(Reference, [Primary()], _ => throw new InvalidOperationException("proposer failed"), []))
            .Throws<InvalidOperationException>();
        await Assert.That(store.History(Reference).Count).IsEqualTo(0);
        await Assert.That(store.ListEntitledSources(Reference.WorkUnitId).Count).IsEqualTo(1);
    }

    [Test]
    public async Task CallbackCannotMutateFrozenCallerAuthorityThroughOriginalOrExposedCollections()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var effects = new RequiredEffect[] { new("pr", "Open the PR.", "pull_request", "main") };
        var status = store.AdmitSources(Reference, [Primary()], input =>
        {
            effects[0] = effects[0] with { TargetBranch = "unauthorized" };
            input.Sources[0].Content[0] = 255;
            if (input.RequiredEffects is IList<RequiredEffect> list && !list.IsReadOnly)
                list[0] = effects[0];
            return Propose(input);
        }, effects);
        await Assert.That(status.Revision.Contract.RequiredEffects.Single().TargetBranch).IsEqualTo("main");
        await Assert.That(status.Sources.Single().Content.SequenceEqual(Primary().Content)).IsTrue();
        await Assert.That(status.Decision!.Admitted).IsTrue();
        await Assert.That(((IList<RequiredEffect>)status.Revision.Contract.RequiredEffects).IsReadOnly).IsTrue();
        var exposed = status.Revision.CanonicalBytes;
        exposed[0] = 0;
        await Assert.That(store.Status(status.Revision.ContractRevisionId).Revision.CanonicalBytes[0]).IsNotEqualTo((byte)0);
    }
}
