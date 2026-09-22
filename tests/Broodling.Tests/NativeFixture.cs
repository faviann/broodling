namespace Broodling.Tests;

internal sealed class NativeFixture : IDisposable
{
    internal static string RepositoryRoot { get; } = FindRoot();
    internal static string Python => Environment.GetEnvironmentVariable("BROODLING_TEST_PYTHON") ?? Path.Combine(RepositoryRoot, ".venv", "bin", "python");
    internal static string Launcher => Path.Combine(RepositoryRoot, "src", "Broodling.Codex", "bin",
#if DEBUG
        "Debug",
#else
        "Release",
#endif
        "net10.0", "linux-x64", "codex");
    internal AttemptFixture Git { get; } = new();
    internal string Root => Git.State.Root;
    internal string Home => Path.Combine(Root, "home");
    internal string CodexHome => Path.Combine(Root, "codex-home");
    internal string NativeState { get; } = Path.Combine("/dev/shm", "b137-" + Guid.NewGuid().ToString("N"));
    internal CodexProfile Codex { get; }
    internal NativeProfile Profile { get; }
    internal NativeFixture(string? provider = null)
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("These witnesses require Linux.");
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(CodexHome);
        File.WriteAllText(Path.Combine(CodexHome, "auth.json"), "{\"canary\":\"AUTH_NEVER_PERSIST\"}");
        var executable = Path.Combine(Root, "provider");
        ExecutableFile.Copy(provider ?? Path.Combine(RepositoryRoot, "tests", "fixtures", "software-change-codex"), executable);
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Codex = new(executable, Home, CodexHome, Launcher);
        Profile = new(NativeState, Codex, toolPath: "/usr/bin:/bin");
        Git.Git("remote", "add", "origin", "https://github.com/acme/widget.git");
    }
    internal AttemptRecord Provision(BroodlingStore store) => store.ProvisionAttempt(Git.Admit(store).AttemptId);
    internal static ZeroshotTransport Transport() => new(Python);
    internal static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Broodling.sln"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Cannot locate test repository.");
    }
    public void Dispose()
    {
        Git.Dispose();
        if (Directory.Exists(NativeState)) Directory.Delete(NativeState, true);
    }
}

internal sealed class ControlledTransport : INativeTransport
{
    internal Func<string, IReadOnlyDictionary<string, string>, Task<string>> Submit { get; set; } = (_, _) => Task.FromResult("native-run");
    internal int Calls { get; private set; }
    internal Func<NativeLocator, string, CancellationToken, Task<NativeResult>> Wait { get; set; } = (_, _, _) => throw new InvalidOperationException("Unexpected wait");
    internal int WaitCalls { get; private set; }
    public Task<string> SubmitAsync(string requestJson, IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken = default)
    { Calls++; return Submit(requestJson, credentials); }
    public Task<NativeResult> WaitAsync(NativeLocator locator, string runId, CancellationToken cancellationToken = default)
    { WaitCalls++; return Wait(locator, runId, cancellationToken); }
    public Task<NativeResult> StopAsync(NativeLocator locator, string runId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected stop");
}
