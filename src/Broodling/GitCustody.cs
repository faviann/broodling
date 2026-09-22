using System.Diagnostics;
using System.Text;

namespace Broodling;

internal sealed record StartingState(string Repository, string CommitOid, string RequestedRevision);

/// <summary>Local administrative Git custody. No checkout, driver execution, fetch or delivery.</summary>
internal static class GitCustody
{
    internal static StartingState Resolve(string repository, string revision)
    {
        if (string.IsNullOrWhiteSpace(repository) || !Directory.Exists(repository))
            throw new UnsupportedStartingState("B1 requires a local Git repository.");
        if (string.IsNullOrWhiteSpace(revision))
            throw new UnsupportedStartingState("B1 requires a commit revision.");
        var commit = Text(repository, "rev-parse", "--verify", "--end-of-options", revision + "^{commit}").Trim();
        if (commit.Length != 40 || commit.Any(c => !char.IsAsciiHexDigitLower(c)))
            throw new UnsupportedStartingState("B1 requires a full SHA-1 commit identity.");
        AssertSupportedCheckout(repository, commit);
        if (Text(repository, "rev-parse", "--is-bare-repository").Trim() != "true")
        {
            var entries = Text(repository, "status", "--porcelain=v1", "--untracked-files=all", "--no-renames")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (entries.Length != 0)
                throw new UnsupportedStartingState("Uncommitted or untracked starting material cannot be represented by B1:\n"
                    + string.Join('\n', entries.Take(20)) + (entries.Length > 20 ? $"\n... and {entries.Length - 20} more" : "")
                    + "\nCommit or remove this material before admission.");
        }
        var common = Text(repository, "rev-parse", "--path-format=absolute", "--git-common-dir").Trim();
        return new(PhysicalPaths.Resolve(common), commit, revision);
    }

    internal static string WorkspaceRoot(string root, string repository, StartingState state)
    {
        if (!System.IO.Path.IsPathFullyQualified(root))
            throw new UnsupportedWorkspaceRoot("The workspace root must be absolute.");
        string resolved;
        try { resolved = PhysicalPaths.Resolve(root); }
        catch (IOException error) { throw new UnsupportedWorkspaceRoot(error.Message); }
        if (PhysicalPaths.IsWithinTemporaryRoot(resolved))
            throw new UnsupportedWorkspaceRoot("The workspace root must be outside temporary or volatile roots.");
        if (PhysicalPaths.IsWithinDisposable(resolved))
            throw new UnsupportedWorkspaceRoot("The workspace root cannot be inside a disposable Attempt enclosure.");
        var source = PhysicalPaths.Resolve(repository);
        // A caller may name a subdirectory: exclude the whole source checkout too.
        var top = Text(repository, "rev-parse", "--is-bare-repository").Trim() == "true"
            ? source : PhysicalPaths.Resolve(Text(repository, "rev-parse", "--show-toplevel").Trim());
        if (new[] { source, top, state.Repository }.Any(path => PhysicalPaths.Contains(path, resolved)))
            throw new UnsupportedWorkspaceRoot("The workspace root must be outside the source repository and common Git directory.");
        return resolved;
    }

    internal static void Retain(StartingState state, AdministrativeGitProcess.EnclosureLock? enclosureLock = null)
    {
        var repository = state.Repository;
        var oid = state.CommitOid;
        var reference = "refs/broodling/starting/" + oid;
        if (Text(repository, "cat-file", "-t", oid).Trim() != "commit")
            throw new UnsupportedStartingState("The selected B1 object is not a commit.");
        // --no-walk restricts validation to this snapshot, never every ancestor.
        Text(repository, "rev-list", "--objects", "--no-walk", "--missing=error", oid);
        var existing = RetentionOid(repository, reference);
        if (existing == oid) return;
        if (existing is not null)
            throw new UnsupportedStartingState("The B1 retention pin conflicts with the selected commit.");
        var update = Run(repository, ["update-ref", "--no-deref", reference, oid, new string('0', 40)], enclosureLock: enclosureLock);
        // Concurrent creation is acceptable only if it left precisely the same direct pin.
        if (update.ExitCode != 0 && RetentionOid(repository, reference) != oid)
            throw Failure(update);
        if (RetentionOid(repository, reference) != oid)
            throw new UnsupportedStartingState("The B1 retention pin is not the selected direct commit.");
    }

