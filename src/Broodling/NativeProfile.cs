using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>Current secrets are never part of the frozen invocation or a diagnostic.</summary>
public sealed class DispatchCredentials(string? githubToken, string? gatewayBaseUrl, string? gatewayApiKey)
{
    internal Dictionary<string, string> Environment()
    {
        if (string.IsNullOrWhiteSpace(githubToken) || githubToken.Length > 4096
            || string.IsNullOrWhiteSpace(gatewayApiKey) || gatewayApiKey.Length > 4096
            || gatewayBaseUrl != NativeProfile.GatewayBaseUrl)
            throw new UnsupportedRuntime("PR dispatch requires current GitHub/gateway credentials and the exact supported gateway URL.");
        return new() { ["GH_TOKEN"] = githubToken, ["GATEWAY_BASE_URL"] = gatewayBaseUrl, ["GATEWAY_API_KEY"] = gatewayApiKey };
    }
    public override string ToString() => nameof(DispatchCredentials);
}

public sealed record NativeLocator(string Kind, string Address, string SdkVersion = NativeProfile.SdkVersion)
{
    internal void Validate()
    {
        NativeProfile.RequireBundledRuntime();
        if (SdkVersion != NativeProfile.SdkVersion || string.IsNullOrWhiteSpace(Address)
            || Kind is not ("local" or "direct"))
            throw new UnsupportedRuntime("The frozen native locator is unsupported.");
        if (Kind == "local" && (!Path.IsPathFullyQualified(Address) || PhysicalPaths.Resolve(Address) != Address))
            throw new UnsupportedRuntime("Native state must remain canonical.");
    }
    internal JsonObject Json() => new() { ["kind"] = Kind, ["address"] = Address, ["sdkVersion"] = SdkVersion };
    internal static NativeLocator Read(JsonNode value) => new((string)value["kind"]!, (string)value["address"]!, (string)value["sdkVersion"]!);
}

/// <summary>Fixed execution policy. There is no caller-selected model/runtime or arbitrary environment map.</summary>
public sealed class NativeProfile
{
    public const string SdkVersion = "10.3.0.post1";
    public const string NativeVersion = "zeroshot 10.3.0";
    public const string NativeSourceRevision = "054ad3fd6c763b98d12f5b2e90830b97116561ad";
    public const string NativeExecutableSha256 = "afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06";
    public const string GatewayBaseUrl = "https://cliproxy.local.faviann.com/v1";
    internal static readonly string[] OperatingVariables = ["HOME", "CODEX_HOME", "LANG", "LC_ALL", "SYSTEMROOT", "TEMP", "TMP", "TMPDIR", "USERPROFILE", "XDG_CACHE_HOME", "XDG_CONFIG_HOME"];
    private readonly string stateDirectory;
    private readonly string toolPath;
    private readonly string? directOrigin;
    private readonly CodexProfile? codex;

    public NativeProfile(string stateDirectory, CodexProfile? codex = null, string? directOrigin = null, string? toolPath = null)
    {
        this.stateDirectory = PhysicalPaths.Resolve(stateDirectory);
        this.codex = codex;
        this.directOrigin = directOrigin;
        this.toolPath = toolPath ?? Environment.GetEnvironmentVariable("PATH") ?? "";
    }

