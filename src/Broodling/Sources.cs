namespace Broodling;

public sealed record SourceEntitlement(string GrantedBy, string Basis);

/// <summary>Trusted presentation metadata, separate from the uninterpreted payload.</summary>
public sealed class SourceSubmission(string kind, string locator, byte[] content,
    string mediaType = "text/plain; charset=utf-8", string retrievedAt = "",
    string origin = "caller", SourceEntitlement? entitlement = null)
{
    private readonly byte[] bytes = (byte[])content.Clone();
    public string Kind { get; } = kind;
    public string Locator { get; } = locator;
    public byte[] Content => (byte[])bytes.Clone();
    public string MediaType { get; } = mediaType;
    public string RetrievedAt { get; } = retrievedAt;
    public string Origin { get; } = origin;
    public SourceEntitlement? Entitlement { get; } = entitlement;

    internal SourceEntitlement EvaluateEntitlement()
    {
        if (Kind is not ("primary_issue" or "referenced_document" or "repository_file" or "caller_statement"))
            throw new SourceNotEntitled("Unrecognized source kind.");
        if (string.IsNullOrWhiteSpace(Locator))
            throw new SourceNotEntitled("A source locator is required.");
        if (Origin is not ("caller" or "broodling_policy"))
            throw new SourceNotEntitled("This origin cannot supply entitled sources, regardless of the payload or claimed grant.");
        if (Entitlement is not null)
        {
            if (Entitlement.GrantedBy is not ("caller" or "broodling_policy")
                || string.IsNullOrWhiteSpace(Entitlement.Basis))
                throw new SourceNotEntitled("An explicit grant requires an entitling authority and a nonempty basis.");
            return Entitlement;
        }
        if (Kind == "primary_issue")
            return new("broodling_policy", "primary_authoritative_work_reference");
        throw new SourceNotEntitled("Referenced material requires an explicit entitlement grant.");
    }
}

public sealed class EntitledSource(string sourceId, string workUnitId, string kind, string locator,
    byte[] content, string contentSha256, string mediaType, string origin, string entitledBy,
    string entitlementBasis, string retrievedAt, string recordedAt)
{
    private readonly byte[] bytes = (byte[])content.Clone();
    public string SourceId { get; } = sourceId;
    public string WorkUnitId { get; } = workUnitId;
    public string Kind { get; } = kind;
    public string Locator { get; } = locator;
    public byte[] Content => (byte[])bytes.Clone();
    public string ContentSha256 { get; } = contentSha256;
    public string MediaType { get; } = mediaType;
    public string Origin { get; } = origin;
    public string EntitledBy { get; } = entitledBy;
    public string EntitlementBasis { get; } = entitlementBasis;
    public string RetrievedAt { get; } = retrievedAt;
    public string RecordedAt { get; } = recordedAt;
}

public sealed record WorkUnit(string WorkUnitId, string ReferenceKey, string Host, string Owner,
    string Repository, long IssueNumber, string IssueLocator, string? RepositoryIdentity,
    string? IssueIdentity, string FirstSeenAt);

public sealed record WorkSubmission(string SubmissionId, string WorkUnitId, string SubmittedRepository,
    string SubmittedIssue, string ReceivedAt);

public sealed record StoreInformation(string Format, int SchemaVersion, string InitializedAt);
