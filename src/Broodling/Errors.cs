namespace Broodling;

/// <summary>A refused application operation; the code is safe for operator output.</summary>
public class BroodlingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class InvalidWorkReference(string message)
    : BroodlingException("invalid_work_reference", message);

public sealed class WorkUnitIdentityConflict(string message)
    : BroodlingException("work_unit_identity_conflict", message);

public sealed class SourceNotEntitled(string message)
    : BroodlingException("source_not_entitled", message);

public sealed class UnknownRecord(string message)
    : BroodlingException("unknown_record", message);

public sealed class StoreStateException(string code, string message)
    : BroodlingException(code, message);
