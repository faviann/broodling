using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Spike.Zeroshot;

/// <summary>Terminal outcome returned by wait or force-stop.</summary>
public sealed record RunOutcome(string RunId, bool Succeeded, JsonNode? Output, string? Failure);

/// <summary>Where a correlated run can be addressed again. No credentials, no workspace.</summary>
public sealed record RunLocator(string Kind, string? StateDir = null, string? Origin = null)
{
    public static RunLocator Local(string stateDir) => new("local", StateDir: stateDir);

    public static RunLocator Direct(string origin) => new("direct", Origin: origin);

    public JsonObject ToJson()
    {
        var value = new JsonObject { ["kind"] = Kind };
        if (StateDir is not null)
        {
            value["stateDir"] = StateDir;
        }

        if (Origin is not null)
        {
            value["origin"] = Origin;
        }

        return value;
    }
}

/// <summary>Everything the pinned SDK needs for one dispatch, frozen by the caller.</summary>
public sealed record Dispatch(
    RunLocator Target,
    string Workspace,
    IReadOnlyDictionary<string, string> Environment,
    string SubmissionKey,
    string Title,
    string Task,
    JsonObject Preset,
    JsonObject Runtime,
    JsonObject? Source = null);

public sealed class SubmissionConflictException(string message, string existingRunId) : Exception(message)
{
    public string ExistingRunId { get; } = existingRunId;
}

public sealed class BridgeException(string error, string message) : Exception($"{error}: {message}")
{
    public string Error { get; } = error;
}

/// <summary>Calls the pinned Zeroshot SDK through one short-lived Python process per operation.</summary>
public sealed class ZeroshotBridge(
    string pythonExecutable,
    string bridgeScript,
    IReadOnlyDictionary<string, string>? processEnvironment = null)
{
    public async Task<string> VersionAsync(CancellationToken cancellationToken = default)
    {
        var response = await CallAsync(new JsonObject { ["op"] = "version" }, cancellationToken);
        return (string)response["sdkVersion"]!;
    }

    public async Task<string> SubmitAsync(
        Dispatch dispatch,
        IReadOnlyDictionary<string, string>? secrets = null,
        CancellationToken cancellationToken = default)
    {
        var environment = new JsonObject();
        foreach (var (name, value) in dispatch.Environment)
        {
            environment[name] = value;
        }

        var call = new JsonObject
        {
            ["op"] = "submit",
            ["target"] = dispatch.Target.ToJson(),
            ["workspace"] = dispatch.Workspace,
            ["environment"] = environment,
            ["submissionKey"] = dispatch.SubmissionKey,
            ["title"] = dispatch.Title,
            ["task"] = dispatch.Task,
            ["preset"] = dispatch.Preset.DeepClone(),
            ["runtime"] = dispatch.Runtime.DeepClone(),
        };

        if (dispatch.Source is not null)
        {
            call["source"] = dispatch.Source.DeepClone();
        }

        if (secrets is { Count: > 0 })
        {
            var supplied = new JsonObject();
            foreach (var (name, value) in secrets)
            {
                supplied[name] = value;
            }

            call["secrets"] = supplied;
        }

        var response = await CallAsync(call, cancellationToken);
        return (string)response["runId"]!;
    }

    public Task<RunOutcome> WaitAsync(RunLocator locator, string runId, CancellationToken cancellationToken = default) =>
        ObserveAsync("wait", locator, runId, cancellationToken);

    public Task<RunOutcome> StopAsync(RunLocator locator, string runId, CancellationToken cancellationToken = default) =>
        ObserveAsync("stop", locator, runId, cancellationToken);

    private async Task<RunOutcome> ObserveAsync(
        string operation,
        RunLocator locator,
        string runId,
        CancellationToken cancellationToken)
    {
        var call = new JsonObject
        {
            ["op"] = operation,
            ["target"] = locator.ToJson(),
            ["runId"] = runId,
        };
        var result = (JsonObject)(await CallAsync(call, cancellationToken))["result"]!;
        return new RunOutcome(
            (string)result["runId"]!,
            (bool)result["succeeded"]!,
            result["output"],
            (string?)result["failure"]);
    }

    /// <summary>Exposed so a spike test can observe the bridge process itself.</summary>
    public Process Start(JsonObject call)
    {
        var start = new ProcessStartInfo(pythonExecutable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(bridgeScript);
        // The bridge inherits nothing from this host: the SDK environment is data in the call.
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/bin:/bin";
        foreach (var (name, value) in processEnvironment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }
        var process = Process.Start(start) ?? throw new InvalidOperationException("bridge did not start");
        process.StandardInput.Write(call.ToJsonString());
        process.StandardInput.Close();
        return process;
    }

    private async Task<JsonObject> CallAsync(JsonObject call, CancellationToken cancellationToken)
    {
        using var process = Start(call);
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Detach this caller only. The bridge owns no durable run state.
            Detach(process);
            throw;
        }

        var body = await stdout;
        if (body.Length == 0)
        {
            throw new BridgeException("bridge_no_response", await stderr);
        }

        var response = (JsonObject)JsonNode.Parse(body)!;
        if ((bool)response["ok"]! is false)
        {
            var error = (string)response["error"]!;
            var message = (string)response["message"]!;
            throw error == "submission_conflict"
                ? new SubmissionConflictException(message, (string)response["existingRunId"]!)
                : new BridgeException(error, message);
        }

        return response;
    }

    private static void Detach(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: false);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
