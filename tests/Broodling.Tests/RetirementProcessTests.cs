using System.Diagnostics;
using TUnit.Assertions;
using TUnit.Core;
using static Broodling.Tests.ProvisioningProcessTests;

namespace Broodling.Tests;

public sealed class RetirementProcessTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SurvivingRetirementGitBlocksFollowerAndRetryUntilRemovalAndAcknowledgment(bool afterRemoval)
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        await store.StopAsync(attempt.AttemptId, "retirement crash", new ControlledTransport());
        using var held = new HeldGit(fixture.Git, after: afterRemoval);
        using var caller = held.Start("retire", attempt, hold: true);
        await WaitForFile(held.Entered, caller);
        await Assert.That(Directory.Exists(attempt.Allocation.WorktreePath)).IsEqualTo(!afterRemoval);
        caller.Process.Kill(); // Kill only C# caller; its selected administrative Git survives with the inherited flock.
        await caller.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(LockIsFree(attempt)).IsFalse();
        await Assert.That(store.FindRetirement(attempt.AttemptId)!.RetiredAt).IsNull();
        await Assert.That(() => store.AdmitRetry(attempt.AttemptId, "too-early", fixture.Git.Workspaces, fixture.Profile)).Throws<AttemptAdmissionError>();
        var started = held.Entered + ".follower";
        using var follower = held.Start("retire", attempt, extra: [started]);
        await WaitForFile(started, follower);
        await Task.Delay(150);
        await Assert.That(follower.Process.HasExited).IsFalse();
        await Assert.That(store.FindRetirement(attempt.AttemptId)!.RetiredAt).IsNull();
        using (var reader = fixture.Git.State.Open())
        {
            using var queryOnly = ReplacementTests.Connection(reader).CreateCommand();
            queryOnly.CommandText = "PRAGMA busy_timeout=0; PRAGMA query_only=ON"; queryOnly.ExecuteNonQuery();
            await Assert.That(reader.History(ContractIngressTests.Reference).Single().Attempts.Single().Retirement!.RetiredAt).IsNull();
        }
        held.Release();
        await follower.Succeed();
        await WaitUntil(() => LockIsFree(attempt));
        await Assert.That(Directory.Exists(attempt.Allocation.WorktreePath)).IsFalse();
        var retired = store.FindRetirement(attempt.AttemptId)!;
        await Assert.That(retired.RetiredAt).IsNotNull();
        await Assert.That(store.RetireAttempt(attempt.AttemptId)).IsEqualTo(retired);
        var replacement = store.AdmitRetry(attempt.AttemptId, "after-retirement", fixture.Git.Workspaces, fixture.Profile);
        await Assert.That(replacement.IsCurrent).IsTrue();
        await Assert.That(() => store.ProvisionAttempt(attempt.AttemptId)).Throws<StaleAttempt>();
        await Assert.That(() => fixture.Git.Admit(store)).Throws<StaleAttempt>();
    }

    [Test]
    public async Task InterruptedReplacementAllocationAndPreparationConvergeOnOneOriginalB1Successor()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var predecessor = fixture.Git.Admit(store);
        await ReplacementTests.SafeRetire(store, predecessor);
        foreach (var mode in new[] { "allocation-write", "allocated", "prepare-write", "prepared" })
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { Path.Combine(AppContext.BaseDirectory, "Broodling.ProcessWitness.dll"),
                "retry-crash", fixture.Git.State.Path, predecessor.AttemptId, mode, fixture.Git.Workspaces,
                fixture.NativeState, Path.Combine(fixture.Root, "provider"), fixture.Home, fixture.CodexHome, NativeFixture.Launcher })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(25));
                if (line != mode) throw new Exception("Retry caller missed crash boundary: " + line + (process.HasExited ? await error : ""));
                process.Kill();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
            var retry = store.FindRetry("process-retry");
            if (mode == "allocation-write")
            {
                await Assert.That(retry).IsNull();
                await Assert.That(store.Status(predecessor.ContractRevisionId).Attempts.Count).IsEqualTo(1);
            }
            else
            {
                await Assert.That(retry).IsNotNull();
                await Assert.That(store.Status(predecessor.ContractRevisionId).Attempts.Count).IsEqualTo(2);
                if (mode == "prepare-write") await Assert.That(store.FindSubmission(retry!.AttemptId)).IsNull();
            }
        }
        fixture.Git.Commit("later source tip\n");
        var prepared = store.PrepareRetry(predecessor.AttemptId, "process-retry", fixture.Git.Workspaces, fixture.Profile);
        await Assert.That(store.PrepareRetry(predecessor.AttemptId, "process-retry", fixture.Git.Workspaces, fixture.Profile)).IsEqualTo(prepared);
        await Assert.That(AttemptFixture.RunGit(store.GetAttempt(prepared.AttemptId).Allocation.WorktreePath, "rev-parse", "HEAD").Trim()).IsEqualTo(predecessor.B1.CommitOid);
        await Assert.That(store.Status(predecessor.ContractRevisionId).Attempts.Count).IsEqualTo(2);
    }
}
