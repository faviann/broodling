using Broodling;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

// A local test caller, never a product command or runtime helper.
try
{
    if (args[0] == "native-crash")
    {
        using var dispatchStore = new BroodlingApplication().OpenStore(args[1]);
        var profile = new NativeProfile(args[3], new CodexProfile(args[4], args[5], args[6], args[7]), toolPath: "/usr/bin:/bin");
        var mode = args[8];
        if (mode == "during-correlation" || mode == "during-prepare")
        {
            var connection = (Microsoft.Data.Sqlite.SqliteConnection)typeof(BroodlingStore)
                .GetField("connection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dispatchStore)!;
            connection.CreateFunction("crash_gate", () => { CrashTransport.Gate("transaction"); return 1; });
            using var command = connection.CreateCommand();
            command.CommandText = mode == "during-correlation"
                ? "CREATE TEMP TRIGGER crash_gate AFTER UPDATE ON native_submissions WHEN NEW.state = 'correlated' BEGIN SELECT crash_gate(); END;"
                : "CREATE TEMP TRIGGER crash_gate AFTER INSERT ON native_submissions BEGIN SELECT crash_gate(); END;";
            command.ExecuteNonQuery();
        }
        if (mode is "prepared" or "during-prepare")
        {
            dispatchStore.PrepareSubmission(args[2], profile);
            CrashTransport.Gate("prepared");
        }
        var submission = await dispatchStore.DispatchAsync(args[2], profile, new CrashTransport(new ZeroshotTransport(args[9]), mode));
        CrashTransport.Gate(submission.RunId!);
        return 99;
    }
    if (args[0] == "native-bridge-crash")
    {
        using var dispatchStore = new BroodlingApplication().OpenStore(args[1]);
        var profile = new NativeProfile(args[3], new CodexProfile(args[4], args[5], args[6], args[7]), toolPath: "/usr/bin:/bin");
        await dispatchStore.DispatchAsync(args[2], profile, new ZeroshotTransport(args[8], args[9]));
        return 99;
    }
    if (args[0] == "http-dispatch")
    {
        // The parent's controlled target decides where this caller is killed.
        using var httpStore = new BroodlingApplication().OpenStore(args[1]);
        await httpStore.DispatchHttpAsync(args[2], new DispatchCredentials("witness-github-token", NativeProfile.GatewayBaseUrl, "witness-gateway-key"));
        return 99;
    }
    if (args[0] == "repository-preparation-crash")
    {
        using var preparationStore = new BroodlingApplication().OpenStore(args[1]);
        var source = new GitHubRepositorySource(args[4], args[5]);
        await preparationStore.PrepareRequestBundleRepositoryAsync(args[2], args[3],
            new GitHubRepositoryCredentials("configured-token"), source);
        return 0;
    }
    if (args[0] == "host-profile")
    {
        AdministrativeGitProcess.RequireSupportedHost();
        Console.WriteLine("Packaged administrative Git shim loaded in supported host");
        return 0;
    }
    if (args[0] == "ignored-sigchld")
    {
        Native.signal(17, 1); // This isolated test host is deliberately unsupported.
        using var unsupported = new BroodlingApplication().OpenStore(args[1]);
        unsupported.ProvisionAttempt(args[2]);
        return 99;
    }
    using var store = new BroodlingApplication().OpenStore(args[1]);
    if (args[0] == "retire")
    {
        if (args.Length > 3) File.WriteAllText(args[3], "retirement-started");
        Console.WriteLine(JsonSerializer.Serialize(store.RetireAttempt(args[2])));
        return 0;
    }
    if (args[0] == "retry-crash")
    {
        var mode = args[3];
        var profile = new NativeProfile(args[5], new CodexProfile(args[6], args[7], args[8], args[9]), toolPath: "/usr/bin:/bin");
        if (mode is "allocation-write" or "prepare-write")
        {
            var connection = (Microsoft.Data.Sqlite.SqliteConnection)typeof(BroodlingStore)
                .GetField("connection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
            connection.CreateFunction("crash_gate", () => { CrashTransport.Gate(mode); return 1; });
            using var command = connection.CreateCommand();
            command.CommandText = mode == "allocation-write"
                ? "CREATE TEMP TRIGGER crash_gate AFTER INSERT ON attempts BEGIN SELECT crash_gate(); END;"
                : "CREATE TEMP TRIGGER crash_gate AFTER INSERT ON native_submissions BEGIN SELECT crash_gate(); END;";
            command.ExecuteNonQuery();
        }
        if (mode is "allocation-write" or "allocated")
            store.AdmitRetry(args[2], "process-retry", args[4], profile);
        else store.PrepareRetry(args[2], "process-retry", args[4], profile);
        CrashTransport.Gate(mode);
        return 99;
    }
    if (args[0] == "pipe-cleanup")
    {
        var attempt = store.GetAttempt(args[2]);
        WorktreeMaterialization.ClaimEnclosure(attempt);
        using var held = AdministrativeGitProcess.EnclosureLock.Acquire(Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.LockName));
        var missing = new ProcessStartInfo(Path.Combine(attempt.Allocation.Enclosure, "missing-executable"));
        void FailSpawn()
        {
            try { AdministrativeGitProcess.Run(missing, held); throw new Exception("Spawn should have failed"); }
            catch (WorktreeProvisioningError) { }
        }
        FailSpawn();
        var before = Directory.GetFiles("/proc/self/fd").Length;
        FailSpawn();
        if (Directory.GetFiles("/proc/self/fd").Length != before) throw new Exception("Spawn failure leaked descriptors");
        var flood = new ProcessStartInfo("/bin/sh");
        flood.ArgumentList.Add("-c");
        flood.ArgumentList.Add("head -c 262144 /dev/zero; head -c 262144 /dev/zero >&2");
        var result = AdministrativeGitProcess.Run(flood, held);
        if (result.ExitCode != 0 || result.Output.Length != 262144 || result.Error.Length != 262144)
            throw new Exception("Selected-child simultaneous output draining failed");
        return 0;
    }
    if (args[0] == "abandon")
    {
        Console.WriteLine(JsonSerializer.Serialize(store.AbandonAttempt(args[2], "process witness")));
        return 0;
    }
    if (args[0] == "dispose-lock")
    {
        // Exercise ordinary close-only parent disposal while its selected Git is alive.
        var attempt = store.GetAttempt(args[2]);
        WorktreeMaterialization.ClaimEnclosure(attempt);
        var enclosureLock = AdministrativeGitProcess.EnclosureLock.Acquire(Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.LockName));
        var selected = Task.Run(() => GitCustody.Run(attempt.B1.Repository,
            ["worktree", "add", "-b", attempt.Allocation.Branch, attempt.Allocation.WorktreePath, attempt.B1.CommitOid], enclosureLock: enclosureLock));
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(args[3]))
        {
            if (selected.IsCompleted || DateTime.UtcNow > until) throw new Exception("Selected Git did not reach gate");
            Thread.Sleep(10);
        }
        // A normal unrelated Process.Start must never inherit the selected lock.
        using var unrelated = Process.Start(new ProcessStartInfo("sleep", "20"))!;
        try
        {
            enclosureLock.Dispose();
            File.WriteAllText(args[3] + ".disposed", "disposed");
            var result = await selected;
            if (result.ExitCode != 0) throw new Exception(result.Error);
            File.WriteAllText(args[3] + ".finished", "git-finished");
            while (!File.Exists(args[4]) && DateTime.UtcNow < until) await Task.Delay(10);
        }
        finally
        {
            enclosureLock.Dispose();
            if (!unrelated.HasExited) unrelated.Kill();
            unrelated.WaitForExit();
        }
        return 0;
    }
    if (args.Length > 3) File.WriteAllText(args[3], "caller-started");
    var resultAttempt = store.ProvisionAttempt(args[2]);
    Console.WriteLine(JsonSerializer.Serialize(new {
        Attempt = resultAttempt,
        Head = GitCustody.Text(resultAttempt.Allocation.WorktreePath, "rev-parse", "HEAD").Trim(),
        Material = File.ReadAllText(Path.Combine(resultAttempt.Allocation.WorktreePath, "original.txt"))
    }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
    return 1;
}

internal static class Native
{
    [DllImport("libc")] internal static extern nint signal(int number, nint handler);
}

internal sealed class CrashTransport(INativeTransport inner, string mode) : INativeTransport
{
    internal static void Gate(string value)
    {
        Console.WriteLine(value);
        Console.Out.Flush();
        Thread.Sleep(Timeout.Infinite); // Parent SIGKILL, not managed unwinding, exercises each durable boundary.
    }
    public async Task<string> SubmitAsync(string request, IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken = default)
    {
        if (mode == "before-call") Gate("before-call");
        var id = await inner.SubmitAsync(request, credentials, cancellationToken);
        if (mode == "after-accept") Gate(id);
        return id;
    }
    public Task<NativeResult> WaitAsync(NativeRunBinding run, CancellationToken cancellationToken = default) => inner.WaitAsync(run, cancellationToken);
    public Task<NativeResult> StopAsync(NativeRunBinding run, CancellationToken cancellationToken = default) => inner.StopAsync(run, cancellationToken);
    public Task<NativeProgress> StatusAsync(NativeRunBinding run, TimeSpan bound, CancellationToken cancellationToken = default) => inner.StatusAsync(run, bound, cancellationToken);
}
