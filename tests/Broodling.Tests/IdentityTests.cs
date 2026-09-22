using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class IdentityTests
{
    [Test]
    [Arguments("repository")]
    [Arguments("issue")]
    [Arguments("repositoryIdentity")]
    [Arguments("issueIdentity")]
    public async Task MalformedCallerTextCannotRewriteIdentityOrSubmission(string field)
    {
        using var fixture = new StoreFixture();
        WorkUnit first;
        using (var store = fixture.Initialize())
        {
            first = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12));
            await Assert.That(() => store.ResolveWorkUnit(WorkReference.Parse(
                field == "repository" ? "https://\ud800@github.com/acme/widget" : "acme/widget",
                field == "issue" ? "https://\ud800@github.com/acme/widget/issues/12" : "12",
                repositoryIdentity: field == "repositoryIdentity" ? "R-\ud800" : "R-valid",
                issueIdentity: field == "issueIdentity" ? "I-\udc00" : "I-valid")))
                .Throws<InvalidWorkReference>();
            await Assert.That(store.GetWorkUnit(first.WorkUnitId)).IsEqualTo(first);
            await Assert.That(store.ListWorkSubmissions(first.WorkUnitId).Count).IsEqualTo(1);
        }
        using var reopened = fixture.Open();
        await Assert.That(reopened.GetWorkUnit(first.WorkUnitId)).IsEqualTo(first);
        await Assert.That(reopened.ListWorkSubmissions(first.WorkUnitId).Count).IsEqualTo(1);
    }

    [Test]
    public async Task CanonicalFormsConvergeAcrossReopenAndRetainEveryRawSubmission()
    {
        using var fixture = new StoreFixture();
        WorkUnit first;
        var forms = new[]
        {
            "https://github.com/faviann/broodling", "https://github.com/Faviann/Broodling.git/",
            "ssh://git@github.com/Faviann/Broodling", "git@github.com:faviann/broodling.git",
            "github.com/faviann/broodling/", "faviann/broodling"
        };
        var issues = new[] { "12", "#12", "https://github.com/faviann/broodling/issues/12" };
        using (var store = fixture.Initialize())
        {
            first = store.ResolveWorkUnit(WorkReference.Parse(forms[0], 12));
            foreach (var repository in forms)
                foreach (var issue in issues)
                    await Assert.That(store.ResolveWorkUnit(WorkReference.Parse(repository, issue))).IsEqualTo(first);
            var submissions = store.ListWorkSubmissions(first.WorkUnitId);
            await Assert.That(submissions.Count).IsEqualTo(19);
            await Assert.That(submissions.Any(item => item.SubmittedRepository == forms[3] && item.SubmittedIssue == "#12")).IsTrue();
        }
        using var reopened = fixture.Open();
        await Assert.That(reopened.GetWorkUnit(first.WorkUnitId)).IsEqualTo(first);
        await Assert.That(reopened.ResolveWorkUnit(WorkReference.Parse("faviann/broodling", 12))).IsEqualTo(first);
        await Assert.That(reopened.ListWorkSubmissions(first.WorkUnitId).Count).IsEqualTo(20);
    }

    [Test]
    public async Task EachCanonicalComponentDeterminesIdentityAndMismatchedLocatorsRefuse()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var original = store.ResolveWorkUnit(WorkReference.Parse("github.com/acme/widget", 12));
        foreach (var (repository, issue) in new[]
        {
            ("gitlab.com/acme/widget", 12), ("github.com/other/widget", 12),
            ("github.com/acme/other", 12), ("github.com/acme/widget", 13)
        })
        {
            var other = store.ResolveWorkUnit(WorkReference.Parse(repository, issue));
            await Assert.That(other.WorkUnitId).IsNotEqualTo(original.WorkUnitId);
            await Assert.That(() => store.ResolveWorkUnit(WorkReference.Parse(repository, issue), original.WorkUnitId))
                .Throws<WorkUnitIdentityConflict>();
            if (issue == 12)
                await Assert.That(() => WorkReference.Parse(repository, "https://github.com/acme/widget/issues/12"))
                    .Throws<InvalidWorkReference>();
        }
        await Assert.That(store.GetWorkUnit(original.WorkUnitId)).IsEqualTo(original);
    }

    [Test]
    [Arguments("", "12")]
    [Arguments("acme", "12")]
    [Arguments("https://github.com/acme/widget/deeper", "12")]
    [Arguments("file:///acme/widget", "12")]
    [Arguments("ftp://github.com/acme/widget", "12")]
    [Arguments("acme/widget", "0")]
    [Arguments("acme/widget", "-1")]
    [Arguments("acme/widget", "12.5")]
    [Arguments("acme/widget", "")]
    public async Task UnusableReferencesRefuse(string repository, string issue) =>
        await Assert.That(() => WorkReference.Parse(repository, issue)).Throws<InvalidWorkReference>();

    [Test]
    public async Task ConflictingPinsLeaveNoPartialIdentityOrSubmissionAndObservationNeverPins()
    {
        using var fixture = new StoreFixture();
        using (var store = fixture.Initialize())
        {
            var first = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12, issueIdentity: "I-original"));
            var count = store.ListWorkSubmissions(first.WorkUnitId).Count;
            await Assert.That(() => store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12, "R-new", "I-conflict")))
                .Throws<WorkUnitIdentityConflict>();
            await Assert.That(store.GetWorkUnit(first.WorkUnitId)).IsEqualTo(first);
            await Assert.That(store.ListWorkSubmissions(first.WorkUnitId).Count).IsEqualTo(count);
            await Assert.That(store.FindWorkUnit(WorkReference.Parse("acme/widget", 12, "R-observe"))).IsEqualTo(first);
            await Assert.That(store.ListWorkSubmissions(first.WorkUnitId).Count).IsEqualTo(count);
            await Assert.That(store.FindWorkUnit(WorkReference.Parse("acme/widget", 99))).IsNull();
            var pinned = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12, "R-new", "I-original"));
            await Assert.That(pinned.RepositoryIdentity).IsEqualTo("R-new");
            await Assert.That(store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12))).IsEqualTo(pinned);
        }
        using var reopened = fixture.Open();
        await Assert.That(() => reopened.FindWorkUnit(WorkReference.Parse("acme/widget", 12, "R-conflict")))
            .Throws<WorkUnitIdentityConflict>();
        await Assert.That(() => reopened.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12, "R-conflict")))
            .Throws<WorkUnitIdentityConflict>();
    }

    [Test]
    public async Task DatabasePreventsIdentityRewriteAndUnpinning()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var original = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12, "R-original", "I-original"));
        foreach (var sql in new[]
        {
            "UPDATE work_units SET issue_number = 13", "UPDATE work_units SET reference_key = 'other'",
            "UPDATE work_units SET repository_identity = NULL", "UPDATE work_units SET issue_identity = 'I-new'",
            "DELETE FROM work_units", "UPDATE work_submissions SET submitted_issue = '13'", "DELETE FROM work_submissions"
        })
            await Assert.That(() => fixture.Execute(sql)).Throws<SqliteException>();
        await Assert.That(store.GetWorkUnit(original.WorkUnitId)).IsEqualTo(original);
    }

    [Test]
    public async Task SubmissionFailureRollsBackNewWorkAndNewPins()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var first = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12));
        fixture.Execute("CREATE TRIGGER fail_submission BEFORE INSERT ON work_submissions BEGIN SELECT RAISE(ABORT, 'injected interruption'); END;");
        await Assert.That(() => store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12, "R-new"))).Throws<SqliteException>();
        await Assert.That(() => store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 13))).Throws<SqliteException>();
        await Assert.That(store.GetWorkUnit(first.WorkUnitId)).IsEqualTo(first);
        await Assert.That(store.FindWorkUnit(WorkReference.Parse("acme/widget", 13))).IsNull();
        await Assert.That(store.ListWorkSubmissions(first.WorkUnitId).Count).IsEqualTo(1);
        fixture.Execute("DROP TRIGGER fail_submission");
    }

    [Test]
    public async Task RacingSessionsConvergeAndOnlyOneConflictingPinWins()
    {
        using var fixture = new StoreFixture();
        using (fixture.Initialize()) { }
        using var start = new Barrier(2);
        Task<(WorkUnit? Work, bool Conflict)> Submit(string pin) => Task.Run(() =>
        {
            using var store = fixture.Open();
            start.SignalAndWait();
            try { return (store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12, pin)), false); }
            catch (WorkUnitIdentityConflict) { return ((WorkUnit?)null, true); }
        });
        var results = await Task.WhenAll(Submit("R-one"), Submit("R-two"));
        await Assert.That(results.Count(result => result.Conflict)).IsEqualTo(1);
        using var reopened = fixture.Open();
        var winner = results.Single(result => !result.Conflict).Work!;
        await Assert.That(reopened.GetWorkUnit(winner.WorkUnitId)).IsEqualTo(winner);
        await Assert.That(reopened.ListWorkSubmissions(winner.WorkUnitId).Count).IsEqualTo(1);
        await Assert.That(reopened.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12))).IsEqualTo(winner);
    }
}
