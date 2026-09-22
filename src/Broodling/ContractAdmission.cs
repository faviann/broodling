using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Broodling;

public sealed class ContractProposalInput
{
    internal ContractProposalInput(WorkUnit workUnit, IEnumerable<EntitledSource> sources,
        IEnumerable<RequiredEffect> requiredEffects, string constructedBy)
    {
        WorkUnit = workUnit;
        Sources = Array.AsReadOnly(sources.ToArray());
        SourceAttribution = Array.AsReadOnly(Sources.Select(source => new SourceAttribution(source.SourceId, source.ContentSha256)).ToArray());
        RequiredEffects = Array.AsReadOnly(requiredEffects.ToArray());
        ConstructedBy = constructedBy;
    }

    public WorkUnit WorkUnit { get; }
    public IReadOnlyList<EntitledSource> Sources { get; }
    public IReadOnlyList<SourceAttribution> SourceAttribution { get; }
    public IReadOnlyList<RequiredEffect> RequiredEffects { get; }
    public string ConstructedBy { get; }
}

public sealed class ContractRevision
{
    private readonly byte[] bytes;
    internal ContractRevision(string contractRevisionId, string workUnitId, long revisionNumber,
        string contractSha256, byte[] canonicalBytes, string constructedBy, string? supersedesRevisionId,
        string recordedAt, Contract contract)
    {
        ContractRevisionId = contractRevisionId;
        WorkUnitId = workUnitId;
        RevisionNumber = revisionNumber;
        ContractSha256 = contractSha256;
        bytes = (byte[])canonicalBytes.Clone();
        ConstructedBy = constructedBy;
        SupersedesRevisionId = supersedesRevisionId;
        RecordedAt = recordedAt;
        Contract = contract;
    }

    public string ContractRevisionId { get; }
    public string WorkUnitId { get; }
    public long RevisionNumber { get; }
    public string ContractSha256 { get; }
    public byte[] CanonicalBytes => (byte[])bytes.Clone();
    public string ConstructedBy { get; }
    public string? SupersedesRevisionId { get; }
    public string RecordedAt { get; }
    public Contract Contract { get; }
}

public sealed class AdmissionDecision(string decisionId, string contractRevisionId, string outcome,
    IEnumerable<ClosabilityFinding> findings, string policyVersion, string decidedAt)
{
    public string DecisionId { get; } = decisionId;
    public string ContractRevisionId { get; } = contractRevisionId;
    public string Outcome { get; } = outcome;
    public bool Admitted => Outcome == "admitted";
    public IReadOnlyList<ClosabilityFinding> Findings { get; } = Array.AsReadOnly(findings.ToArray());
    public string PolicyVersion { get; } = policyVersion;
    public string DecidedAt { get; } = decidedAt;
}

/// <summary>Retained facts for one exact revision; null decision grants no admission.</summary>
public sealed class AdmissionStatus(WorkUnit workUnit, IEnumerable<EntitledSource> sources,
    ContractRevision revision, AdmissionDecision? decision)
{
    public WorkUnit WorkUnit { get; } = workUnit;
    public IReadOnlyList<EntitledSource> Sources { get; } = Array.AsReadOnly(sources.ToArray());
    public ContractRevision Revision { get; } = revision;
    public AdmissionDecision? Decision { get; } = decision;
}

public sealed partial class BroodlingStore
{
    // Versioned admission semantics, separate from physical schema and future execution configuration.
    private const string AdmissionPolicyVersion = "broodling.dotnet.admission.v1/zeroshot-10.3.0";

