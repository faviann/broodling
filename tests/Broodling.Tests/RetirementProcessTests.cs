using System.Diagnostics;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class RetirementProcessTests
{
    [Test]
    public async Task InterruptedHttpReplacementAllocationAndPreparationConvergeOnOneOriginalB1Successor()
    {
        using var fixture = new HttpFixture();
        var store = fixture.Store;
        var predecessor = fixture.Attempt;
        // A prepared, never-dispatched predecessor retires safely and keeps the origin its successor targets.
        fixture.Prepare();
        await ReplacementTests.SafeRetire(store, predecessor);
        foreach (var mode in new[] { "allocation-write", "allocated", "prepare-write", "prepared" })
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { Path.Combine(AppContext.BaseDirectory, "Broodling.ProcessWitness.dll"),
                "replacement-crash", fixture.Git.State.Path, predecessor.AttemptId, mode })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(25));
                if (line != mode) throw new Exception("Replacement caller missed crash boundary: " + line + (process.HasExited ? await error : ""));
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
        var prepared = store.PrepareRetry(predecessor.AttemptId, "process-retry");
        await Assert.That(store.PrepareRetry(predecessor.AttemptId, "process-retry")).IsEqualTo(prepared);
        await Assert.That((string)JsonNode.Parse(prepared.RequestJson)!["submission"]!["source"]!["revision"]!).IsEqualTo(predecessor.B1.CommitOid);
        await Assert.That(prepared.State).IsEqualTo("prepared");
        await Assert.That(store.Status(predecessor.ContractRevisionId).Attempts.Count).IsEqualTo(2);
    }

}
