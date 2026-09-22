using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Broodling;

public sealed class GitHubSourceError(string message) : BroodlingException("github_source_error", message);

public sealed record AcquiredIssue(WorkReference Reference, SourceSubmission Source);

/// <summary>Acquire only the named issue through the operator's authenticated GitHub CLI.</summary>
public sealed class GitHubIssueSource(string executable = "gh")
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<AcquiredIssue> AcquireAsync(WorkReference reference, CancellationToken cancellationToken = default)
    {
        if (reference.Host != "github.com")
            throw new GitHubSourceError("Primary issue acquisition requires github.com.");
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
        foreach (var argument in new[]
        {
            "api", "--hostname", "github.com", "--method", "GET",
            "--header", "Accept: application/vnd.github+json",
            "--header", "X-GitHub-Api-Version: 2022-11-28",
            $"/repos/{reference.Owner}/{reference.Repository}/issues/{reference.IssueNumber.ToString(CultureInfo.InvariantCulture)}"
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
                throw new GitHubSourceError("GitHub primary issue acquisition failed.");
            content = output.ToArray();
        }
        catch (OperationCanceledException)
        {
            StopRead(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new GitHubSourceError("GitHub primary issue acquisition failed.");
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or InvalidOperationException or ArgumentException)
        {
            StopRead(process);
            // CLI errors can contain credentials and private response details. Never retain them as an inner exception.
            throw new GitHubSourceError("GitHub primary issue acquisition failed.");
        }
        return Validate(reference, content);
    }

    private static void StopRead(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
    }

    private static AcquiredIssue Validate(WorkReference reference, byte[] content)
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
                throw new GitHubSourceError("The primary work reference must be an issue, not a pull request.");
            if (!issue.TryGetProperty("number", out var number) || number.ValueKind != JsonValueKind.Number
                || !number.TryGetInt64(out var parsedNumber) || parsedNumber != reference.IssueNumber)
                throw new GitHubSourceError("GitHub primary issue number does not match the reference.");
            var repositoryUrl = $"https://api.github.com/repos/{reference.Owner}/{reference.Repository}";
            if (!StringEquals(issue, "html_url", reference.IssueLocator) || !StringEquals(issue, "repository_url", repositoryUrl))
                throw new GitHubSourceError("GitHub primary issue locator does not match the reference.");
            if (!issue.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(title.GetString()))
                throw new GitHubSourceError("GitHub primary issue response has no title.");
            if (!issue.TryGetProperty("body", out var body) || body.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new GitHubSourceError("GitHub primary issue response has an invalid body.");
            if (!issue.TryGetProperty("node_id", out var identity) || identity.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(identity.GetString()))
                throw new GitHubSourceError("GitHub primary issue response has no stable identity.");
            var nodeId = identity.GetString()!;
            if (reference.IssueIdentity is not null && reference.IssueIdentity != nodeId)
                throw new GitHubSourceError("GitHub primary issue identity does not match the reference.");
            var acquiredReference = WorkReference.Parse(reference.SubmittedRepository, reference.SubmittedIssue,
                reference.RepositoryIdentity, nodeId);
            return new(acquiredReference, new("primary_issue", reference.IssueLocator, content,
                mediaType: "application/json", retrievedAt: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                origin: "broodling_policy"));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or DecoderFallbackException)
        {
            throw new GitHubSourceError("GitHub primary issue response is not valid issue JSON.");
        }
    }

    private static bool StringEquals(JsonElement element, string property, string expected) =>
        element.TryGetProperty(property, out var actual) && actual.ValueKind == JsonValueKind.String
        && string.Equals(actual.GetString(), expected, StringComparison.OrdinalIgnoreCase);
}