    /// <summary>
    /// Capture explicitly granted caller bytes and admit the caller's typed proposal.
    /// The complete frozen sources remain authority alongside the Contract. This creates no Attempt.
    /// </summary>
    public AdmissionStatus AdmitSources(WorkReference reference, IEnumerable<SourceSubmission> sources,
        Func<ContractProposalInput, Contract> propose, IEnumerable<RequiredEffect> requiredEffects,
        string constructedBy = "model_extraction")
    {
        if (constructedBy is not ("caller" or "broodling_policy" or "model_extraction"))
            throw new InvalidContractProposal("Unrecognized proposal producer.");
        if (sources is null || requiredEffects is null || propose is null)
            throw new InvalidContractProposal("Sources, proposer and explicit effect authority are required.");
        // Snapshot caller collections before invoking any caller code. The callback cannot amend its grants.
        var supplied = sources.ToArray();
        var effects = requiredEffects.ToArray();
        if (effects.Any(effect => effect is null))
            throw new InvalidContractProposal("Effect authority cannot contain null entries.");
        foreach (var source in supplied)
        {
            if (source is null || source.Origin != "caller" || source.Entitlement?.GrantedBy != "caller")
                throw new SourceNotEntitled("Supplied sources require caller origin and an explicit caller grant.");
            source.EvaluateEntitlement();
        }
        if (supplied.Count(source => source.Kind == "primary_issue") != 1)
            throw new SourceNotEntitled("Ingress requires exactly one primary issue snapshot.");
        var work = ResolveWorkUnit(reference);
        var captured = supplied.Select(source => EntitleSource(work.WorkUnitId, source)).ToArray();
        var inputs = new ContractProposalInput(work, captured, effects, constructedBy);
        var proposal = propose(inputs) ?? throw new InvalidContractProposal("A typed Contract proposal is required.");
        proposal.Validate();
        if (proposal.WorkUnitId != work.WorkUnitId || proposal.ConstructedBy != constructedBy)
            throw new InvalidContractProposal("The proposal changed its Work Unit or producer attribution.");
        var expectedPins = inputs.SourceAttribution.ToHashSet();
        if (proposal.SourceAttribution.Count != expectedPins.Count || !expectedPins.SetEquals(proposal.SourceAttribution))
            throw new SourceAttributionError("The proposal must pin every input snapshot exactly once, without additions or substitutions.");
        if (!proposal.RequiredEffects.SequenceEqual(effects))
            throw new InvalidContractProposal("The proposal changed the caller's exact effect authority.");
        var revision = RecordContractRevision(proposal);
        Admit(revision.ContractRevisionId);
        return Status(revision.ContractRevisionId);
    }

    /// <summary>Persist an undecided immutable revision and its source bindings atomically.</summary>
    public ContractRevision RecordContractRevision(Contract contract)
    {
        if (contract is null)
            throw new InvalidContractProposal("A typed Contract is required.");
        contract.Validate();
        using var transaction = connection.BeginTransaction(deferred: false);
        _ = ReadWorkUnit(contract.WorkUnitId, transaction) ?? throw new UnknownRecord("Unknown Work Unit.");
        foreach (var pin in contract.SourceAttribution)
        {
            EntitledSource source;
            try { source = ReadSource(pin.SourceId, transaction); }
            catch (UnknownRecord) { throw new SourceAttributionError("The Contract attributes an unknown source."); }
            if (source.WorkUnitId != contract.WorkUnitId || source.ContentSha256 != pin.ContentSha256)
                throw new SourceAttributionError("The Contract must pin the exact entitled source of its Work Unit.");
        }
        var existing = ReadRevision(contract.ContractRevisionId, transaction);
        if (existing is not null)
        {
            transaction.Commit();
            return existing;
        }
        string? previousId = null;
        long number = 1;
        using (var previous = Command("SELECT contract_revision_id, revision_number FROM contract_revisions WHERE work_unit_id = $p0 ORDER BY revision_number DESC LIMIT 1",
            transaction, contract.WorkUnitId))
        using (var row = previous.ExecuteReader())
        {
            if (row.Read())
            {
                previousId = row.GetString(0);
                number = checked(row.GetInt64(1) + 1);
            }
        }
        Execute("INSERT INTO contract_revisions VALUES ($p0, $p1, $p2, $p3, $p4, $p5, $p6, $p7)", transaction,
            contract.ContractRevisionId, contract.WorkUnitId, number, contract.ContractSha256, contract.CanonicalBytes(),
            contract.ConstructedBy, previousId, Now());
        foreach (var pin in contract.SourceAttribution)
            Execute("INSERT INTO contract_sources VALUES ($p0, $p1, $p2)", transaction,
                contract.ContractRevisionId, pin.SourceId, pin.ContentSha256);
        var result = ReadRevision(contract.ContractRevisionId, transaction)!;
        transaction.Commit();
        return result;
    }

    public ContractRevision GetContractRevision(string revisionId) => ReadRevision(revisionId)
        ?? throw new UnknownRecord("Unknown Contract revision.");

