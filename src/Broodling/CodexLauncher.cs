using System.Collections;
using System.Runtime.InteropServices;

namespace Broodling;

/// <summary>Native-owned launcher: apply local policy and exec the configured CLI in this PID.</summary>
public static class CodexLauncher
{
    private static readonly Dictionary<string, string> Policy = new()
    {
        ["sandbox_workspace_write.network_access"] = "false",
        ["sandbox_workspace_write.exclude_slash_tmp"] = "true", ["web_search"] = "\"disabled\"",
        ["approval_policy"] = "\"never\"", ["features.apps"] = "false", ["features.plugins"] = "false",
        ["features.hooks"] = "false", ["notify"] = "[]"
    };

    internal static string[] ApplyPolicy(string[] arguments)
    {
        if (arguments.FirstOrDefault() == "app-server")
            throw new UnsupportedRuntime("Broodling supplies explicit execution policy.");
        var filtered = new List<string>();
        var sandbox = "workspace-write";
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            if (argument is "--dangerously-bypass-approvals-and-sandbox" or "--full-auto" or "--search") continue;
            if (argument is "--sandbox" or "-s" || argument.StartsWith("--sandbox=", StringComparison.Ordinal))
            {
                sandbox = argument.Contains('=') ? argument[(argument.IndexOf('=') + 1)..]
                    : ++i < arguments.Length ? arguments[i] : "";
                if (sandbox is not ("read-only" or "workspace-write")) throw new UnsupportedRuntime("Unsupported Codex sandbox.");
                continue;
            }
            if (argument is "--ask-for-approval" or "-a" || argument.StartsWith("--ask-for-approval=", StringComparison.Ordinal))
            {
                if (!argument.Contains('=')) i++;
                continue;
            }
            if (argument is "--config" or "-c" || argument.StartsWith("--config=", StringComparison.Ordinal))
            {
                var value = argument.Contains('=') ? argument[9..] : i + 1 < arguments.Length ? arguments[i + 1] : "";
                var key = value.Split('=', 2)[0].Trim();
                if (Policy.ContainsKey(key) || key == "sandbox_mode")
                {
                    if (!argument.Contains('=')) i++;
                    continue;
                }
            }
            filtered.Add(argument);
        }
        if (filtered.FirstOrDefault() == "exec")
            filtered.InsertRange(1, new[] { "--ignore-user-config", "--ignore-rules" }
                .Concat(Policy.SelectMany(pair => new[] { "--config", pair.Key + "=" + pair.Value }))
                .Concat(["--sandbox", sandbox]));
        return filtered.ToArray();
    }

    public static int Run(string[] arguments)
    {
        try
        {
            var executable = Environment.GetEnvironmentVariable("BROODLING_REAL_CODEX");
            var home = Environment.GetEnvironmentVariable("BROODLING_PROFILE_HOME");
            var codexHome = Environment.GetEnvironmentVariable("BROODLING_ISOLATED_CODEX_HOME");
            if (string.IsNullOrEmpty(executable) || string.IsNullOrEmpty(home) || string.IsNullOrEmpty(codexHome))
                throw new UnsupportedRuntime("Explicit Codex profile paths are required.");
            var filtered = ApplyPolicy(arguments);
            var environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
                .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
            environment["HOME"] = home;
            environment["CODEX_HOME"] = codexHome;
            // No child process, waiter, or supervisor. execve replaces this managed process.
            using var argv = new Utf8Vector([executable, .. filtered]);
            using var envp = new Utf8Vector(environment.Select(pair => pair.Key + "=" + pair.Value));
            Execve(executable, argv.Pointer, envp.Pointer);
        }
        catch (Exception error) when (error is BroodlingException or ArgumentException) { }
        Console.Error.WriteLine("Broodling Codex execution policy refused this invocation.");
        return 78;
    }

    [DllImport("libc", EntryPoint = "execve", SetLastError = true)]
    private static extern int Execve([MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint argv, nint envp);
}
