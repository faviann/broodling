using System.Diagnostics;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class DispatchProcessTests
{
    [Test]
    [Arguments("during-prepare", null)]
    [Arguments("prepared", "prepared")]
    [Arguments("before-call", "dispatched")]
    [Arguments("after-accept", "dispatched")]
    [Arguments("during-correlation", "dispatched")]
    [Arguments("after-correlation", "correlated")]
    public async Task KilledCallerRetainsOnlyCommittedFactsAndReplayConvergesOnTheSameNativeRun(string mode, string? expectedState)
    {
        using var fixture = new NativeFixture();
        AttemptRecord attempt;
        using (var store = fixture.Git.State.Open()) attempt = fixture.Provision(store);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine(AppContext.BaseDirectory, "Broodling.ProcessWitness.dll"),
            "native-crash", fixture.Git.State.Path, attempt.AttemptId, fixture.NativeState,
            fixture.Codex.RealCodex, fixture.Home, fixture.CodexHome, NativeFixture.Launcher, mode, NativeFixture.Python })
            start.ArgumentList.Add(argument);
        using var caller = Process.Start(start)!;
        var error = caller.StandardError.ReadToEndAsync();
        string? observed;
        try
        {
            observed = await caller.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(40));
            if (observed is null) throw new Exception("Crash witness failed before gate: " + await error);
        }
        finally
        {
            if (!caller.HasExited) caller.Kill(entireProcessTree: false);
            await caller.WaitForExitAsync();
        }
        await Assert.That(caller.ExitCode).IsNotEqualTo(0);
        using var reopened = fixture.Git.State.Open();
        var retained = reopened.FindSubmission(attempt.AttemptId);
        await Assert.That(retained?.State).IsEqualTo(expectedState);
        var correlated = await reopened.DispatchAsync(attempt.AttemptId, fixture.Profile, NativeFixture.Transport());
        if (retained is not null)
        {
            await Assert.That(correlated.RequestJson).IsEqualTo(retained.RequestJson);
            await Assert.That(correlated.SubmissionKey).IsEqualTo(retained.SubmissionKey);
        }
        if (mode is "after-accept" or "after-correlation") await Assert.That(correlated.RunId).IsEqualTo(observed);
        var terminal = await NativeFixture.Transport().WaitAsync(correlated.Locator, correlated.RunId!);
        await Assert.That(terminal.Succeeded).IsTrue();
        using var again = fixture.Git.State.Open();
        await Assert.That(again.FindSubmission(attempt.AttemptId)).IsEqualTo(correlated);
    }
}