    private ContractRevision? ReadRevision(string revisionId, SqliteTransaction? transaction = null)
    {
        using var command = Command("SELECT * FROM contract_revisions WHERE contract_revision_id = $p0", transaction, revisionId);
        using var row = command.ExecuteReader();
        if (!row.Read()) return null;
        var bytes = (byte[])row[4];
        Contract contract;
        try { contract = Contract.FromCanonicalBytes(bytes); }
        catch (Exception exception) when (exception is JsonException or InvalidContractProposal or ArgumentException)
        {
            throw new ContractImmutabilityError("The retained Contract no longer has valid canonical meaning.");
        }
        if (contract.ContractRevisionId != row.GetString(0) || contract.WorkUnitId != row.GetString(1)
            || contract.ContractSha256 != row.GetString(3) || contract.ConstructedBy != row.GetString(5)
            || !contract.CanonicalBytes().SequenceEqual(bytes))
            throw new ContractImmutabilityError("The retained Contract differs from its recorded identity.");
        var pins = ReadPins(revisionId, transaction);
        if (!pins.SequenceEqual(contract.SourceAttribution))
            throw new ContractImmutabilityError("The retained source bindings differ from the frozen Contract.");
        return new(row.GetString(0), row.GetString(1), row.GetInt64(2), row.GetString(3), bytes,
            row.GetString(5), row.IsDBNull(6) ? null : row.GetString(6), row.GetString(7), contract);
    }

    private IReadOnlyList<SourceAttribution> ReadPins(string revisionId, SqliteTransaction? transaction)
    {
        using var command = Command("SELECT source_id, content_sha256 FROM contract_sources WHERE contract_revision_id = $p0 ORDER BY source_id, content_sha256", transaction, revisionId);
        using var row = command.ExecuteReader();
        var pins = new List<SourceAttribution>();
        while (row.Read()) pins.Add(new(row.GetString(0), row.GetString(1)));
        return pins;
    }

    /// <summary>Record or replay a deterministic decision. A revision alone is not admitted.</summary>
    public AdmissionDecision Admit(string revisionId)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        var revision = ReadRevision(revisionId, transaction) ?? throw new UnknownRecord("Unknown Contract revision.");
        var existing = ReadDecision(revisionId, transaction);
        if (existing is not null)
        {
            transaction.Commit();
            return existing;
        }
        var work = ReadWorkUnit(revision.WorkUnitId, transaction)!;
        var assessment = Closability.Assess(revision.Contract, work.Host);
        Execute("INSERT INTO admission_decisions VALUES ($p0, $p1, $p2, $p3, $p4, $p5)", transaction,
            "ad-" + Digests.Parts("broodling.dotnet.admission.v1", revisionId), revisionId,
            assessment.Outcome, JsonSerializer.Serialize(assessment.Findings), AdmissionPolicyVersion, Now());
        var result = ReadDecision(revisionId, transaction)!;
        transaction.Commit();
        return result;
    }

    public AdmissionDecision? FindAdmissionDecision(string revisionId) => ReadDecision(revisionId);
    public bool IsAdmitted(string revisionId) => FindAdmissionDecision(revisionId)?.Admitted == true;

    private AdmissionDecision? ReadDecision(string revisionId, SqliteTransaction? transaction = null)
    {
        using var command = Command("SELECT * FROM admission_decisions WHERE contract_revision_id = $p0", transaction, revisionId);
        using var row = command.ExecuteReader();
        if (!row.Read()) return null;
        return new(row.GetString(0), row.GetString(1), row.GetString(2),
            JsonSerializer.Deserialize<ClosabilityFinding[]>(row.GetString(3))!, row.GetString(4), row.GetString(5));
    }

    /// <summary>Read exact retained lineage in a coherent snapshot without reserving the writer.</summary>
    public AdmissionStatus Status(string revisionId)
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        var revision = ReadRevision(revisionId, transaction) ?? throw new UnknownRecord("Unknown Contract revision.");
        var work = ReadWorkUnit(revision.WorkUnitId, transaction)!;
        var sources = revision.Contract.SourceAttribution.Select(pin => ReadSource(pin.SourceId, transaction)).ToArray();
        var result = new AdmissionStatus(work, sources, revision, ReadDecision(revisionId, transaction));
        transaction.Commit();
        return result;
    }

    /// <summary>Oldest-first retained revisions; checks known identity pins without adding new ones.</summary>
    public IReadOnlyList<AdmissionStatus> History(WorkReference reference)
    {
        if (FindWorkUnit(reference) is not { } work) return Array.Empty<AdmissionStatus>();
        var ids = new List<string>();
        using (var command = Command("SELECT contract_revision_id FROM contract_revisions WHERE work_unit_id = $p0 ORDER BY revision_number", null, work.WorkUnitId))
        using (var row = command.ExecuteReader())
            while (row.Read()) ids.Add(row.GetString(0));
        return Array.AsReadOnly(ids.Select(Status).ToArray());
    }
}
