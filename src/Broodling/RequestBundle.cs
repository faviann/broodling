using System.Text;

namespace Broodling;

/// <summary>
/// Opaque acquisition inputs and limits retained with one accepted Issue
/// submission. Reference selections are registered as capture discovers them.
/// </summary>
public sealed class RequestBundlePlan(byte[] acquisitionInputs, byte[] acquisitionPolicy, byte[] acquisitionLimits)
{
    private readonly byte[] inputs = (byte[])acquisitionInputs.Clone();
    private readonly byte[] policy = (byte[])acquisitionPolicy.Clone();
    private readonly byte[] limits = (byte[])acquisitionLimits.Clone();

    public byte[] AcquisitionInputs => (byte[])inputs.Clone();
    public byte[] AcquisitionPolicy => (byte[])policy.Clone();
    public byte[] AcquisitionLimits => (byte[])limits.Clone();
}

/// <summary>One acquired selection registered before its first capture.</summary>
public sealed class RequestBundleReferenceInput
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] selector;

    private RequestBundleReferenceInput(string referenceId, string captureKind, byte[] selector,
        string? gitRepository, string? gitRevision, string? gitPath)
    {
        ValidateText(referenceId, "Reference identity");
        if (string.IsNullOrWhiteSpace(referenceId))
            throw new RequestBundleConflict("A RequestBundle reference identity is required.");
        ReferenceId = referenceId;
        CaptureKind = captureKind;
        this.selector = (byte[])selector.Clone();
        GitRepository = gitRepository;
        GitRevision = gitRevision;
        GitPath = gitPath;
    }

    public string ReferenceId { get; }
    public string CaptureKind { get; }
    public byte[] Selector => (byte[])selector.Clone();
    public string? GitRepository { get; }
    public string? GitRevision { get; }
    public string? GitPath { get; }

    public static RequestBundleReferenceInput Source(string referenceId, byte[] selector) =>
        new(referenceId, "source", selector ?? throw new ArgumentNullException(nameof(selector)), null, null, null);

    /// <summary>
    /// Register a file selected from a local commit. The revision is resolved
    /// when the Git object is first durably captured, without remote traversal.
    /// </summary>
    public static RequestBundleReferenceInput GitBlob(string referenceId, byte[] selector,
        string repository, string revision, string path)
    {
        ValidateGitText(repository, "Git repository");
        ValidateGitText(revision, "Git revision");
        ValidateGitText(path, "Git path");
        return new(referenceId, "git_blob", selector ?? throw new ArgumentNullException(nameof(selector)),
            repository, revision, path);
    }

    // Registration is immutable and capture passes these to Git as arguments,
    // which cannot carry NUL. Existence and resolution remain capture concerns.
    private static void ValidateGitText(string? value, string name)
    {
        ValidateText(value, name);
        if (string.IsNullOrEmpty(value) || value.Contains('\0'))
            throw new RequestBundleConflict($"{name} must be nonempty text without NUL.");
    }

    internal static void ValidateText(string? value, string name)
    {
        try
        {
            if (value is not null) StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            throw new RequestBundleConflict($"{name} contains invalid Unicode.");
        }
    }
}

public sealed class RequestBundleReference
{
    private readonly byte[] selector;

    internal RequestBundleReference(string bundleId, string referenceId, int ordinal, string captureKind,
        byte[] selector, string? sourceId, string? contentSha256, string? gitRepository,
        string? gitCommitOid, string? gitPath, string? gitBlobOid)
    {
        BundleId = bundleId;
        ReferenceId = referenceId;
        Ordinal = ordinal;
        CaptureKind = captureKind;
        this.selector = (byte[])selector.Clone();
        SourceId = sourceId;
        ContentSha256 = contentSha256;
        GitRepository = gitRepository;
        GitCommitOid = gitCommitOid;
        GitPath = gitPath;
        GitBlobOid = gitBlobOid;
    }

    public string BundleId { get; }
    public string ReferenceId { get; }
    public int Ordinal { get; }
    public string CaptureKind { get; }
    public byte[] Selector => (byte[])selector.Clone();
    public bool IsCaptured => ContentSha256 is not null;
    public string? SourceId { get; }
    public string? ContentSha256 { get; }
    public string? GitCommitOid { get; }
    public string? GitPath { get; }
    public string? GitBlobOid { get; }
    internal string? GitRepository { get; }
}

public sealed class RequestBundle
{
    private readonly byte[] inputs;
    private readonly byte[] policy;
    private readonly byte[] limits;

    internal RequestBundle(string bundleId, string submissionId, string state,
        byte[] acquisitionInputs, byte[] acquisitionPolicy, byte[] acquisitionLimits,
        string? manifestJson, string? manifestSha256, string createdAt, string? completedAt,
        RepositoryPreparation? repository, IReadOnlyList<RequestBundleReference> references,
        IReadOnlyList<RequestCaptureFinding> findings)
    {
        BundleId = bundleId;
        SubmissionId = submissionId;
        State = state;
        inputs = (byte[])acquisitionInputs.Clone();
        policy = (byte[])acquisitionPolicy.Clone();
        limits = (byte[])acquisitionLimits.Clone();
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
    public byte[] AcquisitionLimits => (byte[])limits.Clone();
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
    string contentSha256, byte[] content, string? gitCommitOid, string? gitPath)
{
    private readonly byte[] bytes = (byte[])content.Clone();
    public string BundleId { get; } = bundleId;
    public string ReferenceId { get; } = referenceId;
    public string ContentSha256 { get; } = contentSha256;
    public byte[] Content => (byte[])bytes.Clone();
    public string? GitCommitOid { get; } = gitCommitOid;
    public string? GitPath { get; } = gitPath;
}
