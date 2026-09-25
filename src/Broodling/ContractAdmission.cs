using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Broodling;

public sealed class ContractProposalInput
{
    internal ContractProposalInput(WorkUnit workUnit, IEnumerable<EntitledSource> sources,
        IEnumerable<RequiredEffect> requiredEffects, string constructedBy, RequestBundle? requestBundle = null)
    {
        WorkUnit = workUnit;
        Sources = Array.AsReadOnly(sources.ToArray());
        SourceAttribution = Array.AsReadOnly(Sources.Select(source => new SourceAttribution(source.SourceId, source.ContentSha256)).ToArray());
        RequiredEffects = Array.AsReadOnly(requiredEffects.ToArray());
        ConstructedBy = constructedBy;
        RequestBundle = requestBundle;
        BundleBinding = requestBundle is null ? null : new(requestBundle.BundleId, requestBundle.ManifestSha256!);
    }

    public WorkUnit WorkUnit { get; }
    public IReadOnlyList<EntitledSource> Sources { get; }
    public IReadOnlyList<SourceAttribution> SourceAttribution { get; }
    public IReadOnlyList<RequiredEffect> RequiredEffects { get; }
    public string ConstructedBy { get; }

    /// <summary>
    /// The completed bundle for bundle-bound admission: its members other than the Executable
    /// Request are supporting material, read through the store, never attributed sources.
    /// </summary>
    public RequestBundle? RequestBundle { get; }

