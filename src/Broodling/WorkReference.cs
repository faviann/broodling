using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Broodling;

/// <summary>A parsed reference. Only canonical components determine identity.</summary>
public sealed class WorkReference
{
    private WorkReference(string host, string owner, string repository, long issueNumber,
        string submittedRepository, string submittedIssue, string? repositoryIdentity, string? issueIdentity)
    {
        Host = host;
        Owner = owner;
        Repository = repository;
        IssueNumber = issueNumber;
        SubmittedRepository = submittedRepository;
        SubmittedIssue = submittedIssue;
        RepositoryIdentity = string.IsNullOrEmpty(repositoryIdentity) ? null : repositoryIdentity;
        IssueIdentity = string.IsNullOrEmpty(issueIdentity) ? null : issueIdentity;
    }

    public string Host { get; }
    public string Owner { get; }
    public string Repository { get; }
    public long IssueNumber { get; }
    public string SubmittedRepository { get; }
    public string SubmittedIssue { get; }
    public string? RepositoryIdentity { get; }
    public string? IssueIdentity { get; }
    public string Key => $"{Host}/{Owner}/{Repository}#{IssueNumber.ToString(CultureInfo.InvariantCulture)}";
    public string WorkUnitId => "wu-" + Digests.Parts("broodling.work-unit.v1", Key);
    public string IssueLocator => $"https://{Host}/{Owner}/{Repository}/issues/{IssueNumber.ToString(CultureInfo.InvariantCulture)}";

    public static WorkReference Parse(string repository, long issue,
        string? repositoryIdentity = null, string? issueIdentity = null) =>
        Parse(repository, issue.ToString(CultureInfo.InvariantCulture), repositoryIdentity, issueIdentity);

    public static WorkReference Parse(string repository, string issue,
        string? repositoryIdentity = null, string? issueIdentity = null)
    {
        var canonical = ParseRepository(repository);
        if (string.IsNullOrWhiteSpace(issue))
            throw new InvalidWorkReference("An issue number or locator is required.");
        var raw = issue.Trim().TrimEnd('/');
        var tail = Regex.Match(raw, @"/(?:issues|-/issues)/([0-9]+)$", RegexOptions.CultureInvariant);
        var digits = raw.StartsWith('#') ? raw[1..] : raw;
        if (tail.Success)
        {
            if (ParseRepository(raw[..tail.Index]) != canonical)
                throw new InvalidWorkReference("The issue locator and repository reference disagree.");
            digits = tail.Groups[1].Value;
        }
        if (!Regex.IsMatch(digits, @"\A[0-9]+\z", RegexOptions.CultureInvariant)
            || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number <= 0)
            throw new InvalidWorkReference("The issue number must be a positive integer supported by SQLite.");
        return new(canonical.Host, canonical.Owner, canonical.Repository, number,
            repository, issue, repositoryIdentity, issueIdentity);
    }

    private static (string Host, string Owner, string Repository) ParseRepository(string submitted)
    {
        if (string.IsNullOrWhiteSpace(submitted))
            throw new InvalidWorkReference("A repository reference is required.");
        var raw = submitted.Trim().TrimEnd('/');
        string host;
        string path;
        var scheme = Regex.Match(raw, @"\A([a-zA-Z][a-zA-Z0-9+.-]*)://");
        if (scheme.Success)
        {
            if (scheme.Groups[1].Value.ToLowerInvariant() is not ("https" or "http" or "ssh" or "git"))
                throw new InvalidWorkReference("Unsupported repository scheme.");
            var remainder = raw[scheme.Length..];
            var slash = remainder.IndexOf('/');
            if (slash < 0)
                throw new InvalidWorkReference("The repository reference has no path.");
            var authority = remainder[..slash];
            host = authority[(authority.LastIndexOf('@') + 1)..];
            path = remainder[(slash + 1)..];
        }
        else
        {
            var scp = Regex.Match(raw, @"\A(?:[^@/]+@)?([^:/@]+):([^:].*)\z");
            if (scp.Success)
            {
                host = scp.Groups[1].Value;
                path = scp.Groups[2].Value;
            }
            else
            {
                var parts = raw.Split('/');
                if (parts.Length == 3 && parts[0].Contains('.'))
                {
                    host = parts[0];
                    path = string.Join('/', parts[1..]);
                }
                else
                {
                    host = "github.com";
                    path = raw;
                }
            }
        }
        host = host.Trim().ToLowerInvariant().TrimEnd('.');
        path = path.Trim('/').ToLowerInvariant();
        if (path.EndsWith(".git", StringComparison.Ordinal))
            path = path[..^4];
        var segments = path.Split('/');
        if (!Regex.IsMatch(host, @"\A[A-Za-z0-9.-]+(?::[0-9]+)?\z")
            || segments.Length != 2
            || segments.Any(segment => !Regex.IsMatch(segment, @"\A[A-Za-z0-9._-]+\z")))
            throw new InvalidWorkReference("Expected a repository host and owner/repository path.");
        return (host, segments[0], segments[1]);
    }
}

internal static class Digests
{
    internal static string Bytes(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string Parts(params string[] parts) => Bytes(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)));
}