    private static string? RetentionOid(string repository, string reference)
    {
        var symbolic = Run(repository, ["symbolic-ref", "--quiet", reference]);
        if (symbolic.ExitCode == 0)
            throw new UnsupportedStartingState("The B1 retention pin is symbolic; a direct pin is required.");
        if (symbolic.ExitCode != 1) throw Failure(symbolic);
        var exists = Run(repository, ["show-ref", "--verify", "--quiet", reference]);
        if (exists.ExitCode == 1) return null;
        if (exists.ExitCode != 0) throw Failure(exists);
        return Text(repository, "show-ref", "--verify", "--hash", reference).Trim();
    }

    // This pre-status check is also needed by C at checkout/first dispatch.
    internal static void AssertSupportedCheckout(string repository, string commit)
    {
        var configuration = Text(repository, "config", "--null", "--list");
        foreach (var entry in configuration.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('\n');
            var key = (separator < 0 ? entry : entry[..separator]).ToLowerInvariant();
            var value = (separator < 0 ? "" : entry[(separator + 1)..]).ToLowerInvariant();
            var external = key.StartsWith("filter.", StringComparison.Ordinal)
                && key[(key.LastIndexOf('.') + 1)..] is "clean" or "smudge" or "process";
            var unsupported = (key is "core.autocrlf" or "core.sparsecheckout" or "core.fsmonitor")
                    && value is not ("false" or "no" or "off" or "0")
                || key == "core.symlinks" && value is not ("true" or "yes" or "on" or "1" or "")
                || key == "core.eol" && value is not ("lf" or "native");
            if (external || unsupported || key.StartsWith("includeif.", StringComparison.Ordinal) && key.EndsWith(".path", StringComparison.Ordinal))
                throw new UnsupportedStartingState($"Unsupported checkout transformation: configuration {key}.");
        }
        var paths = Checked(repository, ["ls-tree", "-rz", "--name-only", commit]);
        var gitDirectory = Text(repository, "rev-parse", "--absolute-git-dir").Trim();
        var attributes = Checked(repository, [$"--git-dir={gitDirectory}", $"--work-tree={PhysicalPaths.Resolve(repository)}",
            "check-attr", $"--source={commit}", "--stdin", "-z", "filter", "text", "eol", "ident", "working-tree-encoding", "crlf"], paths);
        var fields = Encoding.UTF8.GetString(attributes).Split('\0');
        if (fields[^1] != "" || (fields.Length - 1) % 3 != 0)
            throw new UnsupportedStartingState("Cannot establish effective checkout attributes.");
        for (var offset = 0; offset < fields.Length - 1; offset += 3)
            if (fields[offset + 2] is not ("unspecified" or "unset"))
                throw new UnsupportedStartingState($"Unsupported checkout transformation: attribute {fields[offset + 1]}={fields[offset + 2]} on {fields[offset]}.");
    }

    internal sealed record Result(int ExitCode, byte[] Output, string Error);
    private static UnsupportedStartingState Failure(Result result) => new($"Cannot establish local Git custody: {result.Error.Trim()}");
    internal static string Text(string repository, params string[] arguments) => Encoding.UTF8.GetString(Checked(repository, arguments));
    private static byte[] Checked(string repository, string[] arguments, byte[]? input = null)
    {
        var result = Run(repository, arguments, input);
        if (result.ExitCode != 0) throw Failure(result);
        return result.Output;
    }

    internal static Result Run(string repository, string[] arguments, byte[]? input = null,
        AdministrativeGitProcess.EnclosureLock? enclosureLock = null)
    {
        var start = StartInfo(repository, arguments);
        if (enclosureLock is not null)
        {
            if (input is not null) throw new ArgumentException("Protected administrative commands take no stdin.");
            return AdministrativeGitProcess.Run(start, enclosureLock);
        }
        using var process = Process.Start(start) ?? throw new UnsupportedStartingState("Git could not be started.");
        using var output = new MemoryStream();
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(output);
        var stderr = process.StandardError.ReadToEndAsync();
        if (input is not null) process.StandardInput.BaseStream.Write(input);
        process.StandardInput.Close();
        process.WaitForExit();
        stdout.GetAwaiter().GetResult();
        return new(process.ExitCode, output.ToArray(), stderr.GetAwaiter().GetResult());
    }

    private static ProcessStartInfo StartInfo(string repository, string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.Ordinal)).ToArray())
            start.Environment.Remove(key);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        start.Environment["LC_ALL"] = "C";
        foreach (var argument in new[] { "--no-replace-objects", "-C", repository, "-c", "core.hooksPath=/dev/null" }.Concat(arguments))
            start.ArgumentList.Add(argument);
        return start;
    }
}
