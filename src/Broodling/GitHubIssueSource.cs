using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Broodling;

/// <summary>
/// A refused GitHub read. Retryable failures are operational; the others are
/// deterministic facts about the requested object, such as its absence.
/// </summary>
public sealed class GitHubSourceError(string message, bool retryable = true)
    : BroodlingException("github_source_error", message)
{
    public bool Retryable { get; } = retryable;
}

public sealed record AcquiredIssue(WorkReference Reference, SourceSubmission Source);

/// <summary>Acquire only explicitly selected issues or comments through the authenticated GitHub CLI.</summary>
public sealed class GitHubIssueSource(string executable = "gh")
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public Task<AcquiredIssue> AcquireAsync(WorkReference reference, CancellationToken cancellationToken = default) =>
        AcquireAsync(reference, null, "primary_issue", cancellationToken);

    internal async Task<AcquiredIssue> AcquireAsync(WorkReference reference, GitHubRepositoryCredentials? credentials,
        string kind, CancellationToken cancellationToken)
    {
        var content = await ReadAsync(reference,
            $"/repos/{reference.Owner}/{reference.Repository}/issues/{reference.IssueNumber.ToString(CultureInfo.InvariantCulture)}",
            credentials, cancellationToken);
        return Validate(reference, content, kind);
    }

    /// <summary>Acquire one exact issue comment, without its surrounding discussion.</summary>
    internal async Task<SourceSubmission> AcquireCommentAsync(WorkReference issue, long commentId,
        GitHubRepositoryCredentials? credentials, CancellationToken cancellationToken)
    {
        var id = commentId.ToString(CultureInfo.InvariantCulture);
        var locator = issue.IssueLocator + "#issuecomment-" + id;
        var content = await ReadAsync(issue, $"/repos/{issue.Owner}/{issue.Repository}/issues/comments/{id}",
            credentials, cancellationToken);
        try
        {
            _ = StrictUtf8.GetCharCount(content);
            using var document = JsonDocument.Parse(content);
            var comment = document.RootElement;
            if (comment.ValueKind != JsonValueKind.Object)
                throw new GitHubSourceError("GitHub comment response is not an object.");
            if (!StringEquals(comment, "html_url", locator) || !StringEquals(comment, "issue_url",
                    $"https://api.github.com/repos/{issue.Owner}/{issue.Repository}/issues/{issue.IssueNumber.ToString(CultureInfo.InvariantCulture)}"))
                throw new GitHubSourceError("GitHub comment locator does not match the reference.", retryable: false);
            if (!comment.TryGetProperty("body", out var body) || body.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new GitHubSourceError("GitHub comment response has an invalid body.", retryable: false);
            _ = body.ValueKind == JsonValueKind.String ? body.GetString() : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or DecoderFallbackException)
        {
            throw new GitHubSourceError("GitHub comment response is not valid comment JSON.");
        }
        return new("referenced_document", locator, content, mediaType: "application/json",
            retrievedAt: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), origin: "broodling_policy",
            entitlement: ReferenceEntitlement);
    }

    internal static readonly SourceEntitlement ReferenceEntitlement = new("broodling_policy", "request_bundle_reference");

    private async Task<byte[]> ReadAsync(WorkReference reference, string path,
        GitHubRepositoryCredentials? credentials, CancellationToken cancellationToken)
    {
        if (reference.Host != "github.com")
            throw new GitHubSourceError("GitHub acquisition requires github.com.");
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        if (credentials is not null)
            process.StartInfo.Environment["GH_TOKEN"] = credentials.Token;
        foreach (var argument in new[]
        {
            "api", "--hostname", "github.com", "--method", "GET",
            "--header", "Accept: application/vnd.github+json",
            "--header", "X-GitHub-Api-Version: 2022-11-28",
            path
        })
            process.StartInfo.ArgumentList.Add(argument);
        byte[] content;
        try
        {
            process.Start();
            using var output = new MemoryStream();
            await Task.WhenAll(
                process.StandardOutput.BaseStream.CopyToAsync(output, linked.Token),
                process.StandardError.BaseStream.CopyToAsync(Stream.Null, linked.Token),
                process.WaitForExitAsync(linked.Token));
            if (process.ExitCode != 0)
                throw Unavailable(output.ToArray())
                    ? new GitHubSourceError("The GitHub object is not available.", retryable: false)
                    : new GitHubSourceError("GitHub acquisition failed.");
            content = output.ToArray();
        }
        catch (OperationCanceledException)
        {
            StopRead(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new GitHubSourceError("GitHub acquisition failed.");
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or InvalidOperationException or ArgumentException)
        {
            StopRead(process);
            // CLI errors can contain credentials and private response details. Never retain them as an inner exception.
            throw new GitHubSourceError("GitHub acquisition failed.");
        }
        return content;
    }

    // gh prints GitHub's error document on stdout. Only an explicit absence is
    // a fact about the object; every other failure may be operational.
    private static bool Unavailable(byte[] output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String && status.GetString() is "404" or "410";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void StopRead(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
    }

    private static AcquiredIssue Validate(WorkReference reference, byte[] content, string kind)
    {
        try
        {
            // JsonDocument defers decoding strings we never access. Validate the complete response too.
            _ = StrictUtf8.GetCharCount(content);
            using var document = JsonDocument.Parse(content);
            var issue = document.RootElement;
            if (issue.ValueKind != JsonValueKind.Object)
                throw new GitHubSourceError("GitHub primary issue response is not an object.");
            if (issue.TryGetProperty("pull_request", out _))
                throw new GitHubSourceError("The GitHub reference must be an issue, not a pull request.", retryable: false);
            if (!issue.TryGetProperty("number", out var number) || number.ValueKind != JsonValueKind.Number
                || !number.TryGetInt64(out var parsedNumber) || parsedNumber != reference.IssueNumber)
                throw new GitHubSourceError("GitHub issue number does not match the reference.", retryable: false);
            var repositoryUrl = $"https://api.github.com/repos/{reference.Owner}/{reference.Repository}";
            if (!StringEquals(issue, "html_url", reference.IssueLocator) || !StringEquals(issue, "repository_url", repositoryUrl))
                throw new GitHubSourceError("GitHub issue locator does not match the reference.", retryable: false);
            if (!issue.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(title.GetString()))
                throw new GitHubSourceError("GitHub issue response has no title.", retryable: false);
            if (!issue.TryGetProperty("body", out var body) || body.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new GitHubSourceError("GitHub issue response has an invalid body.", retryable: false);
            _ = body.ValueKind == JsonValueKind.String ? body.GetString() : null;
            if (!issue.TryGetProperty("node_id", out var identity) || identity.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(identity.GetString()))
                throw new GitHubSourceError("GitHub issue response has no stable identity.", retryable: false);
            var nodeId = identity.GetString()!;
            if (reference.IssueIdentity is not null && reference.IssueIdentity != nodeId)
                throw new GitHubSourceError("GitHub issue identity does not match the reference.", retryable: false);
            var acquiredReference = WorkReference.Parse(reference.SubmittedRepository, reference.SubmittedIssue,
                reference.RepositoryIdentity, nodeId);
            return new(acquiredReference, new(kind, reference.IssueLocator, content,
                mediaType: "application/json", retrievedAt: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                origin: "broodling_policy", entitlement: kind == "primary_issue" ? null : ReferenceEntitlement));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or DecoderFallbackException)
        {
            throw new GitHubSourceError("GitHub issue response is not valid issue JSON.");
        }
    }

    private static bool StringEquals(JsonElement element, string property, string expected) =>
        element.TryGetProperty(property, out var actual) && actual.ValueKind == JsonValueKind.String
        && string.Equals(actual.GetString(), expected, StringComparison.OrdinalIgnoreCase);
}
