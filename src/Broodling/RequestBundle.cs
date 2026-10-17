namespace Broodling;

/// <summary>Opaque acquisition inputs and policy retained with one accepted Issue submission.</summary>
public sealed class RequestBundlePlan(byte[] acquisitionInputs, byte[] acquisitionPolicy)
{
    private readonly byte[] inputs = (byte[])acquisitionInputs.Clone();
    private readonly byte[] policy = (byte[])acquisitionPolicy.Clone();

    public byte[] AcquisitionInputs => (byte[])inputs.Clone();
    public byte[] AcquisitionPolicy => (byte[])policy.Clone();
}

public sealed class RequestBundleReference
{
    private readonly byte[] selector;

    internal RequestBundleReference(string bundleId, string referenceId, int ordinal,
        byte[] selector, string? sourceId, string? contentSha256)
    {
        BundleId = bundleId;
        ReferenceId = referenceId;
        Ordinal = ordinal;
        this.selector = (byte[])selector.Clone();
        SourceId = sourceId;
        ContentSha256 = contentSha256;
    }

    public string BundleId { get; }
    public string ReferenceId { get; }
    public int Ordinal { get; }
    public byte[] Selector => (byte[])selector.Clone();
    public bool IsCaptured => ContentSha256 is not null;
    public string? SourceId { get; }
    public string? ContentSha256 { get; }
}

public sealed class RequestBundle
{
    private readonly byte[] inputs;
    private readonly byte[] policy;

    internal RequestBundle(string bundleId, string submissionId, string state,
        byte[] acquisitionInputs, byte[] acquisitionPolicy,
        string? manifestJson, string? manifestSha256, string createdAt, string? completedAt,
        RepositoryPreparation? repository, IReadOnlyList<RequestBundleReference> references,
        IReadOnlyList<RequestCaptureFinding> findings)
    {
        BundleId = bundleId;
        SubmissionId = submissionId;
        State = state;
        inputs = (byte[])acquisitionInputs.Clone();
        policy = (byte[])acquisitionPolicy.Clone();
        ManifestJson = manifestJson;
        ManifestSha256 = manifestSha256;
        CreatedAt = createdAt;
        CompletedAt = completedAt;
        Repository = repository;
        References = Array.AsReadOnly(references.ToArray());
        Findings = Array.AsReadOnly(findings.ToArray());
    }

    public string BundleId { get; }
    public string SubmissionId { get; }
    public string State { get; }
    public byte[] AcquisitionInputs => (byte[])inputs.Clone();
    public byte[] AcquisitionPolicy => (byte[])policy.Clone();
    public string? ManifestJson { get; }
    public string? ManifestSha256 { get; }
    public string CreatedAt { get; }
    public string? CompletedAt { get; }
    public RepositoryPreparation? Repository { get; }
    public RepositoryPreparation? RepositoryPreparation => Repository;
    public IReadOnlyList<RequestBundleReference> References { get; }

    /// <summary>Retained deterministic capture findings; nonempty only for a refused bundle.</summary>
    public IReadOnlyList<RequestCaptureFinding> Findings { get; }
}

/// <summary>One retained reason a capture was refused before admission.</summary>
public sealed record RequestCaptureFinding(string Code, string Subject, string Detail);

public sealed class RequestBundleReferenceContent(string bundleId, string referenceId,
    string contentSha256, byte[] content)
{
    private readonly byte[] bytes = (byte[])content.Clone();
    public string BundleId { get; } = bundleId;
    public string ReferenceId { get; } = referenceId;
    public string ContentSha256 { get; } = contentSha256;
    public byte[] Content => (byte[])bytes.Clone();
}
