using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Spike.Zeroshot;

/// <summary>
/// Builds one disposable controlled dispatch: a real Git workspace, the repository's
/// no-effect Codex launcher profile, and a private native state directory.
/// Everything here is policy the C# side owns. None of it reaches the bridge as logic.
/// </summary>
public sealed class Scenario : IDisposable
{
    private const string GatewayBaseUrl = "https://cliproxy.local.faviann.com/v1";

    private Scenario(string root, string stateDir, string repositoryRoot)
    {
        Root = root;
        StateDir = stateDir;
        Workspace = repositoryRoot;
    }

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string PythonExecutable { get; } = Path.Combine(RepositoryRoot, ".venv", "bin", "python");

    public static string BridgeScript { get; } =
        Path.Combine(RepositoryRoot, "spike", "130-zeroshot-bridge", "bridge", "zsbridge.py");

    /// <summary>Bridge wired to the controlled stub SDK instead of the pinned one.</summary>
    public static ZeroshotBridge StubBridge() => new(
        PythonExecutable,
        BridgeScript,
        new Dictionary<string, string>
        {
            ["PYTHONPATH"] = Path.Combine(RepositoryRoot, "spike", "130-zeroshot-bridge", "stub"),
        });

    public string Root { get; }

    public string StateDir { get; }

    public string Workspace { get; }

    public RunLocator Locator => RunLocator.Local(StateDir);

    public static Scenario Create(string name, string? provider = null, double providerDelaySeconds = 0)
    {
        var root = Directory.CreateTempSubdirectory($"spike130-{name}-").FullName;
        // Only disposable controller sockets belong on /dev/shm; the workspace stays durable.
        var stateDir = Directory.CreateTempSubdirectory("spike130-state-").FullName;
        var workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);

        Git(workspace, "init", "--initial-branch", "candidate");
        Git(workspace, "config", "user.email", "spike@example.invalid");
        Git(workspace, "config", "user.name", "Spike");
        File.WriteAllText(Path.Combine(workspace, "README.md"), "original\n");
        Git(workspace, "add", "README.md");
        Git(workspace, "-c", "commit.gpgsign=false", "commit", "-m", "base");
        Git(workspace, "remote", "add", "origin", "https://github.com/faviann/broodling.git");

        var scenario = new Scenario(root, stateDir, workspace);
        scenario.BuildCodexProfile(provider, providerDelaySeconds);
        return scenario;
    }

    public IReadOnlyDictionary<string, string> Environment { get; private set; } = new Dictionary<string, string>();

    public string HeadCommit => Git(Workspace, "rev-parse", "HEAD").Trim();

    public JsonObject Runtime(string provider = "openai") => new()
    {
        ["harness"] = "codex",
        ["provider"] = provider,
        ["model"] = "gpt-5.6-sol",
        ["effort"] = "medium",
        ["size"] = "small",
        ["session_scope"] = "execution",
        ["connections"] = new JsonObject
        {
            ["profile"] = new JsonArray("BROODLING_REAL_CODEX", "BROODLING_PROFILE_HOME", "BROODLING_ISOLATED_CODEX_HOME"),
        },
    };

    public Dispatch Dispatch(string submissionKey, string task = "Make the requested README change.", string? title = null) =>
        new(
            Locator,
            Workspace,
            Environment,
            submissionKey,
            title ?? $"Spike {submissionKey}",
            task,
            new JsonObject { ["name"] = "software-change", ["delivery"] = "none" },
            Runtime());

    private void BuildCodexProfile(string? provider, double providerDelaySeconds)
    {
        var profileHome = Path.Combine(Root, "codex-profile-home");
        var codexHome = Path.Combine(Root, "codex-isolated-home");
        Directory.CreateDirectory(profileHome);
        Directory.CreateDirectory(codexHome);
        File.WriteAllText(Path.Combine(codexHome, "auth.json"), "{}");

        string realCodex;
        if (provider == "slow")
        {
            // Each scenario owns its provider copy and delay; parallel tests share no file.
            var providerDirectory = Path.Combine(Root, "provider");
            Directory.CreateDirectory(providerDirectory);
            realCodex = Path.Combine(providerDirectory, "slow-codex");
            File.Copy(
                Path.Combine(RepositoryRoot, "spike", "130-zeroshot-bridge", "provider", "slow-codex"),
                realCodex);
            File.SetUnixFileMode(realCodex, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            File.WriteAllText(
                Path.Combine(providerDirectory, "delay"),
                providerDelaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            realCodex = Path.Combine(RepositoryRoot, "tests", "fixtures", "software-change-codex");
        }

        var launcherDirectory = Path.Combine(RepositoryRoot, "broodling", "codex_bin");
        Environment = new Dictionary<string, string>
        {
            ["HOME"] = profileHome,
            ["CODEX_HOME"] = codexHome,
            ["LANG"] = "",
            ["LC_ALL"] = "",
            ["SYSTEMROOT"] = "",
            ["TEMP"] = "",
            ["TMP"] = "",
            ["TMPDIR"] = "",
            ["USERPROFILE"] = "",
            ["XDG_CACHE_HOME"] = "",
            ["XDG_CONFIG_HOME"] = "",
            ["PATH"] = $"{launcherDirectory}:/usr/bin:/bin",
            ["BROODLING_REAL_CODEX"] = realCodex,
            ["BROODLING_PROFILE_HOME"] = profileHome,
            ["BROODLING_ISOLATED_CODEX_HOME"] = codexHome,
        };
    }

    public static string Git(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }

        return output;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "pyproject.toml")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    public void Dispose()
    {
        foreach (var path in new[] { Root, StateDir })
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
