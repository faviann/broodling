using System.Diagnostics;

namespace Broodling.Tests;

internal sealed class AttemptFixture : IDisposable
{
    internal StoreFixture State { get; } = new();
    internal string Repository => System.IO.Path.Combine(State.Root, "source");
    internal string Workspaces => System.IO.Path.Combine(State.Root, "attempts");
    internal string GitDirectory => System.IO.Path.Combine(Repository, ".git");
    internal string Head { get; }
    internal string RevisionId { get; }

    internal AttemptFixture()
    {
        Directory.CreateDirectory(Repository);
        Git("init", "--initial-branch=main");
        Git("config", "user.email", "test@example.invalid");
        Git("config", "user.name", "Broodling test");
        Git("config", "gc.auto", "0");
        File.WriteAllText(System.IO.Path.Combine(Repository, "original.txt"), "original selected bytes\n");
        Git("add", ".");
        Git("commit", "-m", "original");
        Head = Git("rev-parse", "HEAD").Trim();
        using var store = State.Initialize();
        RevisionId = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()], ContractIngressTests.Propose, []).Revision.ContractRevisionId;
    }

    internal AttemptRecord Admit(BroodlingStore store, string? revision = null, string? root = null) =>
        store.AdmitAttempt(RevisionId, Repository, root ?? Workspaces, revision ?? Head);

    internal string Commit(string text)
    {
        File.WriteAllText(System.IO.Path.Combine(Repository, "original.txt"), text);
        Git("add", ".");
        Git("commit", "-m", "changed");
        return Git("rev-parse", "HEAD").Trim();
    }

    internal string Origin => System.IO.Path.Combine(State.Root, "origin.git");
    private string Delivery => System.IO.Path.Combine(State.Root, "delivery");

    /// <summary>Commit in a separate clone, as the native target does, so the result is absent from the source repository.</summary>
    internal string Deliver(string content = "delivered\n", bool push = true)
    {
        if (!Directory.Exists(Delivery))
        {
            RunGit(State.Root, "init", "--bare", Origin);
            Git("config", "url." + Origin + ".insteadOf", "https://github.com/acme/widget.git");
            RunGit(State.Root, "clone", Repository, Delivery);
            RunGit(Delivery, "config", "user.email", "test@example.invalid");
            RunGit(Delivery, "config", "user.name", "Broodling test");
        }
        File.WriteAllText(System.IO.Path.Combine(Delivery, "delivered.txt"), content);
        RunGit(Delivery, "add", ".");
        RunGit(Delivery, "commit", "-m", "delivered");
        if (push) Push();
        return RunGit(Delivery, "rev-parse", "HEAD").Trim();
    }

    internal void Push() => RunGit(Delivery, "push", Origin, "HEAD:refs/heads/delivery");

    internal void DeleteObject(string oid) => File.Delete(System.IO.Path.Combine(GitDirectory, "objects", oid[..2], oid[2..]));

    internal string Git(params string[] arguments) => RunGit(Repository, arguments);
    internal static string RunGit(string repository, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.Ordinal)).ToArray())
            start.Environment.Remove(key);
        // Fixture setup must never inherit an operator's hooks or conversion settings.
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        foreach (var argument in new[] { "-C", repository, "-c", "core.hooksPath=/dev/null" }.Concat(arguments))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult();
    }

    public void Dispose() => State.Dispose();
}
