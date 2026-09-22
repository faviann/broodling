using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class NativePolicyTests
{
    [Test]
    public async Task GitRemoteAuthorityDoesNotTreatCallerShorthandAsAGitHubOrigin()
    {
        foreach (var origin in new[] { "https://github.com/acme/widget.git", "git@github.com:acme/widget.git", "ssh://git@github.com/acme/widget" })
            await Assert.That(NativeProfile.GitHubOriginRepository(origin)).IsEqualTo("acme/widget");
        foreach (var origin in new[] { "acme/widget", "github.com/acme/widget", "/acme/widget", "https://other.invalid/acme/widget", "" })
            await Assert.That(NativeProfile.GitHubOriginRepository(origin)).IsNull();
        await Assert.That(NativeProfile.GitHubOriginRepository("https://github.com/ACME/Widget.git")).IsEqualTo("ACME/Widget");
    }

    [Test]
    public async Task NativeGatewayExpansionConsumesCSharpRuntimeWithSeparateAgentAndDeliveryConnections()
    {
        using var fixture = new StoreFixture();
        var start = new ProcessStartInfo(NativeFixture.Python)
        {
            WorkingDirectory = fixture.Root, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/bin:/bin";
        start.ArgumentList.Add("-I");
        start.ArgumentList.Add(NativeFixture.Fixture("gateway-profile.py"));
        using var process = Process.Start(start)!;
        process.StandardInput.Write(NativeProfile.Runtime("pull_request").ToJsonString());
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
        if (process.ExitCode != 0) throw new Exception(await error);
        var text = await output;
        var value = JsonNode.Parse(text)!;
        await Assert.That((string)value["runtime"]!["sessionScope"]!).IsEqualTo("execution");
        var bindings = value["profile"]!["nodes"]!.AsObject().Select(pair => pair.Value!).ToArray();
        var agents = bindings.Where(binding => (string?)binding["kind"] == "agent").ToArray();
        await Assert.That(agents.Length > 0).IsTrue();
        foreach (var agent in agents)
        {
            await Assert.That((string)agent["model"]!).IsEqualTo("gpt-5.6-sol");
            await Assert.That((string)agent["effort"]!).IsEqualTo("medium");
            await Assert.That(JsonNode.DeepEquals(agent["connections"], JsonNode.Parse("{\"gateway\":[\"GATEWAY_API_KEY\",\"GATEWAY_BASE_URL\"]}"))).IsTrue();
        }
        var delivery = bindings.Single(binding => (string?)binding["kind"] == "git_delivery");
        await Assert.That(JsonNode.DeepEquals(delivery["connections"], JsonNode.Parse("{\"github\":[\"GH_TOKEN\"]}"))).IsTrue();
        foreach (var canary in new[] { "GITHUB_CANARY", "GATEWAY_CANARY", NativeProfile.GatewayBaseUrl })
            await Assert.That(text.Contains(canary)).IsFalse();
    }

    [Test]
    public async Task FrozenLauncherIdentityIncludesTheManagedPolicyNotJustItsApphost()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var directory = CopyLauncher(fixture);
        var profile = new NativeProfile(fixture.NativeState, new(fixture.Codex.RealCodex, fixture.Home, fixture.CodexHome, Path.Combine(directory, "codex")), toolPath: "/usr/bin:/bin");
        store.PrepareSubmission(attempt.AttemptId, profile);
        File.AppendAllText(Path.Combine(directory, "codex.dll"), "changed-managed-launcher");
        var transport = new ControlledTransport();
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, profile, transport)).Throws<SubmissionConflict>();
        await Assert.That(transport.Calls).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileRefusesALauncherThatNativeWouldNotResolve(bool pathSeparator)
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var directory = CopyLauncher(fixture, pathSeparator ? "launcher:split" : "launcher");
        var configured = Path.Combine(directory, pathSeparator ? "codex" : "policy-launcher");
        if (!pathSeparator) File.Move(Path.Combine(directory, "codex"), configured);
        var profile = new NativeProfile(fixture.NativeState,
            new(fixture.Codex.RealCodex, fixture.Home, fixture.CodexHome, configured), toolPath: "/usr/bin:/bin");
        var transport = new ControlledTransport();
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, profile, transport)).Throws<UnsupportedRuntime>();
        await Assert.That(transport.Calls).IsEqualTo(0);
        await Assert.That(store.FindSubmission(attempt.AttemptId)).IsNull();
    }

    private static string CopyLauncher(NativeFixture fixture, string directoryName = "launcher")
    {
        var directory = Directory.CreateDirectory(Path.Combine(fixture.Root, directoryName)).FullName;
        foreach (var name in new[] { "codex", "codex.dll", "codex.runtimeconfig.json", "codex.deps.json", "Broodling.dll" })
            File.Copy(Path.Combine(Path.GetDirectoryName(NativeFixture.Launcher)!, name), Path.Combine(directory, name));
        return directory;
    }

    [Test]
    public async Task LauncherExecsInSamePidPreservesPromptSandboxRuntimeAndNativeResume()
    {
        using var fixture = new NativeFixture(NativeFixture.Fixture("inspect-codex"));
        foreach (var sandbox in new[] { "read-only", "workspace-write" })
        {
            var start = new ProcessStartInfo(NativeFixture.Launcher)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.Environment.Clear();
            foreach (var pair in fixture.Codex.Environment("/usr/bin:/bin")) start.Environment[pair.Key] = pair.Value;
            start.Environment["HOME"] = "/ambient-home";
            start.Environment["CODEX_HOME"] = "/ambient-codex-home";
            foreach (var argument in new[] { "exec", "--sandbox", sandbox, "--full-auto", "--search", "--dangerously-bypass-approvals-and-sandbox",
                "--config", "sandbox_workspace_write.network_access=true", "-c", "features.apps=true", "--config=notify=[\"bad\"]",
                "--ask-for-approval", "always", "--model", "gpt-5.6-sol", "--json", "resume", "native-thread", "-" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            process.StandardInput.Write("Unchanged frozen prompt\r\n");
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Assert.That(process.ExitCode).IsEqualTo(0);
            await Assert.That(await error).IsEqualTo("");
            using var response = JsonDocument.Parse(await output);
            var value = response.RootElement;
            await Assert.That(value.GetProperty("pid").GetInt32()).IsEqualTo(process.Id);
            await Assert.That(value.GetProperty("stdin").GetString()).IsEqualTo("Unchanged frozen prompt\r\n");
            await Assert.That(value.GetProperty("home").GetString()).IsEqualTo(fixture.Home);
            await Assert.That(value.GetProperty("codexHome").GetString()).IsEqualTo(fixture.CodexHome);
            var args = value.GetProperty("argv").EnumerateArray().Select(item => item.GetString()).ToArray();
            await Assert.That(args[Array.IndexOf(args, "--sandbox") + 1]).IsEqualTo(sandbox);
            foreach (var required in new[] { "--ignore-user-config", "--ignore-rules", "sandbox_workspace_write.network_access=false",
                "sandbox_workspace_write.exclude_slash_tmp=true", "web_search=\"disabled\"", "approval_policy=\"never\"",
                "features.apps=false", "features.plugins=false", "features.hooks=false", "notify=[]" })
                await Assert.That(args.Contains(required)).IsTrue();
            foreach (var forbidden in new[] { "--full-auto", "--search", "--dangerously-bypass-approvals-and-sandbox", "features.apps=true", "--ephemeral", "always" })
                await Assert.That(args.Contains(forbidden)).IsFalse();
            await Assert.That(args.TakeLast(3).SequenceEqual(["resume", "native-thread", "-"])).IsTrue();
        }
        await Assert.That(() => CodexLauncher.ApplyPolicy(["app-server"])).Throws<UnsupportedRuntime>();
        await Assert.That(() => CodexLauncher.ApplyPolicy(["exec", "--sandbox", "danger-full-access"])).Throws<UnsupportedRuntime>();
        await Assert.That(CodexLauncher.ApplyPolicy(["exec", "-"]).Contains("workspace-write")).IsTrue();
    }

    [Test]
    [Arguments("home")]
    [Arguments("codex-home")]
    [Arguments("auth-link")]
    [Arguments("rebind")]
    [Arguments("version")]
    public async Task ProfileRefusesAmbientMaterialRebindingAndWrongExecutableVersion(string mutation)
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        store.PrepareSubmission(attempt.AttemptId, fixture.Profile);
        switch (mutation)
        {
            case "home": File.WriteAllText(Path.Combine(fixture.Home, "config"), "ambient"); break;
            case "codex-home": File.WriteAllText(Path.Combine(fixture.CodexHome, "config.toml"), "ambient"); break;
            case "auth-link":
                File.Move(Path.Combine(fixture.CodexHome, "auth.json"), Path.Combine(fixture.Root, "auth.json"));
                File.CreateSymbolicLink(Path.Combine(fixture.CodexHome, "auth.json"), Path.Combine(fixture.Root, "auth.json")); break;
            case "rebind":
                Directory.Move(fixture.Home, fixture.Home + "-original");
                Directory.CreateSymbolicLink(fixture.Home, fixture.Home + "-original"); break;
            case "version": File.WriteAllText(fixture.Codex.RealCodex, "#!/bin/sh\nprintf 'codex-cli wrong-version\\n'\n"); break;
        }
        var transport = new ControlledTransport();
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport)).Throws<UnsupportedRuntime>();
        await Assert.That(transport.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task GatewayCredentialRotationIsEphemeralAndChangedTargetOrPersistedInjectionRefuses()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var admitted = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()], ContractIngressTests.Propose,
            [new("pr", "Open PR", "pull_request", "main")]);
        var attempt = store.ProvisionAttempt(store.AdmitAttempt(admitted.Revision.ContractRevisionId, fixture.Git.Repository, fixture.Git.Workspaces).AttemptId);
        var profile = new NativeProfile(fixture.NativeState, directOrigin: "http://target.invalid", toolPath: "/usr/bin:/bin");
        var prepared = store.PrepareSubmission(attempt.AttemptId, profile);
        var transport = new ControlledTransport();
        foreach (var credentials in new DispatchCredentials?[] { null, new("", NativeProfile.GatewayBaseUrl, "key"),
            new("token", NativeProfile.GatewayBaseUrl, " "), new("token", NativeProfile.GatewayBaseUrl + "/", "key"),
            new(new string('s', 4097), NativeProfile.GatewayBaseUrl, "key") })
            await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, profile, transport, credentials)).Throws<UnsupportedRuntime>();
        await Assert.That(transport.Calls).IsEqualTo(0);
        var observed = new List<string>();
        transport.Submit = (request, credentials) =>
        {
            if (request != prepared.RequestJson) throw new Exception("Request changed during credential rotation");
            observed.Add(credentials["GATEWAY_API_KEY"]);
            throw new NativeTransportError();
        };
        foreach (var key in new[] { "KEY_ONE_CANARY", "KEY_TWO_CANARY" })
            await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, profile, transport,
                new("GH_CANARY", NativeProfile.GatewayBaseUrl, key))).Throws<NativeTransportError>();
        await Assert.That(observed.SequenceEqual(["KEY_ONE_CANARY", "KEY_TWO_CANARY"])).IsTrue();
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId,
            new NativeProfile(fixture.NativeState, directOrigin: "http://changed.invalid"), transport,
            new("GH_CANARY", NativeProfile.GatewayBaseUrl, "KEY_TWO_CANARY"))).Throws<SubmissionConflict>();
        var requestNode = JsonNode.Parse(prepared.RequestJson)!;
        foreach (var forbidden in new[] { "GH_TOKEN", "GATEWAY_API_KEY", "OPENAI_API_KEY", "CODEX_API_KEY", "OPENROUTER_API_KEY", "AWS_BEARER_TOKEN_BEDROCK", "GITHUB_TOKEN" })
        {
            requestNode["target"]!["environment"]![forbidden] = "";
            await Assert.That(() => profile.ValidateDispatch(requestNode.AsObject(), attempt,
                new("GH_CANARY", NativeProfile.GatewayBaseUrl, "KEY_TWO_CANARY"))).Throws<SubmissionConflict>();
            requestNode["target"]!["environment"]!.AsObject().Remove(forbidden);
        }
        requestNode["runtime"]!["model"] = "caller-choice";
        await Assert.That(() => profile.ValidateDispatch(requestNode.AsObject(), attempt, null)).Throws<SubmissionConflict>();
        var durable = JsonSerializer.Serialize(store.Status(attempt.ContractRevisionId));
        foreach (var canary in new[] { "KEY_ONE_CANARY", "KEY_TWO_CANARY", "GH_CANARY", NativeProfile.GatewayBaseUrl })
        {
            await Assert.That(durable.Contains(canary)).IsFalse();
            await Assert.That(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(fixture.Git.State.Path)).Contains(canary)).IsFalse();
        }
    }

    [Test]
    [NotInParallel]
    public async Task AmbientEnvironmentCannotSelectCredentialsHomesOrNativeOverride()
    {
        var values = new Dictionary<string, string>
        {
            ["OPENAI_API_KEY"] = "ambient-secret", ["GITHUB_TOKEN"] = "ambient-secret", ["GH_TOKEN"] = "ambient-secret",
            ["CODEX_API_KEY"] = "ambient-secret", ["GATEWAY_API_KEY"] = "ambient-secret", ["TMPDIR"] = "/ambient-scratch",
            ["HOME"] = "/ambient-home", ["PYTHONPATH"] = "/ambient-python"
        };
        var before = values.Keys.Append("ZEROSHOT_PYTHON_NATIVE_BINARY").ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            using var fixture = new NativeFixture();
            foreach (var pair in values) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            var target = new NativeProfile(fixture.NativeState, directOrigin: "http://target.invalid", toolPath: "/usr/bin:/bin").Target("pull_request");
            var environment = target["environment"]!.AsObject();
            foreach (var name in NativeProfile.OperatingVariables) await Assert.That((string)environment[name]!).IsEqualTo("");
            foreach (var name in values.Keys.Except(NativeProfile.OperatingVariables)) await Assert.That(environment.ContainsKey(name)).IsFalse();
            // A real bridge version/unknown-run call ignores PYTHONPATH and ambient secrets.
            var error = await Assert.ThrowsAsync<NativeTransportError>(async () => await NativeFixture.Transport().WaitAsync(
                new("local", fixture.NativeState), "01a00000-0000-7000-8000-000000000000"));
            await Assert.That(error!.Kind).IsEqualTo("RunNotFoundError");
            Environment.SetEnvironmentVariable("ZEROSHOT_PYTHON_NATIVE_BINARY", "/unapproved-native");
            await Assert.That(async () => await NativeFixture.Transport().WaitAsync(new("local", fixture.NativeState), "run")).Throws<UnsupportedRuntime>();
        }
        finally { foreach (var pair in before) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
    }
}
