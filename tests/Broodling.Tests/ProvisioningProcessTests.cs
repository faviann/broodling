using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class ProvisioningProcessTests
{
    [Test]
    public async Task IndependentCallersSeeSettledGitAndObservationDoesNotTakeProvisioningWriter()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        using var held = new HeldGit(fixture);
        using var first = held.Start("provision", attempt, hold: true);
        await WaitForFile(held.Entered, first);
        using (var reader = fixture.State.Open())
        {
            var connection = (SqliteConnection)typeof(BroodlingStore).GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;
            using var configure = connection.CreateCommand();
            configure.CommandText = "PRAGMA busy_timeout=0; PRAGMA query_only=ON";
            configure.ExecuteNonQuery();
            await Assert.That(reader.Status(fixture.RevisionId).Attempts.Single().Provision).IsNull();
            await Assert.That(reader.History(ContractIngressTests.Reference).Single().Attempts.Single()).IsEqualTo(attempt);
        }
        using (var contender = fixture.State.Connect())
        {
            contender.DefaultTimeout = 1;
            await Assert.That(() => contender.BeginTransaction(deferred: false)).Throws<SqliteException>();
        }
        var started = held.Entered + ".second";
        using var second = held.Start("provision", attempt, extra: [started]);
        await WaitForFile(started, second);
        await Task.Delay(150);
        await Assert.That(second.Process.HasExited).IsFalse();
        held.Release();
        await first.Succeed();
        await second.Succeed();
        using var one = JsonDocument.Parse(await first.Output);
        using var two = JsonDocument.Parse(await second.Output);
        await Assert.That(one.RootElement.GetRawText()).IsEqualTo(two.RootElement.GetRawText());
        await Assert.That(one.RootElement.GetProperty("Head").GetString()).IsEqualTo(fixture.Head);
        await Assert.That(one.RootElement.GetProperty("Material").GetString()).IsEqualTo("original selected bytes\n");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrphanGitRetainsExclusionAndAbandonmentCannotAcknowledgeStaleAuthority(bool abandon)
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        using var held = new HeldGit(fixture);
        using var caller = held.Start("provision", attempt, hold: true);
        await WaitForFile(held.Entered, caller);
        caller.Process.Kill(); // Deliberately leave its selected Git alive.
        await caller.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(LockIsFree(attempt)).IsFalse();
        var started = held.Entered + ".replay";
        using var replay = held.Start("provision", attempt, extra: [started]);
        await WaitForFile(started, replay);
        await Task.Delay(150);
        await Assert.That(replay.Process.HasExited).IsFalse();
        if (abandon) store.AbandonAttempt(attempt.AttemptId, "authority ended while replay waited for orphan Git");
        held.Release();
        if (abandon)
        {
            await replay.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(replay.Process.ExitCode).IsEqualTo(1);
            await Assert.That((await replay.Error).Contains(nameof(StaleAttempt))).IsTrue();
        }
        else await replay.Succeed();
        await WaitUntil(() => LockIsFree(attempt));
        await Assert.That(AttemptFixture.RunGit(attempt.Allocation.WorktreePath, "rev-parse", "HEAD").Trim()).IsEqualTo(fixture.Head);
        using var reopened = fixture.State.Open();
        await Assert.That(reopened.GetAttempt(attempt.AttemptId).Provision is not null).IsEqualTo(!abandon);
        await Assert.That(reopened.Status(fixture.RevisionId).Attempts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task KillingCallerAfterRealGitCompletionBeforeAcknowledgmentReplaysSameAttempt()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        using var held = new HeldGit(fixture, after: true);
        using var caller = held.Start("provision", attempt, hold: true);
        await WaitForFile(held.Entered, caller);
        await Assert.That(File.Exists(System.IO.Path.Combine(attempt.Allocation.WorktreePath, "original.txt"))).IsTrue();
        caller.Process.Kill();
        await caller.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(store.GetAttempt(attempt.AttemptId).Provision).IsNull();
        held.Release();
        using var replay = held.Start("provision", attempt);
        await replay.Succeed();
        await Assert.That(store.GetAttempt(attempt.AttemptId).Provision).IsNotNull();
        await Assert.That(store.Status(fixture.RevisionId).Attempts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task OrdinaryDisposalRetainsSelectedGitLockButUnrelatedProcessDoesNotDelayRelease()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        using var held = new HeldGit(fixture);
        var finish = held.Entered + ".finish-host";
        using var caller = held.Start("dispose-lock", attempt, hold: true, extra: [held.Entered, finish]);
        try
        {
            await WaitForFile(held.Entered + ".disposed", caller);
            await Assert.That(LockIsFree(attempt)).IsFalse();
            held.Release();
            await WaitForFile(held.Entered + ".finished", caller);
            await Assert.That(caller.Process.HasExited).IsFalse(); // unrelated sleep is still alive
            await Assert.That(LockIsFree(attempt)).IsTrue();
        }
        finally { File.WriteAllText(finish, "finish"); }
        await caller.Succeed();
        await Assert.That(store.ProvisionAttempt(attempt.AttemptId).Provision).IsNotNull();
    }

    [Test]
    public async Task IgnoredSigchldRefusesBeforeEnclosureMutation()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        using var held = new HeldGit(fixture);
        using var caller = held.Start("ignored-sigchld", attempt);
        await caller.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(caller.Process.ExitCode).IsEqualTo(1);
        await Assert.That((await caller.Error).Contains("waitable children")).IsTrue();
        await Assert.That(Directory.Exists(attempt.Allocation.Enclosure)).IsFalse();
    }

    [Test]
    public async Task SelectedSpawnFailureClosesPipesAndLockAndSignalExitIsNotSuccess()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        WorktreeMaterialization.ClaimEnclosure(attempt);
        using (var held = AdministrativeGitProcess.EnclosureLock.Acquire(System.IO.Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.LockName)))
        {
            var missing = new ProcessStartInfo(System.IO.Path.Combine(fixture.State.Root, "does-not-exist"));
            await Assert.That(() => AdministrativeGitProcess.Run(missing, held)).Throws<WorktreeProvisioningError>();
            var signaled = new ProcessStartInfo("/bin/sh");
            signaled.ArgumentList.Add("-c");
            signaled.ArgumentList.Add("kill -TERM $$");
            await Assert.That(() => AdministrativeGitProcess.Run(signaled, held)).Throws<WorktreeProvisioningError>();
            await Assert.That(LockIsFree(attempt)).IsFalse();
        }
        await WaitUntil(() => LockIsFree(attempt));
        await Assert.That(store.ProvisionAttempt(attempt.AttemptId).Provision).IsNotNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GitNulRefusalPrecedesSpawnAndPreservesEnclosureLock(bool inEnvironment)
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        WorktreeMaterialization.ClaimEnclosure(attempt);
        using (var held = AdministrativeGitProcess.EnclosureLock.Acquire(System.IO.Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.LockName)))
        {
            var marker = System.IO.Path.Combine(fixture.State.Root, "unexpected-spawn");
            var start = new ProcessStartInfo("/usr/bin/touch") { WorkingDirectory = fixture.State.Root };
            start.ArgumentList.Add(marker);
            if (inEnvironment) start.Environment["BROODLING_TEST_VALUE"] = "before\0after";
            else start.ArgumentList.Add("before\0after");
            var error = Assert.Throws<WorktreeProvisioningError>(() => AdministrativeGitProcess.Run(start, held));
            await Assert.That(error!.Code).IsEqualTo("worktree_provisioning_error");
            await Assert.That(error.Message).IsEqualTo("Git arguments/environment cannot contain NUL.");
            await Assert.That(File.Exists(marker)).IsFalse();
            await Assert.That(LockIsFree(attempt)).IsFalse();
        }
        await Assert.That(LockIsFree(attempt)).IsTrue();
        await Assert.That(store.ProvisionAttempt(attempt.AttemptId).Provision).IsNotNull();
    }

    [Test]
    public async Task SelectedSpawnFailureClosesDescriptorsAndBothOutputPipesDrainPastCapacity()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        using var held = new HeldGit(fixture);
        using var caller = held.Start("pipe-cleanup", attempt);
        await caller.Succeed();
        await Assert.That(LockIsFree(attempt)).IsTrue();
    }

    internal static bool LockIsFree(AttemptRecord attempt)
    {
        var fd = open(System.IO.Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.LockName), 0x80002 /* RDWR | CLOEXEC */);
        if (fd < 0) throw new Exception("Cannot open witness lock");
        try
        {
            if (flock(fd, 6 /* EX | NB */) == 0) return true;
            if (Marshal.GetLastPInvokeError() == 11) return false;
            throw new Exception("Unexpected witness flock error");
        }
        finally { close(fd); }
    }

    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int flock(int fd, int flags);
    [DllImport("libc")] private static extern int close(int fd);

    internal static async Task WaitForFile(string path, Caller caller) => await WaitUntil(() =>
    {
        if (File.Exists(path)) return true;
        if (caller.Process.HasExited) throw new Exception("Caller exited before gate: " + caller.Error.GetAwaiter().GetResult());
        return false;
    });
    internal static async Task WaitUntil(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Local process witness did not settle");
            await Task.Delay(10);
        }
    }

    internal sealed class HeldGit : IDisposable
    {
        private readonly AttemptFixture fixture;
        private readonly bool after;
        private readonly string bin;
        private readonly string git;
        internal string Entered { get; }
        private string Gate { get; }
        internal HeldGit(AttemptFixture fixture, bool after = false)
        {
            this.fixture = fixture;
            this.after = after;
            bin = Directory.CreateDirectory(System.IO.Path.Combine(fixture.State.Root, "bin")).FullName;
            git = Environment.GetEnvironmentVariable("PATH")!.Split(':').Select(path => System.IO.Path.Combine(path, "git")).First(File.Exists);
            var wrapper = System.IO.Path.Combine(bin, "git");
            File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "held-git.sh"), wrapper);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Entered = System.IO.Path.Combine(fixture.State.Root, "entered");
            Gate = System.IO.Path.Combine(fixture.State.Root, "gate");
        }
        internal Caller Start(string mode, AttemptRecord attempt, bool hold = false, string[]? extra = null)
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { System.IO.Path.Combine(AppContext.BaseDirectory, "Broodling.ProcessWitness.dll"), mode, fixture.State.Path, attempt.AttemptId }.Concat(extra ?? []))
                start.ArgumentList.Add(arg);
            if (hold)
            {
                start.Environment["PATH"] = bin + ":" + start.Environment["PATH"];
                start.Environment["BROODLING_WITNESS_REAL_GIT"] = git;
                start.Environment["BROODLING_WITNESS_ENTERED"] = Entered;
                start.Environment["BROODLING_WITNESS_GATE"] = Gate;
                start.Environment["BROODLING_WITNESS_MODE"] = after ? "after" : "before";
                start.Environment["BROODLING_WITNESS_OPERATION"] = mode == "retire" ? "remove" : "add";
            }
            return new(start);
        }
        internal void Release() => File.WriteAllText(Gate, "go");
        public void Dispose() => Release();
    }

    internal sealed class Caller : IDisposable
    {
        internal Process Process { get; }
        internal Task<string> Output { get; }
        internal Task<string> Error { get; }
        internal Caller(ProcessStartInfo start)
        {
            Process = Process.Start(start)!;
            Output = Process.StandardOutput.ReadToEndAsync();
            Error = Process.StandardError.ReadToEndAsync();
        }
        internal async Task Succeed()
        {
            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));
            if (Process.ExitCode != 0) throw new Exception("Local caller failed: " + await Error);
        }
        public void Dispose()
        {
            if (!Process.HasExited) { Process.Kill(); Process.WaitForExit(); }
            Process.Dispose();
        }
    }
}