    /// <summary>The exact binding the proposal must carry; null outside bundle-bound admission.</summary>
    public ContractRequestBundle? BundleBinding { get; }
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
    ContractRevision revision, AdmissionDecision? decision, IEnumerable<AttemptRecord>? attempts = null,
    IEnumerable<NativeSubmission>? submissions = null, IEnumerable<AttemptCompletion>? completions = null)
{
    public WorkUnit WorkUnit { get; } = workUnit;
    public IReadOnlyList<EntitledSource> Sources { get; } = Array.AsReadOnly(sources.ToArray());
    public ContractRevision Revision { get; } = revision;
    public AdmissionDecision? Decision { get; } = decision;
    public IReadOnlyList<AttemptRecord> Attempts { get; } = Array.AsReadOnly((attempts ?? []).ToArray());
    public IReadOnlyList<NativeSubmission> Submissions { get; } = Array.AsReadOnly((submissions ?? []).ToArray());
    public IReadOnlyList<AttemptCompletion> Completions { get; } = Array.AsReadOnly((completions ?? []).ToArray());
    // Dispatch is irreversible. This is a cleanup limit even while native execution is current;
    // only a retirement recorded under verified maintenance lifts it.
    public IReadOnlyList<string> QuarantinedAttemptIds => Submissions.Where(submission => submission.State != "prepared"
            && Attempts.FirstOrDefault(attempt => attempt.AttemptId == submission.AttemptId)?.Retirement is null)
        .Select(submission => submission.AttemptId).ToArray();
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
        RequireUnpaused();
        return AdmitCapturedSources(reference, ValidateCallerSources(sources), propose, requiredEffects, constructedBy);
    }

    private static SourceSubmission[] ValidateCallerSources(IEnumerable<SourceSubmission> sources)
    {
        if (sources is null)
            throw new InvalidContractProposal("Sources are required.");
        var supplied = sources.ToArray();
        foreach (var source in supplied)
        {
            if (source is null || source.Origin != "caller" || source.Entitlement?.GrantedBy != "caller")
                throw new SourceNotEntitled("Supplied sources require caller origin and an explicit caller grant.");
            source.EvaluateEntitlement();
        }
        return supplied;
    }

    private AdmissionStatus AdmitCapturedSources(WorkReference reference, SourceSubmission[] supplied,
        Func<ContractProposalInput, Contract> propose, IEnumerable<RequiredEffect> requiredEffects, string constructedBy)
    {
        if (constructedBy is not ("caller" or "broodling_policy" or "model_extraction"))
            throw new InvalidContractProposal("Unrecognized proposal producer.");
        if (requiredEffects is null || propose is null)
            throw new InvalidContractProposal("Sources, proposer and explicit effect authority are required.");
        // Snapshot caller collections before invoking any caller code. The callback cannot amend its grants.
        var effects = requiredEffects.ToArray();
        if (effects.Any(effect => effect is null))
            throw new InvalidContractProposal("Effect authority cannot contain null entries.");
        if (supplied.Count(source => source.Kind == "primary_issue") != 1)
            throw new SourceNotEntitled("Ingress requires exactly one primary issue snapshot.");
        var work = ResolveWorkUnit(reference);
        var captured = supplied.Select(source => EntitleSource(work.WorkUnitId, source)).ToArray();
        var inputs = new ContractProposalInput(work, captured, effects, constructedBy);
        var revision = RecordContractRevision(Proposal(inputs, propose(inputs)));
        Admit(revision.ContractRevisionId);
        return Status(revision.ContractRevisionId);
    }

    /// <summary>
    /// Admit one Issue submission's completed RequestBundle under the trusted URL-to-PR profile.
    /// The Executable Request is the only attributed source: the primary issue and available
    /// references are supporting material reached through the bound bundle, so their capture
    /// adds no work. Broodling supplies the one pull-request effect to the retained PR target
    /// branch. The revision and its submission association commit together and the decision
    /// goes through <see cref="Admit"/>; a bound submission is never proposed again. A malformed
    /// or authority-changing proposal is retained as the submission's refusal and throws
    /// <see cref="ContractProposalRefused"/>, then and on every later call.
    /// </summary>
    public AdmissionStatus AdmitRequestBundle(string submissionId, Func<ContractProposalInput, Contract> propose,
        string constructedBy = "model_extraction")
    {
        if (propose is null)
            throw new InvalidContractProposal("A proposer is required.");
        // The callback runs synchronously, so the shared body completes without awaiting I/O.
        return AdmitRequestBundle(submissionId, (input, _) => Task.FromResult(propose(input)), constructedBy,
            CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// The same admission with the bundled proposer: the supported model behind the pinned gateway,
    /// using current credentials (by default <c>GATEWAY_BASE_URL</c> and <c>GATEWAY_API_KEY</c>) that
    /// are never retained. A gateway failure throws <see cref="ContractProposerError"/> and retains
    /// nothing, so a later call proposes again from the same frozen inputs.
    /// </summary>
    public Task<AdmissionStatus> AdmitRequestBundleAsync(string submissionId, GatewayCredentials? credentials = null,
        CancellationToken cancellationToken = default) =>
        AdmitRequestBundleAsync(submissionId, credentials, null, cancellationToken);

    /// <summary>Tests supply the gateway's HTTP handler; production uses the pinned endpoint.</summary>
    internal Task<AdmissionStatus> AdmitRequestBundleAsync(string submissionId, GatewayCredentials? credentials,
        HttpMessageHandler? gateway, CancellationToken cancellationToken) =>
        AdmitRequestBundle(submissionId, new BundledProposer(this, credentials ?? GatewayCredentials.FromEnvironment(),
            gateway).ProposeAsync, "model_extraction", cancellationToken);

    private async Task<AdmissionStatus> AdmitRequestBundle(string submissionId,
        Func<ContractProposalInput, CancellationToken, Task<Contract>> propose, string constructedBy,
        CancellationToken cancellationToken)
    {
        // Proposal refuses an unrecognized producer: the proposal must validate and repeat it.
        var submission = GetIssueSubmission(submissionId);
        if (submission.ContractRevisionId is { } bound)
            return BoundAdmission(bound);
        if (submission.ProposalRefusal is { } refusal)
            throw new ContractProposalRefused(refusal);
        RequireUnpaused();
        if (submission.State == "cancelled")
            throw new IssueSubmissionConflict("A cancelled Issue submission cannot acquire Contract authority.");

        var bundle = GetRequestBundle(submissionId);
        if (bundle.State != "complete")
            throw new RequestBundleConflict("Contract admission requires a completed RequestBundle.");
        var (request, targetBranch) = ManifestAuthority(bundle, submission);
        var work = GetWorkUnit(submission.WorkUnitId);
        var effect = new RequiredEffect("pull_request",
            $"Deliver one proposal as a pull request to branch '{targetBranch}' of {work.Owner}/{work.Repository}, including its commit and push.",
            "pull_request", targetBranch);
        var inputs = new ContractProposalInput(work, [request], [effect], constructedBy, bundle);
        // No transaction is open while the proposer runs.
        Contract proposal;
        try { proposal = Proposal(inputs, await propose(inputs, cancellationToken)); }
        catch (BroodlingException refused) when (refused is InvalidContractProposal or SourceAttributionError)
        {
            return RefuseProposal(submissionId, new ContractProposalFinding(refused.Code, refused.Message));
        }
        string revisionId;
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            // A concurrent caller may have bound this submission while our proposer ran.
            // Its authority stands; ours is discarded uncommitted.
            if (ReadIssueSubmission(submissionId, transaction)!.ContractRevisionId is { } raced)
                revisionId = raced;
            else
            {
                revisionId = RecordContractRevision(proposal, transaction).ContractRevisionId;
                AssociateIssueSubmission(submissionId, revisionId, transaction);
                transaction.Commit();
            }
        }
        return BoundAdmission(revisionId);
    }

    /// <summary>
    /// Retain a refused proposal and reject its submission, as a refused capture does. A concurrent
    /// binding or an earlier refusal stands instead; a cancelled submission records nothing.
    /// </summary>
    private AdmissionStatus RefuseProposal(string submissionId, ContractProposalFinding finding)
    {
        ContractProposalRefusal refusal;
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            var current = ReadIssueSubmission(submissionId, transaction)!;
            if (current.ContractRevisionId is { } raced)
            {
                transaction.Commit();
                return BoundAdmission(raced);
            }
            if (current.ProposalRefusal is null)
            {
                if (current.State != "capturing")
                    throw new IssueSubmissionConflict("The Issue submission can no longer be prepared.");
                Execute("INSERT INTO contract_proposal_refusals VALUES ($p0, $p1, $p2)", transaction,
                    submissionId, JsonSerializer.Serialize(new[] { finding }), Now());
                Execute("UPDATE issue_submissions SET state = 'rejected' WHERE submission_id = $p0 AND state = 'capturing'",
                    transaction, submissionId);
            }
            refusal = ReadIssueSubmission(submissionId, transaction)!.ProposalRefusal!;
            transaction.Commit();
        }
        throw new ContractProposalRefused(refusal);
    }

    private AdmissionStatus BoundAdmission(string revisionId)
    {
        Admit(revisionId);
        return Status(revisionId);
    }

    /// <summary>The binding a Contract admitted from this bundle carries; none until it completes.</summary>
    private static ContractRequestBundle? Binding(RequestBundle? bundle) =>
        bundle is { State: "complete" } ? new(bundle.BundleId, bundle.ManifestSha256!) : null;

    /// <summary>
    /// The one authority rule for progressing a revision: once an associated Issue submission's
    /// RequestBundle completed, the revision must carry exactly that bundle's binding. An older
    /// association to an unbound Contract stays readable but acquires no admission or Attempt
    /// through any route. Returns the bound bundle, or null for an ordinary unbundled revision.
    /// </summary>
    private RequestBundle? RequireBundleAuthority(ContractRevision revision, SqliteTransaction transaction)
    {
        var submissions = new List<string>();
        using (var command = Command("SELECT submission_id FROM issue_submissions WHERE contract_revision_id = $p0",
            transaction, revision.ContractRevisionId))
        using (var row = command.ExecuteReader())
            while (row.Read()) submissions.Add(row.GetString(0));
        RequestBundle? bound = null;
        foreach (var bundle in submissions.Select(id => ReadRequestBundle(id, transaction)).OfType<RequestBundle>())
        {
            if (revision.Contract.RequestBundle != Binding(bundle))
                throw new IssueSubmissionConflict("The Contract is not bound to its Issue submission's completed RequestBundle.");
            bound = bundle;
        }
        return bound;
    }

    /// <summary>
    /// Take the attributed Executable Request and the PR target from the digest-verified manifest,
    /// and require the retained request source and repository preparation to be the ones it records.
    /// </summary>
    private (EntitledSource Request, string TargetBranch) ManifestAuthority(RequestBundle bundle, IssueSubmission submission)
    {
        var bytes = Encoding.UTF8.GetBytes(bundle.ManifestJson!);
        if (Digests.Bytes(bytes) != bundle.ManifestSha256)
            throw new RequestBundleConflict("The retained RequestBundle manifest does not match its digest.");
        BundleManifestV1? manifest;
        try { manifest = JsonSerializer.Deserialize<BundleManifestV1>(bytes, BundleManifestOptions); }
        catch (JsonException) { throw new RequestBundleConflict("The retained RequestBundle manifest is malformed."); }
        if (manifest is null || manifest.BundleId != bundle.BundleId || manifest.SubmissionId != submission.SubmissionId
            || manifest.WorkUnitId != submission.WorkUnitId)
            throw new RequestBundleConflict("The RequestBundle manifest names another bundle, submission or Work Unit.");
        var request = manifest.References?.Where(reference => reference.ReferenceId == "request").ToArray() ?? [];
        if (request.Length != 1 || request[0].SourceId is not { } sourceId)
            throw new RequestBundleConflict("The RequestBundle manifest records no single captured Executable Request.");
        if (manifest.Repository is not { } repository || bundle.Repository is not { } prepared
            || prepared.Repository != repository.Repository || prepared.TargetBranch != repository.DefaultBranch
            || prepared.StartingRevision != repository.StartingRevision || prepared.StartingCommit != repository.StartingCommit)
            throw new RequestBundleConflict("The retained repository preparation differs from the RequestBundle manifest.");
        var source = GetEntitledSource(sourceId);
        if (source.Kind != "executable_request" || source.ContentSha256 != request[0].ContentSha256
            || source.WorkUnitId != submission.WorkUnitId)
            throw new RequestBundleConflict("The retained Executable Request differs from the RequestBundle manifest.");
        return (source, repository.DefaultBranch);
    }

    /// <summary>Refuse a proposal that is not a valid Contract or changes the authority it was given.</summary>
    private static Contract Proposal(ContractProposalInput inputs, Contract? proposal)
    {
        if (proposal is null)
            throw new InvalidContractProposal("A typed Contract proposal is required.");
        proposal.Validate();
        if (proposal.WorkUnitId != inputs.WorkUnit.WorkUnitId || proposal.ConstructedBy != inputs.ConstructedBy)
            throw new InvalidContractProposal("The proposal changed its Work Unit or producer attribution.");
        var expectedPins = inputs.SourceAttribution.ToHashSet();
        if (proposal.SourceAttribution.Count != expectedPins.Count || !expectedPins.SetEquals(proposal.SourceAttribution))
            throw new SourceAttributionError("The proposal must pin every input snapshot exactly once, without additions or substitutions.");
        if (!proposal.RequiredEffects.SequenceEqual(inputs.RequiredEffects))
            throw new InvalidContractProposal("The proposal changed the caller's exact effect authority.");
        if (proposal.RequestBundle != inputs.BundleBinding)
            throw new InvalidContractProposal("The proposal changed its RequestBundle binding.");
        return proposal;
    }

    /// <summary>
    /// Persist an undecided immutable revision and its source bindings atomically. A
    /// bundle-bound Contract is recorded only by <see cref="AdmitRequestBundle"/>, together
    /// with its submission association, so its admission is always submission-guarded.
    /// </summary>
    public ContractRevision RecordContractRevision(Contract contract)
    {
        if (contract?.RequestBundle is not null)
            throw new InvalidContractProposal("A bundle-bound Contract is recorded only through its Issue submission.");
        using var transaction = connection.BeginTransaction(deferred: false);
        var result = RecordContractRevision(contract!, transaction);
        transaction.Commit();
        return result;
    }

    private ContractRevision RecordContractRevision(Contract contract, SqliteTransaction transaction)
    {
        if (contract is null)
            throw new InvalidContractProposal("A typed Contract is required.");
        contract.Validate();
        _ = ReadWorkUnit(contract.WorkUnitId, transaction) ?? throw new UnknownRecord("Unknown Work Unit.");
        foreach (var pin in contract.SourceAttribution)
        {
            EntitledSource source;
            try { source = ReadSource(pin.SourceId, transaction); }
            catch (UnknownRecord) { throw new SourceAttributionError("The Contract attributes an unknown source."); }
            if (source.WorkUnitId != contract.WorkUnitId || source.ContentSha256 != pin.ContentSha256)
                throw new SourceAttributionError("The Contract must pin the exact entitled source of its Work Unit.");
        }
        if (ReadRevision(contract.ContractRevisionId, transaction) is { } existing)
            return existing;
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
        return ReadRevision(contract.ContractRevisionId, transaction)!;
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
            || contract.ContractSha256 != row.GetString(3) || contract.ConstructedBy != row.GetString(5))
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
        RequireIssueSubmissionNotCancelled(revisionId, transaction);
        RequireBundleAuthority(revision, transaction);
        var existing = ReadDecision(revisionId, transaction);
        if (existing is not null)
        {
            transaction.Commit();
            return existing;
        }
        RequireUnpaused(transaction);
        var work = ReadWorkUnit(revision.WorkUnitId, transaction)!;
        var assessment = Closability.Assess(revision.Contract, work.Host);
        Execute("INSERT INTO admission_decisions VALUES ($p0, $p1, $p2, $p3, $p4, $p5)", transaction,
            "ad-" + Digests.Parts("broodling.dotnet.admission.v1", revisionId), revisionId,
            assessment.Outcome, JsonSerializer.Serialize(assessment.Findings), AdmissionPolicyVersion, Now());
        // A bundle-bound submission reports its outcome with the decision; its capture is over.
        Execute("UPDATE issue_submissions SET state = $p0 WHERE contract_revision_id = $p1 AND state = 'capturing'",
            transaction, assessment.Outcome, revisionId);
        var result = ReadDecision(revisionId, transaction)!;
        transaction.Commit();
        return result;
    }

    private void RequireIssueSubmissionNotCancelled(string contractRevisionId,
        Microsoft.Data.Sqlite.SqliteTransaction transaction)
    {
        using var command = Command("""
            SELECT 1
            WHERE EXISTS (
                SELECT 1 FROM issue_submissions
                WHERE contract_revision_id = $p0 AND state = 'cancelled'
            )
            AND NOT EXISTS (
                SELECT 1 FROM issue_submissions
                WHERE contract_revision_id = $p0 AND state <> 'cancelled'
            )
            """, transaction, contractRevisionId);
        if (command.ExecuteScalar() is not null)
            throw new IssueSubmissionConflict("A cancelled Issue submission cannot progress its Contract.");
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
        var attempts = ReadAttempts("contract_revision_id = $p0", revisionId, transaction);
        var result = new AdmissionStatus(work, sources, revision, ReadDecision(revisionId, transaction), attempts,
            attempts.Select(attempt => ReadSubmission(attempt.AttemptId, transaction)).OfType<NativeSubmission>(),
            attempts.Select(attempt => ReadCompletion(attempt.AttemptId, transaction)).OfType<AttemptCompletion>());
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
