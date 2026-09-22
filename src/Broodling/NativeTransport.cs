using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broodling;

public sealed record NativeResult(string RunId, bool Succeeded, JsonElement Output, string? Failure);

/// <summary>The SDK boundary only. G/H decide what a result/stop means to the application.</summary>
public interface INativeTransport
{
    Task<string> SubmitAsync(string requestJson, IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken = default);
    Task<NativeResult> WaitAsync(NativeLocator locator, string runId, CancellationToken cancellationToken = default);
    Task<NativeResult> StopAsync(NativeLocator locator, string runId, CancellationToken cancellationToken = default);
}

public sealed class ZeroshotTransport : INativeTransport
{
    private readonly string python;
    private readonly string bridge;
    public ZeroshotTransport(string pythonExecutable)
        : this(pythonExecutable, Path.Combine(AppContext.BaseDirectory, "bridge", "zeroshot_bridge.py")) { }
    internal ZeroshotTransport(string pythonExecutable, string bridgeScript)
    {
        python = Path.GetFullPath(pythonExecutable);
        bridge = Path.GetFullPath(bridgeScript);
    }

    public async Task<string> SubmitAsync(string requestJson, IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken = default)
    {
        await RequireVersion(cancellationToken);
        var response = await Call(new { op = "submit", request = JsonNode.Parse(requestJson), credentials }, cancellationToken);
        return RequiredString(response, "runId");
    }

    public Task<NativeResult> WaitAsync(NativeLocator locator, string runId, CancellationToken cancellationToken = default) => Observe("wait", locator, runId, cancellationToken);
    public Task<NativeResult> StopAsync(NativeLocator locator, string runId, CancellationToken cancellationToken = default) => Observe("stop", locator, runId, cancellationToken);

    private async Task<NativeResult> Observe(string operation, NativeLocator locator, string runId, CancellationToken cancellationToken)
    {
        locator.Validate();
        if (string.IsNullOrWhiteSpace(runId)) throw new NativeTransportError();
        await RequireVersion(cancellationToken);
        var response = await Call(new { op = operation, locator = locator.Json(), runId }, cancellationToken);
        try
        {
            var result = response.GetProperty("result");
            var id = RequiredString(result, "runId");
            if (id != runId) throw new NativeTransportError("foreign_run");
            return new(id, result.GetProperty("succeeded").GetBoolean(), result.GetProperty("output").Clone(),
                result.GetProperty("failure").ValueKind == JsonValueKind.Null ? null : result.GetProperty("failure").GetString());
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException) { throw new NativeTransportError(); }
    }

    private async Task RequireVersion(CancellationToken cancellationToken)
    {
        NativeProfile.RequireBundledRuntime();
        var response = await Call(new { op = "version" }, cancellationToken);
        if (RequiredString(response, "sdkVersion") != NativeProfile.SdkVersion || RequiredString(response, "nativeVersion") != NativeProfile.NativeVersion)
            throw new UnsupportedRuntime("The pinned SDK and bundled native runtime are required.");
    }

    private async Task<JsonElement> Call(object request, CancellationToken cancellationToken)
    {
        using var process = Start(request);
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var text = await output;
            _ = await error; // Drain, never expose raw stderr or persist it.
            if (process.ExitCode != 0) throw new NativeTransportError();
            using var document = JsonDocument.Parse(text);
            var response = document.RootElement;
            if (response.GetProperty("ok").GetBoolean()) return response.Clone();
            var kind = RequiredString(response, "error");
            if (kind == "submission_conflict")
                throw new SubmissionConflict("Native submission conflicts with its existing key.",
                    response.TryGetProperty("existingRunId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null);
            // Only allow known public SDK facts into diagnostics, never arbitrary returned strings.
            throw new NativeTransportError(kind is "RunNotFoundError" or "TargetError" ? kind : "sdk_failed");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        { throw new NativeTransportError(); }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: false); } // Detach this caller. Never kill the native process tree.
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }
            try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
        }
    }

    internal Process Start(object request)
    {
        var start = new ProcessStartInfo(python)
        {
            WorkingDirectory = Path.GetDirectoryName(bridge)!, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-I"); // Ignore ambient PYTHONPATH, user site and Python startup settings.
        start.ArgumentList.Add(bridge);
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/bin:/bin";
        Process process;
        try { process = Process.Start(start) ?? throw new NativeTransportError(); }
        catch (System.ComponentModel.Win32Exception) { throw new NativeTransportError(); }
        try
        {
            process.StandardInput.Write(JsonSerializer.Serialize(request));
            process.StandardInput.Close();
            return process;
        }
        catch
        {
            if (!process.HasExited)
                try { process.Kill(entireProcessTree: false); }
                catch (InvalidOperationException) when (process.HasExited) { }
            process.WaitForExit();
            process.Dispose();
            throw new NativeTransportError();
        }
    }

    private static string RequiredString(JsonElement value, string key)
    {
        if (!value.TryGetProperty(key, out var field) || field.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(field.GetString()))
            throw new NativeTransportError();
        return field.GetString()!;
    }
}