    internal static void RequireBundledRuntime()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZEROSHOT_PYTHON_NATIVE_BINARY")))
            throw new UnsupportedRuntime("Use the pinned SDK's bundled native executable.");
    }

    internal JsonObject Target(string delivery)
    {
        RequireBundledRuntime();
        if (PhysicalPaths.Resolve(stateDirectory) != stateDirectory)
            throw new UnsupportedRuntime("Native state must remain canonical.");
        var environment = OperatingVariables.ToDictionary(name => name, _ => "");
        environment["PATH"] = toolPath;
        JsonObject? identity = null;
        if (delivery == "none")
        {
            if (codex is null) throw new UnsupportedRuntime("Local execution requires an explicit Codex profile.");
            identity = codex.Identity();
            foreach (var pair in codex.Environment(toolPath)) environment[pair.Key] = pair.Value;
        }
        else if (delivery != "pull_request" || string.IsNullOrWhiteSpace(directOrigin))
            throw new UnsupportedRuntime("PR execution requires an explicit direct target.");
        return new()
        {
            ["locator"] = new NativeLocator(delivery == "none" ? "local" : "direct", delivery == "none" ? stateDirectory : directOrigin!).Json(),
            ["stateDirectory"] = stateDirectory,
            ["environment"] = new JsonObject(environment.Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))),
            ["codexProfile"] = identity
        };
    }

    internal static JsonObject Runtime(string delivery)
    {
        var runtime = new JsonObject
        {
            ["harness"] = "codex", ["provider"] = delivery == "none" ? "openai" : "gateway",
            ["model"] = "gpt-5.6-sol", ["effort"] = "medium", ["size"] = "small", ["session_scope"] = "execution"
        };
        if (delivery == "none") runtime["connections"] = new JsonObject { ["profile"] = new JsonArray("BROODLING_REAL_CODEX", "BROODLING_PROFILE_HOME", "BROODLING_ISOLATED_CODEX_HOME") };
        return runtime;
    }

    internal IReadOnlyDictionary<string, string> ValidateDispatch(JsonObject request, AttemptRecord attempt, DispatchCredentials? credentials)
    {
        var delivery = (string)request["preset"]!["delivery"]!;
        if (!JsonNode.DeepEquals(request["target"], Target(delivery))
            || !JsonNode.DeepEquals(request["runtime"], Runtime(delivery))
            || (string?)request["preset"]!["name"] != "software-change")
            throw new SubmissionConflict("Execution policy differs from the frozen invocation.");
        foreach (var protectedPath in new[] { attempt.Allocation.WorktreePath, attempt.B1.Repository })
            if (PhysicalPaths.Contains(protectedPath, stateDirectory) || PhysicalPaths.Contains(stateDirectory, protectedPath))
                throw new UnsupportedRuntime("Native state must be separate from candidate and shared Git.");
        if (delivery == "none")
        {
            // Preserve the executable baseline's initial-home validation on ambiguous replay too.
            codex!.Validate(attempt.Allocation.WorktreePath, toolPath);
            return new Dictionary<string, string>();
        }
        var source = request["source"]!;
        var branch = (string)source["branch"]!;
        if (GitCustody.Run(attempt.Allocation.WorktreePath, ["check-ref-format", "--branch", branch]).ExitCode != 0
            || branch.StartsWith("-", StringComparison.Ordinal))
            throw new UnsupportedRuntime("The authorized target branch is invalid.");
        if (GitHubOriginRepository((string)request["originUrl"]!) != (string)source["repository"]!)
            throw new UnsupportedRuntime("The source origin must match the authorized GitHub repository.");
        return credentials?.Environment() ?? throw new UnsupportedRuntime("Current PR dispatch credentials are required.");
    }

    // Git remotes are not work-reference input: a bare owner/name is a local path,
    // not implicit github.com. Preserve the baseline's exact repository path comparison.
    internal static string? GitHubOriginRepository(string origin)
    {
        string path;
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return null;
            path = uri.AbsolutePath;
        }
        else
        {
            var separator = origin.IndexOf(':');
            if (separator < 0 || !origin[..separator].Split('@')[^1].Equals("github.com", StringComparison.OrdinalIgnoreCase)) return null;
            path = origin[(separator + 1)..];
        }
        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.Ordinal)) path = path[..^4];
        var parts = path.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length > 0) ? path : null;
    }
}

public sealed class CodexProfile
{
    [DllImport("libc", EntryPoint = "faccessat")]
    private static extern int EffectiveAccess(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode, int flags);

    public const string Version = "codex-cli 0.153.4";
    public string RealCodex { get; }
    public string ProfileHome { get; }
    public string CodexHome { get; }
    public string Launcher { get; }

    public CodexProfile(string realCodex, string profileHome, string codexHome, string launcher)
    {
        RealCodex = PhysicalPaths.Resolve(realCodex);
        ProfileHome = PhysicalPaths.Resolve(profileHome);
        CodexHome = PhysicalPaths.Resolve(codexHome);
        Launcher = PhysicalPaths.Resolve(launcher);
    }

    internal Dictionary<string, string> Environment(string path) => new()
    {
        ["PATH"] = Path.GetDirectoryName(Launcher) + ":" + path,
        ["HOME"] = ProfileHome, ["CODEX_HOME"] = CodexHome,
        ["BROODLING_REAL_CODEX"] = RealCodex, ["BROODLING_PROFILE_HOME"] = ProfileHome,
        ["BROODLING_ISOLATED_CODEX_HOME"] = CodexHome
    };

