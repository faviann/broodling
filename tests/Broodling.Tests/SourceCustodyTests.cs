using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class SourceCustodyTests
{
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
