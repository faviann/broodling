namespace Broodling;

public sealed class CessationUnconfirmed(string message) : BroodlingException("cessation_unconfirmed", message);

public sealed class SubmissionNotReady(string message) : BroodlingException("submission_not_ready", message);
public sealed class InstallationPaused(string message = "Installation admission and dispatch are paused.")
    : BroodlingException("installation_paused", message);
public sealed class SubmissionConflict(string message, string? existingRunId = null) : BroodlingException("submission_conflict", message)
{
    public string? ExistingRunId { get; } = existingRunId;
}
public sealed class UnsupportedRuntime(string message) : BroodlingException("unsupported_runtime", message);
public sealed class NativeTransportError(string kind = "transport_failed") : BroodlingException("native_transport_error", "Native transport did not return a usable response.")
{
    public string Kind { get; } = kind;
}

public sealed class WorktreeProvisioningError(string message) : BroodlingException("worktree_provisioning_error", message);
public sealed class WorktreeOwnershipConflict(string message) : BroodlingException("worktree_ownership_conflict", message);

/// <summary>A refused application operation; the code is safe for operator output.</summary>
public class BroodlingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class InvalidWorkReference(string message)
    : BroodlingException("invalid_work_reference", message);

public sealed class WorkUnitIdentityConflict(string message)
    : BroodlingException("work_unit_identity_conflict", message);

public sealed class IssueSubmissionConflict(string message)
    : BroodlingException("issue_submission_conflict", message);

public sealed class RequestBundleConflict(string message)
    : BroodlingException("request_bundle_conflict", message);

public sealed class SourceNotEntitled(string message)
    : BroodlingException("source_not_entitled", message);

public sealed class UnknownRecord(string message)
    : BroodlingException("unknown_record", message);

public sealed class InvalidContractProposal(string message)
    : BroodlingException("invalid_contract_proposal", message);

public sealed class SourceAttributionError(string message)
    : BroodlingException("source_attribution_error", message);

public sealed class ContractImmutabilityError(string message)
    : BroodlingException("contract_immutability_error", message);

public sealed class StoreStateException(string code, string message)
    : BroodlingException(code, message);

public sealed class UnsupportedStartingState(string message)
    : BroodlingException("unsupported_starting_state", message);

public sealed class ResultRetentionError(string message)
    : BroodlingException("result_retention_error", message);

public sealed class UnsupportedWorkspaceRoot(string message)
    : BroodlingException("unsupported_workspace_root", message);

public sealed class AttemptAdmissionError(string message)
    : BroodlingException("attempt_admission_error", message);

public sealed class AttemptConflict(string message)
    : BroodlingException("attempt_conflict", message);

public sealed class StaleAttempt(string message)
    : BroodlingException("stale_attempt", message);
