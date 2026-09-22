using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class SourceCustodyTests
{
    [Test]
    public async Task WellFormedUnicodeKeepsIdentityProvenanceAndArbitraryPayloadBytes()
    {
        using var fixture = new StoreFixture();
        const string text = "\ufffd\U0001f680";
        var reference = WorkReference.Parse("https://" + text + "@github.com/acme/widget", 12,
            repositoryIdentity: "R-" + text, issueIdentity: "I-" + text);
        var payload = new byte[] { 0, 255, 0xed, 0xa0, 0x80 };
        var submission = new SourceSubmission("referenced_document", "doc:" + text, payload,
            mediaType: "type/" + text, retrievedAt: "time-" + text, entitlement: new("caller", "grant-" + text));
        EntitledSource first;
        using (var store = fixture.Initialize())
        {
            var work = store.ResolveWorkUnit(reference);
            await Assert.That(work.WorkUnitId).IsEqualTo("wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a");
            first = store.EntitleSource(work.WorkUnitId, submission);
            // Existing v1 digest bytes for this well-formed Unicode input.
            await Assert.That(first.SourceId).IsEqualTo("src-211595d48fc39f5652a72956d32b31b5b625d61628603f57493f4af4fcc8ef3e");
        }
        using var reopened = fixture.Open();
        var restored = reopened.EntitleSource(reference.WorkUnitId, submission);
        await Assert.That(restored.SourceId).IsEqualTo(first.SourceId);
        await Assert.That(restored.RecordedAt).IsEqualTo(first.RecordedAt);
        await Assert.That(restored.Locator).IsEqualTo(submission.Locator);
        await Assert.That(restored.MediaType).IsEqualTo(submission.MediaType);
        await Assert.That(restored.RetrievedAt).IsEqualTo(submission.RetrievedAt);
        await Assert.That(restored.EntitlementBasis).IsEqualTo(submission.Entitlement!.Basis);
        await Assert.That(restored.Content.SequenceEqual(payload)).IsTrue();
        var retainedWork = reopened.GetWorkUnit(reference.WorkUnitId);
        await Assert.That(retainedWork.RepositoryIdentity).IsEqualTo(reference.RepositoryIdentity);
        await Assert.That(retainedWork.IssueIdentity).IsEqualTo(reference.IssueIdentity);
        await Assert.That(reopened.ListWorkSubmissions(reference.WorkUnitId).Single().SubmittedRepository)
            .IsEqualTo(reference.SubmittedRepository);
    }

    [Test]
    [Arguments("mediaType")]
    [Arguments("retrievedAt")]
    [Arguments("basis")]
    public async Task MalformedSourceMetadataIsRefusedBeforeRetention(string field)
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var work = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12));
        var submission = new SourceSubmission("referenced_document", "doc", [0, 255],
            mediaType: field == "mediaType" ? "text/\udc00" : "text/plain",
            retrievedAt: field == "retrievedAt" ? "\ud800" : "",
            entitlement: new("caller", field == "basis" ? "grant \ud800" : "grant"));
        await Assert.That(() => store.EntitleSource(work.WorkUnitId, submission)).Throws<SourceNotEntitled>();
        await Assert.That(store.ListEntitledSources(work.WorkUnitId).Count).IsEqualTo(0);
    }

    [Test]
    public async Task MalformedLocatorCannotAliasReplacementCharacterOrRewriteProvenance()
    {
        using var fixture = new StoreFixture();
        string workId;
        EntitledSource valid;
        using (var store = fixture.Initialize())
        {
            workId = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12)).WorkUnitId;
            SourceSubmission Submission(string locator) => new("referenced_document", locator,
                [0, 255], entitlement: new("caller", "exact grant"));
            Exception? refusal = null;
            EntitledSource? malformed = null;
            try { malformed = store.EntitleSource(workId, Submission("doc:\ud800")); }
            catch (SourceNotEntitled exception) { refusal = exception; }
            var retainedAfterMalformed = store.ListEntitledSources(workId).Count;
            valid = store.EntitleSource(workId, Submission("doc:\ufffd"));
            Console.WriteLine($"refused={refusal is not null}; retainedAfterMalformed={retainedAfterMalformed}; " +
                $"aliased={malformed?.SourceId == valid.SourceId}; " +
                $"returnedLocatorRewritten={malformed is not null && malformed.Locator != "doc:\ud800"}");
            await Assert.That(refusal).IsNotNull();
            await Assert.That(retainedAfterMalformed).IsEqualTo(0);
            await Assert.That(valid.Locator).IsEqualTo("doc:\ufffd");
        }
        using var reopened = fixture.Open();
        await Assert.That(reopened.ListEntitledSources(workId).Count).IsEqualTo(1);
        await Assert.That(reopened.GetEntitledSource(valid.SourceId).Locator).IsEqualTo("doc:\ufffd");
        await Assert.That(reopened.GetEntitledSource(valid.SourceId).EntitlementBasis).IsEqualTo("exact grant");
    }

    [Test]
    public async Task ExactBytesAndFirstProvenanceSurviveReplayChangeAndReopen()
    {
        using var fixture = new StoreFixture();
        var bytes = new byte[] { 0, 255, 10, 13, 123, 34, 125 };
        var originalBytes = (byte[])bytes.Clone();
        EntitledSource first;
        WorkUnit work;
        using (var store = fixture.Initialize())
        {
            work = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12));
            var submission = new SourceSubmission("primary_issue", work.IssueLocator, bytes,
                "application/octet-stream", "2026-09-21T01:02:03Z", "broodling_policy");
            bytes[0] = 99;
            first = store.EntitleSource(work.WorkUnitId, submission);
            var exposedBytes = first.Content;
            exposedBytes[1] = 0;
            await Assert.That(first.Content.SequenceEqual(originalBytes)).IsTrue();
            await Assert.That(first.ContentSha256).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(originalBytes)));
            await Assert.That(first.EntitledBy).IsEqualTo("broodling_policy");
            await Assert.That(first.EntitlementBasis).IsEqualTo("primary_authoritative_work_reference");
            var replay = store.EntitleSource(work.WorkUnitId, new("primary_issue", work.IssueLocator, originalBytes,
                "new-media-type", "later", entitlement: new("caller", "new basis")));
            await Assert.That(replay.SourceId).IsEqualTo(first.SourceId);
            await Assert.That(replay.Origin).IsEqualTo(first.Origin);
            await Assert.That(replay.RetrievedAt).IsEqualTo(first.RetrievedAt);
            await Assert.That(replay.RecordedAt).IsEqualTo(first.RecordedAt);
            await Assert.That(replay.MediaType).IsEqualTo(first.MediaType);
            await Assert.That(replay.EntitlementBasis).IsEqualTo(first.EntitlementBasis);
            var changed = store.EntitleSource(work.WorkUnitId, new("primary_issue", work.IssueLocator, bytes));
            await Assert.That(changed.SourceId).IsNotEqualTo(first.SourceId);
            await Assert.That(store.ListEntitledSources(work.WorkUnitId).Count).IsEqualTo(2);
        }
        using var reopened = fixture.Open();
        var restored = reopened.GetEntitledSource(first.SourceId);
        await Assert.That(restored.Content.SequenceEqual(originalBytes)).IsTrue();
        await Assert.That(restored.ContentSha256).IsEqualTo(first.ContentSha256);
        await Assert.That(restored.MediaType).IsEqualTo(first.MediaType);
        await Assert.That(restored.Origin).IsEqualTo(first.Origin);
        await Assert.That(restored.Locator).IsEqualTo(work.IssueLocator);
        await Assert.That(restored.EntitlementBasis).IsEqualTo(first.EntitlementBasis);
        await Assert.That(reopened.EntitleSource(work.WorkUnitId, new("primary_issue", work.IssueLocator, originalBytes)).SourceId)
            .IsEqualTo(first.SourceId);
    }

    [Test]
    public async Task EveryNonPrimaryKindNeedsExplicitGrantAndGrantIsRetained()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var work = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12));
        foreach (var kind in new[] { "referenced_document", "repository_file", "caller_statement" })
        {
            await Assert.That(() => store.EntitleSource(work.WorkUnitId, new(kind, "document", "payload"u8.ToArray())))
                .Throws<SourceNotEntitled>();
            foreach (var authority in new[] { "caller", "broodling_policy" })
            {
                var source = store.EntitleSource(work.WorkUnitId,
                    new(kind, authority, "payload"u8.ToArray(), entitlement: new(authority, "explicit decision")));
                await Assert.That(source.EntitledBy).IsEqualTo(authority);
                await Assert.That(source.EntitlementBasis).IsEqualTo("explicit decision");
            }
        }
    }

    [Test]
    public async Task ModelAndOtherUntrustedOriginsCannotEntitleThemselvesEvenWithClaimedGrants()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var work = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12));
        var selfEntitling = "{\"entitled\":true,\"entitled_by\":\"broodling_policy\"}"u8.ToArray();
        foreach (var origin in new[] { "model_extraction", "candidate_output", "referenced_material", "unknown" })
            foreach (var kind in new[] { "primary_issue", "referenced_document" })
                await Assert.That(() => store.EntitleSource(work.WorkUnitId,
                    new(kind, work.IssueLocator, selfEntitling, origin: origin, entitlement: new("caller", "claimed"))))
                    .Throws<SourceNotEntitled>();
        await Assert.That(() => store.EntitleSource(work.WorkUnitId,
            new("referenced_document", "document", selfEntitling))).Throws<SourceNotEntitled>();
        await Assert.That(store.ListEntitledSources(work.WorkUnitId).Count).IsEqualTo(0);
    }

    [Test]
    public async Task InvalidKindLocatorGrantAndForeignPrimaryIssueLeaveNoSource()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var work = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12));
        var submissions = new SourceSubmission[]
        {
            new("unknown", "document", [], entitlement: new("caller", "explicit")),
            new("primary_issue", " ", []),
            new("primary_issue", "https://github.com/acme/widget/issues/13", []),
            new("referenced_document", "document", [], entitlement: new("model_extraction", "because")),
            new("referenced_document", "document", [], entitlement: new("caller", "  "))
        };
        foreach (var submission in submissions)
            await Assert.That(() => store.EntitleSource(work.WorkUnitId, submission)).Throws<SourceNotEntitled>();
        await Assert.That(store.ListEntitledSources(work.WorkUnitId).Count).IsEqualTo(0);
        await Assert.That(() => store.EntitleSource("absent", new("primary_issue", work.IssueLocator, []))).Throws<UnknownRecord>();
    }

    [Test]
    public async Task ConcurrentEquivalentSubmissionsConvergeOnOneWorkAndSnapshot()
    {
        using var fixture = new StoreFixture();
        using (fixture.Initialize()) { }
        using var start = new Barrier(2);
        Task<(WorkUnit Work, EntitledSource Source)> Submit(string repository) => Task.Run(() =>
        {
            using var store = fixture.Open();
            start.SignalAndWait();
            var work = store.ResolveWorkUnit(WorkReference.Parse(repository, 12));
            return (work, store.EntitleSource(work.WorkUnitId, new("primary_issue", work.IssueLocator, [0, 255])));
        });
        var results = await Task.WhenAll(Submit("acme/widget"), Submit("git@github.com:Acme/Widget.git"));
        await Assert.That(results[0].Work).IsEqualTo(results[1].Work);
        await Assert.That(results[0].Source.SourceId).IsEqualTo(results[1].Source.SourceId);
        await Assert.That(results[0].Source.RecordedAt).IsEqualTo(results[1].Source.RecordedAt);
        using var reopened = fixture.Open();
        await Assert.That(reopened.ListWorkSubmissions(results[0].Work.WorkUnitId).Count).IsEqualTo(2);
        await Assert.That(reopened.ListEntitledSources(results[0].Work.WorkUnitId).Count).IsEqualTo(1);
    }

    [Test]
    public async Task SnapshotIdentityIncludesWorkKindLocatorAndContentAndSqlCannotRewriteCustody()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var work = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12));
        var otherWork = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 13));
        SourceSubmission Submission(string kind = "referenced_document", string locator = "doc", byte[]? bytes = null) =>
            new(kind, locator, bytes ?? [0], entitlement: new("caller", "explicit"));
        var first = store.EntitleSource(work.WorkUnitId, Submission());
        var variants = new[]
        {
            store.EntitleSource(otherWork.WorkUnitId, Submission()),
            store.EntitleSource(work.WorkUnitId, Submission(kind: "repository_file")),
            store.EntitleSource(work.WorkUnitId, Submission(locator: "other")),
            store.EntitleSource(work.WorkUnitId, Submission(bytes: [1]))
        };
        await Assert.That(variants.All(source => source.SourceId != first.SourceId)).IsTrue();
        foreach (var sql in new[]
        {
            "UPDATE entitled_sources SET content = X'01'", "UPDATE entitled_sources SET entitlement_basis = 'changed'",
            "UPDATE entitled_sources SET work_unit_id = 'other'", "DELETE FROM entitled_sources"
        })
            await Assert.That(() => fixture.Execute(sql)).Throws<SqliteException>();
        await Assert.That(store.GetEntitledSource(first.SourceId).Content.SequenceEqual(new byte[] { 0 })).IsTrue();
    }
}
