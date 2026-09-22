using System.Diagnostics;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Spike.Zeroshot;

/// <summary>
/// Spike evidence for issue #130. Every test drives the real pinned Zeroshot SDK and its
/// bundled native engine through the C# client and the Python bridge. Only the Codex
/// provider is controlled; no paid provider and no network are used.
/// </summary>
public sealed class BridgeTests
{
    private static ZeroshotBridge Bridge() => new(Scenario.PythonExecutable, Scenario.BridgeScript);

    [Test]
    public async Task BridgeReportsThePinnedSdkVersion()
    {
        await Assert.That(await Bridge().VersionAsync()).IsEqualTo("10.3.0.post1");
    }

    [Test]
    public async Task NoEffectRunSucceedsWithNoStableOutput()
    {
        using var scenario = Scenario.Create("no-effect");
        var bridge = Bridge();

        var runId = await bridge.SubmitAsync(scenario.Dispatch("spike:no-effect:1"));
        var outcome = await bridge.WaitAsync(scenario.Locator, runId);

        await Assert.That(outcome.RunId).IsEqualTo(runId);
        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.Failure).IsNull();
        // The current capability gap: a successful no-effect run carries no accepted result.
        await Assert.That(outcome.Output).IsNull();
    }

    [Test]
    public async Task IdenticalReplayUnderTheSameKeyRecoversTheSameRun()
    {
        using var scenario = Scenario.Create("replay");
        var bridge = Bridge();
        var dispatch = scenario.Dispatch("spike:replay:1");

        var accepted = await bridge.SubmitAsync(dispatch);
        // Models a lost acknowledgement: the run exists, the caller never saw its identity.
        var recovered = await bridge.SubmitAsync(dispatch);

        await Assert.That(recovered).IsEqualTo(accepted);
    }

    [Test]
    public async Task DifferentRequestUnderTheSameKeyConflictsAndNamesTheExistingRun()
    {
        using var scenario = Scenario.Create("conflict");
        var bridge = Bridge();
        var accepted = await bridge.SubmitAsync(scenario.Dispatch("spike:conflict:1"));

        var conflict = await Assert.ThrowsAsync<SubmissionConflictException>(
            async () => await bridge.SubmitAsync(scenario.Dispatch("spike:conflict:1", title: "foreign request")));

        await Assert.That(conflict.ExistingRunId).IsEqualTo(accepted);
    }

    [Test]
    public async Task SourceDriftMakesAnIdenticalReplayIndistinguishableFromAGenuineConflict()
    {
        using var scenario = Scenario.Create("drift");
        var bridge = Bridge();
        var dispatch = scenario.Dispatch("spike:drift:1");
        var accepted = await bridge.SubmitAsync(dispatch);

        File.AppendAllText(Path.Combine(scenario.Workspace, "README.md"), "drift\n");
        Scenario.Git(scenario.Workspace, "add", "README.md");
        Scenario.Git(scenario.Workspace, "-c", "commit.gpgsign=false", "commit", "-m", "drift");

        var conflict = await Assert.ThrowsAsync<SubmissionConflictException>(
            async () => await bridge.SubmitAsync(dispatch));

        // Same exception, same identity, same message shape as a genuinely different request.
        // Nothing in the pinned API separates the two; the caller must decide from its own B1.
        await Assert.That(conflict.ExistingRunId).IsEqualTo(accepted);
        await Assert.That(conflict.Message).Contains("already identifies a different admitted run");
    }

    [Test]
    public async Task ReconnectNeedsOnlyTheLocatorAndRunIdentity()
    {
        using var scenario = Scenario.Create("reconnect");
        var bridge = Bridge();
        var runId = await bridge.SubmitAsync(scenario.Dispatch("spike:reconnect:1"));
        await bridge.WaitAsync(scenario.Locator, runId);

        // Nothing from dispatch survives: no workspace, no environment, no credentials.
        Directory.Delete(scenario.Workspace, recursive: true);
        var locator = RunLocator.Local(scenario.StateDir);

        var outcome = await bridge.WaitAsync(locator, runId);
        var repeated = await bridge.WaitAsync(locator, runId);

        await Assert.That(outcome.RunId).IsEqualTo(runId);
        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(repeated).IsEqualTo(outcome);
    }

    [Test]
    public async Task CancellingAWaitDetachesTheCallerAndLeavesTheRunAlone()
    {
        using var scenario = Scenario.Create("cancel", provider: "slow", providerDelaySeconds: 15);
        var bridge = Bridge();
        var runId = await bridge.SubmitAsync(scenario.Dispatch("spike:cancel:1"));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await bridge.WaitAsync(scenario.Locator, runId, cancellation.Token));

        var clock = Stopwatch.StartNew();
        var outcome = await bridge.WaitAsync(scenario.Locator, runId);
        var remaining = clock.Elapsed;

        await Assert.That(outcome.RunId).IsEqualTo(runId);
        await Assert.That(outcome.Succeeded).IsTrue();
        // The follow-up wait had to block, so the run was genuinely still executing when
        // the first caller detached. Cancellation did not stop or restart it.
        await Assert.That(remaining).IsGreaterThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task KillingTheBridgeMidWaitLosesNoRunState()
    {
        using var scenario = Scenario.Create("kill", provider: "slow", providerDelaySeconds: 15);
        var bridge = Bridge();
        var runId = await bridge.SubmitAsync(scenario.Dispatch("spike:kill:1"));

        var call = new JsonObject
        {
            ["op"] = "wait",
            ["target"] = scenario.Locator.ToJson(),
            ["runId"] = runId,
        };
        using (var waiter = bridge.Start(call))
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            waiter.Kill(entireProcessTree: false);
            await waiter.WaitForExitAsync();
        }

        var clock = Stopwatch.StartNew();
        var outcome = await bridge.WaitAsync(scenario.Locator, runId);
        var remaining = clock.Elapsed;

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(remaining).IsGreaterThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ExplicitStopIsASeparateRequestThatTerminalizesTheRun()
    {
        using var scenario = Scenario.Create("stop", provider: "slow", providerDelaySeconds: 40);
        var bridge = Bridge();
        var runId = await bridge.SubmitAsync(scenario.Dispatch("spike:stop:1"));
        await Task.Delay(TimeSpan.FromSeconds(3));

        var stopped = await bridge.StopAsync(scenario.Locator, runId);
        var observed = await bridge.WaitAsync(scenario.Locator, runId);

        await Assert.That(stopped.RunId).IsEqualTo(runId);
        // A stopped run is a failed run. It is terminal, and it is not a receipt for
        // physical cessation of anything the provider started.
        await Assert.That(stopped.Succeeded).IsFalse();
        await Assert.That(stopped.Failure).IsEqualTo("force_stopped");
        await Assert.That(observed).IsEqualTo(stopped);
    }

    [Test]
    public async Task UnknownRunIdentityFailsClosedWithATypedError()
    {
        using var scenario = Scenario.Create("unknown");
        var bridge = Bridge();

        var error = await Assert.ThrowsAsync<BridgeException>(
            async () => await bridge.WaitAsync(scenario.Locator, "01a00000-0000-7000-8000-000000000000"));

        await Assert.That(error.Error).IsEqualTo("RunNotFoundError");
    }

    [Test]
    public async Task DirectTargetReconnectCarriesNoCredentialsAndFailsOnTransportAlone()
    {
        var bridge = Bridge();

        var error = await Assert.ThrowsAsync<BridgeException>(
            async () => await bridge.WaitAsync(RunLocator.Direct("http://127.0.0.1:8123"), "01a00000-0000-7000-8000-000000000000"));

        // No dispatch credentials were supplied and none were demanded: the pinned API
        // reaches transport before any authentication or configuration requirement.
        await Assert.That(error.Error).IsEqualTo("TargetError");
    }

    [Test]
    public async Task FullPullRequestReceiptReachesTheCallerUnchanged()
    {
        // The released engine has no offline path to a pull-request receipt, so this one
        // scenario substitutes the controlled stub SDK. It exercises C# decoding, not Zeroshot.
        using var scenario = Scenario.Create("receipt");
        var bridge = Scenario.StubBridge();
        var dispatch = scenario.Dispatch("spike:receipt:1") with
        {
            Preset = new JsonObject { ["name"] = "software-change", ["delivery"] = "pull_request" },
            Runtime = scenario.Runtime("gateway"),
            Source = new JsonObject
            {
                ["repository"] = "faviann/broodling",
                ["branch"] = "main",
                ["revision"] = scenario.HeadCommit,
            },
        };

        var runId = await bridge.SubmitAsync(
            dispatch,
            new Dictionary<string, string>
            {
                ["GH_TOKEN"] = "spike-github-token",
                ["GATEWAY_BASE_URL"] = "https://cliproxy.local.faviann.com/v1",
                ["GATEWAY_API_KEY"] = "spike-provider-key",
            });
        var outcome = await bridge.WaitAsync(scenario.Locator, runId);

        var receipt = (JsonObject)outcome.Output!;
        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That((string?)receipt["version"]).IsEqualTo("v1");
        await Assert.That((string?)receipt["mode"]).IsEqualTo("pr");
        await Assert.That((string?)receipt["outcome"]).IsEqualTo("opened");
        await Assert.That((string?)receipt["repository"]).IsEqualTo("faviann/broodling");
        await Assert.That((string?)receipt["targetBranch"]).IsEqualTo("main");
        await Assert.That((string?)receipt["headRevision"]).IsEqualTo(new string('b', 40));
        await Assert.That((string?)receipt["pullRequestId"]).IsEqualTo("50");
    }

    [Test]
    public async Task ReconnectAfterAPullRequestDispatchCarriesNoCredentials()
    {
        using var scenario = Scenario.Create("receipt-reconnect");
        var bridge = Scenario.StubBridge();
        var dispatch = scenario.Dispatch("spike:receipt-reconnect:1") with
        {
            Preset = new JsonObject { ["name"] = "software-change", ["delivery"] = "pull_request" },
            Runtime = scenario.Runtime("gateway"),
            Source = new JsonObject
            {
                ["repository"] = "faviann/broodling",
                ["branch"] = "main",
                ["revision"] = scenario.HeadCommit,
            },
        };
        var runId = await bridge.SubmitAsync(
            dispatch,
            new Dictionary<string, string> { ["GH_TOKEN"] = "spike-github-token" });

        // Same locator, no secrets argument, no workspace, no runtime, no preset.
        var outcome = await bridge.WaitAsync(scenario.Locator, runId);
        var stopped = await bridge.StopAsync(scenario.Locator, runId);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(stopped.Failure).IsEqualTo("force_stopped");
    }

    [Test]
    public async Task BridgeProcessStartupCostIsRecorded()
    {
        var bridge = Bridge();
        var clock = Stopwatch.StartNew();
        await bridge.VersionAsync();
        var cold = clock.Elapsed;

        clock.Restart();
        await bridge.VersionAsync();
        var warm = clock.Elapsed;

        Console.WriteLine($"bridge process cost: cold={cold.TotalMilliseconds:F0}ms warm={warm.TotalMilliseconds:F0}ms");
        await Assert.That(warm).IsLessThan(TimeSpan.FromSeconds(5));
    }
}