    internal JsonObject Identity()
    {
        // The pinned native runtime resolves the literal command "codex" through PATH.
        if (Path.GetFileName(Launcher) != "codex" || Path.GetDirectoryName(Launcher)!.Contains(Path.PathSeparator))
            throw new UnsupportedRuntime("The validated launcher must be the native codex command.");
        foreach (var path in new[] { RealCodex, ProfileHome, CodexHome, Launcher })
            if (PhysicalPaths.Resolve(path) != path) throw new UnsupportedRuntime("Provider paths must remain canonical.");
        var directory = Path.GetDirectoryName(Launcher)!;
        // Freeze the executable and managed policy, not just the apphost shim. No authentication bytes.
        var files = new[] { Launcher, Path.Combine(directory, "codex.dll"), Path.Combine(directory, "codex.deps.json"),
            Path.Combine(directory, "codex.runtimeconfig.json"), Path.Combine(directory, "Broodling.dll") };
        if (files.Any(path => !File.Exists(path) || PhysicalPaths.Resolve(path) != path))
            throw new UnsupportedRuntime("The complete C# Codex launcher is required.");
        if (Digests.Bytes(File.ReadAllBytes(files[^1])) != Digests.Bytes(File.ReadAllBytes(typeof(CodexProfile).Assembly.Location)))
            throw new UnsupportedRuntime("The launcher must use this application's policy assembly.");
        return new()
        {
            ["profile"] = "broodling-dotnet-no-effect-codex/v1", ["codexVersion"] = Version,
            ["realCodex"] = RealCodex, ["profileHome"] = ProfileHome, ["isolatedCodexHome"] = CodexHome,
            ["launcher"] = Launcher, ["launcherSha256"] = Digests.Parts(files.Select(path => Digests.Bytes(File.ReadAllBytes(path))).ToArray())
        };
    }

    internal void Validate(string candidate, string toolPath)
    {
        if (!OperatingSystem.IsLinux()) throw new UnsupportedRuntime("The Codex execution profile requires Linux.");
        Identity();
        if (new[] { RealCodex, ProfileHome, CodexHome, Launcher }.Any(path => PhysicalPaths.Contains(candidate, path))
            || PhysicalPaths.Contains(ProfileHome, CodexHome) || PhysicalPaths.Contains(CodexHome, ProfileHome))
            throw new UnsupportedRuntime("Provider paths must be separate from the candidate and homes from each other.");
        var auth = Path.Combine(CodexHome, "auth.json");
        if (!Directory.Exists(ProfileHome) || Directory.EnumerateFileSystemEntries(ProfileHome).Any()
            || !Directory.Exists(CodexHome) || !Directory.EnumerateFileSystemEntries(CodexHome).SequenceEqual([auth])
            || !File.Exists(auth) || new FileInfo(auth).LinkTarget is not null)
            throw new UnsupportedRuntime("HOME must start empty and CODEX_HOME auth-only.");
        if (RealCodex == Launcher) throw new UnsupportedRuntime("Codex and launcher must be different executables.");
        foreach (var path in new[] { RealCodex, Launcher })
            // Linux AT_FDCWD, X_OK, AT_EACCESS: check this dispatch identity, not merely any execute bit.
            if (!File.Exists(path) || EffectiveAccess(-100, path, 1, 0x200) != 0)
                throw new UnsupportedRuntime("Codex and launcher must be executable.");
        var start = new ProcessStartInfo(RealCodex) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--version");
        start.Environment.Clear();
        start.Environment["PATH"] = toolPath;
        start.Environment["HOME"] = ProfileHome;
        start.Environment["CODEX_HOME"] = CodexHome;
        using var process = Process.Start(start) ?? throw new UnsupportedRuntime("Cannot check Codex version.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000)) { process.Kill(entireProcessTree: false); process.WaitForExit(); throw new UnsupportedRuntime("Cannot check Codex version."); }
        _ = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0 || output.GetAwaiter().GetResult().Trim() != Version)
            throw new UnsupportedRuntime("The pinned Codex version is required.");
    }
}
