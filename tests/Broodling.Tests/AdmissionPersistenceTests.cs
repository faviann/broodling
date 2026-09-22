using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class AdmissionPersistenceTests
{
    [Test]
    public async Task InterruptedRevisionAndDecisionWritesRollBackWhileCommittedUndecidedMeaningSurvivesReopen()
    {
        using var fixture = new StoreFixture();
        Contract contract;
        using (var store = fixture.Initialize())
        {
            var work = store.ResolveWorkUnit(ContractIngressTests.Reference);
            var source = store.EntitleSource(work.WorkUnitId, ContractIngressTests.Primary());
            contract = new(work.WorkUnitId, [new(source.SourceId, source.ContentSha256)], [new("c", "Preserve this outcome.")]);
            // Fail after the source binding insertion, inside the real revision transaction.
            fixture.Execute("CREATE TRIGGER interrupt_revision AFTER INSERT ON contract_sources BEGIN SELECT RAISE(ABORT, 'interrupted revision'); END;");
            await Assert.That(() => store.RecordContractRevision(contract)).Throws<SqliteException>();
            fixture.Execute("DROP TRIGGER interrupt_revision");
        }
        using (var reopened = fixture.Open())
        {
            await Assert.That(reopened.History(ContractIngressTests.Reference).Count).IsEqualTo(0);
            await Assert.That(reopened.ListEntitledSources(contract.WorkUnitId).Count).IsEqualTo(1);
            await Assert.That(reopened.IsAdmitted(contract.ContractRevisionId)).IsFalse();
            reopened.RecordContractRevision(contract);
        }
        using (var reopened = fixture.Open())
        {
            var status = reopened.Status(contract.ContractRevisionId);
            await Assert.That(status.Decision).IsNull();
            await Assert.That(status.Sources.Count).IsEqualTo(1);
            await Assert.That(reopened.IsAdmitted(contract.ContractRevisionId)).IsFalse();
            // Fail after the decision insertion; no partial admission may escape rollback.
            fixture.Execute("CREATE TRIGGER interrupt_admission AFTER INSERT ON admission_decisions BEGIN SELECT RAISE(ABORT, 'interrupted decision'); END;");
            await Assert.That(() => reopened.Admit(contract.ContractRevisionId)).Throws<SqliteException>();
            fixture.Execute("DROP TRIGGER interrupt_admission");
        }
        AdmissionDecision decision;
        using (var reopened = fixture.Open())
        {
            await Assert.That(reopened.Status(contract.ContractRevisionId).Decision).IsNull();
            decision = reopened.Admit(contract.ContractRevisionId);
            await Assert.That(decision.Admitted).IsTrue();
        }
        using var last = fixture.Open();
        await Assert.That(last.Admit(contract.ContractRevisionId).DecisionId).IsEqualTo(decision.DecisionId);
        await Assert.That(last.Admit(contract.ContractRevisionId).DecidedAt).IsEqualTo(decision.DecidedAt);
        await Assert.That(last.Status(contract.ContractRevisionId).Revision.CanonicalBytes.SequenceEqual(contract.CanonicalBytes())).IsTrue();
    }

    [Test]
    public async Task ConcurrentAdmissionConvergesOnOriginalRevisionDecisionAndLineage()
    {
        using var fixture = new StoreFixture();
        using (fixture.Initialize()) { }
        using var start = new Barrier(2);
        Task<AdmissionStatus> Submit() => Task.Run(() =>
        {
            using var store = fixture.Open();
            start.SignalAndWait();
            return store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()], ContractIngressTests.Propose, []);
        });
        var statuses = await Task.WhenAll(Submit(), Submit());
        await Assert.That(statuses[0].Revision.ContractRevisionId).IsEqualTo(statuses[1].Revision.ContractRevisionId);
        await Assert.That(statuses[0].Revision.RecordedAt).IsEqualTo(statuses[1].Revision.RecordedAt);
        await Assert.That(statuses[0].Decision!.DecidedAt).IsEqualTo(statuses[1].Decision!.DecidedAt);
        using var reopened = fixture.Open();
        await Assert.That(reopened.History(ContractIngressTests.Reference).Count).IsEqualTo(1);
    }

    [Test]
    public async Task ObservationDoesNotAcquireWriterPinIdentityOrRecordSubmissions()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var status = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()], ContractIngressTests.Propose, []);
        var before = store.ListWorkSubmissions(status.WorkUnit.WorkUnitId).Count;
        using (var writer = fixture.Connect())
        using (var transaction = writer.BeginTransaction(deferred: false))
        {
            using var command = writer.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE work_units SET repository_identity = 'uncommitted'";
            command.ExecuteNonQuery();
            var observed = store.Status(status.Revision.ContractRevisionId);
            await Assert.That(observed.WorkUnit.RepositoryIdentity).IsNull();
            await Assert.That(observed.Decision!.DecisionId).IsEqualTo(status.Decision!.DecisionId);
            await Assert.That(store.History(WorkReference.Parse("acme/widget", 12, repositoryIdentity: "not-yet-pinned", issueIdentity: "also-unknown")).Count).IsEqualTo(1);
            await Assert.That(store.History(WorkReference.Parse("acme/unknown", 99)).Count).IsEqualTo(0);
        }
        await Assert.That(store.GetWorkUnit(status.WorkUnit.WorkUnitId).RepositoryIdentity).IsNull();
        await Assert.That(store.GetWorkUnit(status.WorkUnit.WorkUnitId).IssueIdentity).IsNull();
        await Assert.That(store.ListWorkSubmissions(status.WorkUnit.WorkUnitId).Count).IsEqualTo(before);
        store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12, repositoryIdentity: "repo", issueIdentity: "issue"));
        foreach (var reference in new[]
        {
            WorkReference.Parse("acme/widget", 12, repositoryIdentity: "wrong"),
            WorkReference.Parse("acme/widget", 12, issueIdentity: "wrong")
        })
            await Assert.That(() => store.History(reference)).Throws<WorkUnitIdentityConflict>();
        await Assert.That(store.History(WorkReference.Parse("acme/widget", 12, repositoryIdentity: "repo", issueIdentity: "issue")).Count).IsEqualTo(1);
        await Assert.That(store.History(ContractIngressTests.Reference).Count).IsEqualTo(1);
        await Assert.That(store.ListWorkSubmissions(status.WorkUnit.WorkUnitId).Count).IsEqualTo(before + 1);
    }

    [Test]
    public async Task DatabaseRefusesRevisionDecisionAndBindingAmendmentIncludingExtraSourceInsertion()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var status = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()], ContractIngressTests.Propose, []);
        var unused = store.EntitleSource(status.WorkUnit.WorkUnitId, ContractIngressTests.Supplement);
        foreach (var sql in new[]
        {
            "UPDATE contract_revisions SET canonical_bytes = X'7B7D'", "DELETE FROM contract_revisions",
            "UPDATE contract_sources SET content_sha256 = 'wrong'", "DELETE FROM contract_sources",
            "UPDATE admission_decisions SET outcome = 'rejected'", "DELETE FROM admission_decisions",
            $"INSERT INTO contract_sources VALUES ('{status.Revision.ContractRevisionId}', '{unused.SourceId}', '{unused.ContentSha256}')"
        })
            await Assert.That(() => fixture.Execute(sql)).Throws<SqliteException>();
        await Assert.That(store.Status(status.Revision.ContractRevisionId).Revision.CanonicalBytes.SequenceEqual(status.Revision.CanonicalBytes)).IsTrue();
        await Assert.That(store.IsAdmitted(status.Revision.ContractRevisionId)).IsTrue();
    }

    [Test]
    public async Task OutOfBandCorruptionCannotBeReadOrAdmittedAsFrozenMeaning()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var status = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()], ContractIngressTests.Propose, []);
        fixture.Execute("DROP TRIGGER revisions_no_update; UPDATE contract_revisions SET canonical_bytes = X'7B7D'");
        await Assert.That(() => store.GetContractRevision(status.Revision.ContractRevisionId)).Throws<ContractImmutabilityError>();
        await Assert.That(() => store.Admit(status.Revision.ContractRevisionId)).Throws<ContractImmutabilityError>();
        await Assert.That(() => store.Status(status.Revision.ContractRevisionId)).Throws<ContractImmutabilityError>();
    }
}
