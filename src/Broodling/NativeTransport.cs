using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;

namespace Broodling;

public sealed record NativeResult(string RunId, bool Succeeded, JsonElement Output, string? Failure);

/// <summary>The SDK boundary only. G/H decide what a result/stop means to the application.</summary>
public interface INativeTransport
{
    /// <summary>
    /// <paramref name="initiation"/> is a shared lock description. Keep it open in every process
    /// that can still create the native run; installation status is undrained while any does.
    /// </summary>
    Task<string> SubmitAsync(string requestJson, IReadOnlyDictionary<string, string> credentials, SafeHandle initiation,
        CancellationToken cancellationToken = default);
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

    public async Task<string> SubmitAsync(string requestJson, IReadOnlyDictionary<string, string> credentials, SafeHandle initiation,
        CancellationToken cancellationToken = default)
    {
        await RequireVersion(cancellationToken);
        var response = await Call(new { op = "submit", request = JsonNode.Parse(requestJson), credentials }, cancellationToken, initiation);
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

    private async Task<JsonElement> Call(object request, CancellationToken cancellationToken, SafeHandle? initiation = null)
    {
        try
        {
            var (exitCode, text) = initiation is null ? await Run(request, cancellationToken) : await RunHolding(request, initiation, cancellationToken);
            if (exitCode != 0) throw new NativeTransportError();
            using var document = JsonDocument.Parse(text);
            var response = document.RootElement;
            if (response.GetProperty("ok").GetBoolean()) return response.Clone();
            var kind = RequiredString(response, "error");
            if (kind == "submission_conflict")
                throw new SubmissionConflict("Native submission conflicts with its existing key.",
                    RequiredString(response, "existingRunId"));
            // Only allow known public SDK facts into diagnostics, never arbitrary returned strings.
            throw new NativeTransportError(kind is "RunNotFoundError" or "TargetError" ? kind : "sdk_failed");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        { throw new NativeTransportError(); }
    }

    private async Task<(int ExitCode, string Output)> Run(object request, CancellationToken cancellationToken)
    {
        using var process = Start(request);
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var text = await output;
            _ = await error; // Drain, never expose raw stderr or persist it.
            return (process.ExitCode, text);
        }
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

    /// <summary>
    /// The bridge inherits the initiation lock description, so the lock stays held while the
    /// bridge can still submit even if this caller dies. Its own children do not inherit it.
    /// </summary>
    private async Task<(int ExitCode, string Output)> RunHolding(object request, SafeHandle initiation, CancellationToken cancellationToken)
    {
        AdministrativeGitProcess.RequireSupportedHost();
        int pid, input, stdout, stderr, spawned;
        using (var arguments = new Utf8Vector([python, "-I", bridge]))
        using (var environment = new Utf8Vector(["PATH=/usr/bin:/bin"]))
        {
            var held = false;
            try
            {
                initiation.DangerousAddRef(ref held);
                spawned = broodling_spawn_bridge(python, arguments.Pointer, environment.Pointer, Path.GetDirectoryName(bridge)!,
                    initiation.DangerousGetHandle().ToInt32(), out pid, out input, out stdout, out stderr);
            }
            finally { if (held) initiation.DangerousRelease(); }
        }
        if (spawned != 0) throw new NativeTransportError();

        using var outputHandle = new SafeFileHandle(stdout, ownsHandle: true);
        using var errorHandle = new SafeFileHandle(stderr, ownsHandle: true);
        var output = Task.Run(() => Read(outputHandle));
        var error = Task.Run(() => Read(errorHandle));
        var gate = new object();
        bool exited = false, killed = false;
        void Detach() { lock (gate) if (!exited) killed = kill(pid, 9) == 0; } // Never the native process tree.
        var exit = Task.Run(() =>
        {
            var waited = broodling_wait_exited(pid);
            lock (gate) exited = true;
            var status = 0;
            while (waitpid(pid, out status, 0) != pid)
                if (Marshal.GetLastPInvokeError() != 4 /* EINTR */) return -1;
            return waited == 0 && (status & 0x7f) == 0 ? (status >> 8) & 0xff : -1;
        });
        try
        {
            using (var stream = new FileStream(new SafeFileHandle(input, ownsHandle: true), FileAccess.Write))
                stream.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));
        }
        catch (IOException) { Detach(); }
        using (cancellationToken.Register(Detach))
        {
            var exitCode = await exit;
            var text = await output;
            _ = await error;
            if (killed) cancellationToken.ThrowIfCancellationRequested();
            return (exitCode, text);
        }
    }

    private static string Read(SafeFileHandle handle)
    {
        using var stream = new FileStream(handle, FileAccess.Read);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [DllImport("broodling_git", CallingConvention = CallingConvention.Cdecl)]
    private static extern int broodling_spawn_bridge([MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint argv, nint envp,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string cwd, int lockFd, out int pid, out int stdin, out int stdout, out int stderr);
    [DllImport("broodling_git", CallingConvention = CallingConvention.Cdecl)]
    private static extern int broodling_wait_exited(int pid);
    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);
    [DllImport("libc")]
    private static extern int kill(int pid, int signal);

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
