using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Broodling;

/// <summary>
/// A refused repository preparation. Identity, clone-URL and default-branch mismatches and local
/// configuration or repository-state conflicts need attention; failed or changing acquisition is retryable.
/// </summary>
public sealed class GitHubRepositoryError(string message, bool retryable = true)
    : BroodlingException("github_repository_error", message)
{
    public bool Retryable { get; } = retryable;
}

/// <summary>
/// Credentials supplied by the configured Broodling service. The value is only
/// placed in child-process environments and is never part of retained state.
/// </summary>
public sealed class GitHubRepositoryCredentials
{
    public GitHubRepositoryCredentials(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Contains('\0'))
            throw new GitHubRepositoryError("Configured GitHub repository credentials are required.", retryable: false);
        Token = token;
    }

    public string Token { get; }
}

/// <summary>One repository selected by the authenticated GitHub preparation boundary.</summary>
public sealed record AcquiredRepository(string Repository, string DefaultBranch, string StartingRevision,
    string StartingCommit, string RepositoryIdentity)
{
    public string CommitOid => StartingCommit;
    public string TargetBranch => DefaultBranch;
}

/// <summary>
/// Acquire GitHub repository metadata and a durable bare clone. The caller
/// supplies only the Work Unit and service configuration; source revision and
/// target branch are selected here. Each metadata read has a deadline and a fetch
/// fails once its transfer stalls, so a hung acquisition ends as a retryable error.
/// Acquisitions into the same local repository run one at a time in this process.
/// </summary>
public sealed class GitHubRepositorySource(string executable = "gh", string gitExecutable = "git")
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const string UpstreamFetchRefspec = "+refs/heads/*:refs/broodling/upstream/*";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Repositories = new();

    /// <summary>The bound on one metadata read, as for issue reads; tests shorten it.</summary>
    internal TimeSpan MetadataDeadline { get; init; } = TimeSpan.FromSeconds(30);

    public async Task<AcquiredRepository> AcquireAsync(WorkReference reference,
        GitHubRepositoryCredentials credentials, string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(credentials);
        var root = RepositoryRoot(repositoryRoot);
        if (reference.Host != "github.com")
            throw new GitHubRepositoryError("Repository preparation requires github.com.", retryable: false);
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = await ReadMetadataAsync(reference, credentials, cancellationToken);
        var destination = Path.Combine(root, reference.Owner, reference.Repository + ".git");
        // Concurrent fetches into one repository can fail on its ref locks.
        var exclusive = Repositories.GetOrAdd(destination, _ => new SemaphoreSlim(1, 1));
        await exclusive.WaitAsync(cancellationToken);
        try
        {
            await CloneOrFetchAsync(metadata.CloneUrl, destination, credentials, cancellationToken);

            var verified = await ReadMetadataAsync(reference, credentials, cancellationToken);
            if (verified != metadata)
                throw new GitHubRepositoryError("GitHub repository identity or default branch changed during acquisition.");

            var startingRevision = "refs/heads/" + metadata.DefaultBranch;
            var state = GitCustody.ResolvePinned(destination,
                "refs/broodling/upstream/" + metadata.DefaultBranch);
            GitCustody.Retain(state);
            return new(state.Repository, metadata.DefaultBranch, startingRevision, state.CommitOid, metadata.RepositoryIdentity);
        }
        finally
        {
            exclusive.Release();
        }
    }

    public Task<AcquiredRepository> AcquireAsync(WorkReference reference, string token,
        string repositoryRoot, CancellationToken cancellationToken = default) =>
        AcquireAsync(reference, new GitHubRepositoryCredentials(token), repositoryRoot, cancellationToken);

    private async Task<RepositoryMetadata> ReadMetadataAsync(WorkReference reference,
        GitHubRepositoryCredentials credentials, CancellationToken cancellationToken)
    {
        var response = await RunForgeAsync([
            "api", "--hostname", "github.com", "--method", "GET",
            "--header", "Accept: application/vnd.github+json",
            "--header", "X-GitHub-Api-Version: 2022-11-28",
            $"/repos/{reference.Owner}/{reference.Repository}"
        ], credentials, cancellationToken);
        try
        {
            _ = StrictUtf8.GetCharCount(response);
            using var document = JsonDocument.Parse(response);
            var repository = document.RootElement;
            if (repository.ValueKind != JsonValueKind.Object
                || !StringEquals(repository, "full_name", reference.Owner + "/" + reference.Repository)
                || !StringEquals(repository, "html_url", $"https://github.com/{reference.Owner}/{reference.Repository}"))
                throw new GitHubRepositoryError("GitHub repository identity does not match the Work Unit.", retryable: false);
            if (!StringValue(repository, "node_id", out var repositoryIdentity)
                || reference.RepositoryIdentity is not null && reference.RepositoryIdentity != repositoryIdentity)
                throw new GitHubRepositoryError("GitHub repository identity does not match the Work Unit pin.", retryable: false);
            if (!StringValue(repository, "default_branch", out var branch) || !ValidBranchName(branch))
                throw new GitHubRepositoryError("GitHub repository has no supported default branch.", retryable: false);
            if (!StringValue(repository, "clone_url", out var cloneUrl)
                || !CanonicalCloneUrl(cloneUrl, reference))
                throw new GitHubRepositoryError("GitHub repository has no supported authenticated clone URL.", retryable: false);
            return new(branch, $"https://github.com/{reference.Owner}/{reference.Repository}.git", repositoryIdentity);
        }
        catch (GitHubRepositoryError)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or FormatException or DecoderFallbackException)
        {
            throw new GitHubRepositoryError("GitHub repository response is not valid repository JSON.");
        }
    }

    private async Task CloneOrFetchAsync(string cloneUrl, string destination,
        GitHubRepositoryCredentials credentials, CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(destination))
                throw new GitHubRepositoryError("The configured repository path is not a directory.", retryable: false);
            if (Directory.Exists(destination))
            {
                await RefreshExistingAsync(cloneUrl, destination, credentials, cancellationToken);
                return;
            }

            var parent = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(parent);
            var staging = Path.Combine(parent,
                "." + Path.GetFileName(destination) + ".initializing-" + Guid.NewGuid().ToString("N"));
            try
            {
                var initialized = await RunGitAsync(["init", "--bare", staging], null,
                    credentials, allowCredentials: false, cancellationToken);
                if (initialized.ExitCode != 0)
                    throw new GitHubRepositoryError("GitHub repository acquisition failed.");
                var addedOrigin = await RunGitAsync(["-C", staging, "remote", "add", "origin", cloneUrl], null,
                    credentials, allowCredentials: false, cancellationToken);
                if (addedOrigin.ExitCode != 0)
                    throw new GitHubRepositoryError("GitHub repository acquisition failed.");

                try
                {
                    Directory.Move(staging, destination);
                }
                catch (IOException) when (Directory.Exists(destination))
                {
                    // Another preparation published an owned repository first.
                }
            }
            finally
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }

            // The final path is either ours, with its canonical origin already
            // installed, or a concurrent publisher's path. Validate it before
            // the first credential-bearing operation in either case.
            await RefreshExistingAsync(cloneUrl, destination, credentials, cancellationToken);
        }
        catch (GitHubRepositoryError)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            // The repository root is not writable: local configuration.
            throw new GitHubRepositoryError("GitHub repository acquisition failed.", retryable: false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            throw new GitHubRepositoryError("GitHub repository acquisition failed.");
        }
    }

    private async Task RefreshExistingAsync(string cloneUrl, string destination,
        GitHubRepositoryCredentials credentials, CancellationToken cancellationToken)
    {
        var bare = await RunGitAsync(["-C", destination, "rev-parse", "--is-bare-repository"], null,
            credentials, allowCredentials: false, cancellationToken);
        if (bare.ExitCode != 0 || Encoding.UTF8.GetString(bare.Output).Trim() != "true")
            throw new GitHubRepositoryError("The configured repository path is not an owned bare Git repository.", retryable: false);
        var origin = await RunGitAsync(["-C", destination, "remote", "get-url", "origin"], null,
            credentials, allowCredentials: false, cancellationToken);
        if (origin.ExitCode != 0 || Encoding.UTF8.GetString(origin.Output).Trim() != cloneUrl)
            throw new GitHubRepositoryError("The configured repository origin does not match the Work Unit.", retryable: false);
        await RefreshAsync(destination, credentials, cancellationToken);
    }

    private async Task RefreshAsync(string destination, GitHubRepositoryCredentials credentials,
        CancellationToken cancellationToken)
    {
        var fetched = await RunGitAsync(["-C", destination, "fetch", "--prune", "origin", UpstreamFetchRefspec],
            null, credentials, allowCredentials: true, cancellationToken);
        if (fetched.ExitCode != 0)
            throw new GitHubRepositoryError("GitHub repository refresh failed.");
    }

    private async Task<byte[]> RunForgeAsync(IReadOnlyList<string> arguments,
        GitHubRepositoryCredentials credentials, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(executable, arguments, null, credentials, allowCredentials: true,
            cancellationToken, MetadataDeadline);
        if (result.ExitCode != 0)
            throw new GitHubRepositoryError("GitHub repository metadata acquisition failed.");
        return result.Output;
    }

    private async Task<ProcessResult> RunGitAsync(IReadOnlyList<string> arguments, string? workingDirectory,
        GitHubRepositoryCredentials credentials, bool allowCredentials, CancellationToken cancellationToken) =>
        await RunProcessAsync(gitExecutable, arguments, workingDirectory, credentials, allowCredentials, cancellationToken);

    private static async Task<ProcessResult> RunProcessAsync(string executable,
        IReadOnlyList<string> arguments, string? workingDirectory, GitHubRepositoryCredentials credentials,
        bool allowCredentials,
        CancellationToken cancellationToken, TimeSpan? deadline = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var key in process.StartInfo.Environment.Keys.Where(key =>
            key.StartsWith("GIT_", StringComparison.Ordinal)
                || key is "GH_TOKEN" or "GITHUB_TOKEN").ToArray())
            process.StartInfo.Environment.Remove(key);
        if (allowCredentials)
            process.StartInfo.Environment["GH_TOKEN"] = credentials.Token;
        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        process.StartInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        process.StartInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";
        process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        if (allowCredentials)
        {
            process.StartInfo.Environment["GIT_CONFIG_COUNT"] = "3";
            process.StartInfo.Environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + credentials.Token));
            process.StartInfo.Environment["GIT_CONFIG_VALUE_0"] = "Authorization: Basic " + basic;
            // The only credentialed Git command is the fetch. Rather than cap a large transfer,
            // fail it once no data arrives for two minutes; the server's keepalives count as data.
            process.StartInfo.Environment["GIT_CONFIG_KEY_1"] = "http.lowSpeedLimit";
            process.StartInfo.Environment["GIT_CONFIG_VALUE_1"] = "1";
            process.StartInfo.Environment["GIT_CONFIG_KEY_2"] = "http.lowSpeedTime";
            process.StartInfo.Environment["GIT_CONFIG_VALUE_2"] = "120";
        }
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        using var outputStream = new MemoryStream();
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (deadline is { } bound)
            expiry.CancelAfter(bound);
        Task output = Task.CompletedTask;
        Task<string> error = Task.FromResult("");
        try
        {
            process.Start();
            output = process.StandardOutput.BaseStream.CopyToAsync(outputStream);
            error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(expiry.Token);
            await output;
            return new(process.ExitCode, outputStream.ToArray(), await error);
        }
        catch (OperationCanceledException)
        {
            await StopAndReapAsync(process, output, error);
            cancellationToken.ThrowIfCancellationRequested();
            if (expiry.IsCancellationRequested)
                throw new GitHubRepositoryError("GitHub repository metadata acquisition did not finish in time.");
            throw;
        }
        catch (Exception exception) when (exception is IOException or Win32Exception
            or InvalidOperationException or ArgumentException)
        {
            await StopAndReapAsync(process, output, error);
            // A process that cannot be started is local configuration, such as a missing git or gh.
            throw new GitHubRepositoryError("GitHub repository acquisition failed.",
                retryable: exception is not Win32Exception);
        }
    }

    private static async Task StopAndReapAsync(Process process, Task output, Task<string> error)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }

        try { await process.WaitForExitAsync(CancellationToken.None); }
        catch (InvalidOperationException) { }
        try { await output; } catch (Exception) { }
        try { await error; } catch (Exception) { }
    }

    private static string RepositoryRoot(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new GitHubRepositoryError("The configured repository root must be absolute.", retryable: false);
        string resolved;
        try { resolved = PhysicalPaths.Resolve(path); }
        catch (IOException error) { throw new GitHubRepositoryError(error.Message, retryable: false); }
        if (PhysicalPaths.IsWithinTemporaryRoot(resolved) || PhysicalPaths.IsWithinDisposable(resolved))
            throw new GitHubRepositoryError("The configured repository root must be durable and owned by the service.", retryable: false);
        return resolved;
    }

    private static bool CanonicalCloneUrl(string value, WorkReference reference)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;
        return string.Equals(uri.AbsolutePath.TrimEnd('/'),
            "/" + reference.Owner + "/" + reference.Repository + ".git", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ValidBranchName(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains('\0') && !value.StartsWith('-')
        && !value.Contains("..", StringComparison.Ordinal) && !value.Contains("@{", StringComparison.Ordinal)
        && !value.EndsWith('/') && !value.EndsWith('.') && !value.Contains(' ')
        && value.All(character => !char.IsControl(character));

    private static bool StringValue(JsonElement element, string property, out string value)
    {
        if (element.TryGetProperty(property, out var actual) && actual.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(actual.GetString()))
        {
            value = actual.GetString()!;
            return true;
        }
        value = "";
        return false;
    }

    private static bool StringEquals(JsonElement element, string property, string expected) =>
        StringValue(element, property, out var actual)
        && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private sealed record RepositoryMetadata(string DefaultBranch, string CloneUrl, string RepositoryIdentity);
    private sealed record ProcessResult(int ExitCode, byte[] Output, string Error);
}
