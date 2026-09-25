using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// The selected unmodified native HTTP/OECP boundary with the approved asset. Provider, forge and
/// PR receipt are controlled: this is not a real GitHub PR or semantic-quality result.
/// </summary>
public sealed class StockDirectTargetTests
{
    [Test]
    public async Task ControlledPullRequestStartsFromExactB1AndSurvivesTargetRestart()
    {
        using var fixture = new HttpFixture();
        await using var target = await StockDirectTarget.StartAsync(fixture.Git.State.Root);
        var b1 = fixture.Attempt.B1.CommitOid;
        await target.PushAsync(fixture.Git.Repository, "main");
        // The PR branch moves after B1 was admitted.
        var moved = fixture.Git.Commit("moved after admission\n");
        await target.PushAsync(fixture.Git.Repository, "main");
        var prepared = fixture.Prepare(target: target.Origin);
        var run = prepared.Frozen.Run(prepared.IntendedRunId!);

        await Assert.That(await SubmitAsync(target, prepared.RequestJson)).IsEqualTo(prepared.IntendedRunId);
        var result = await WaitAsync(run);
        await Assert.That(result.Succeeded).IsTrue();
        var receipt = result.Output;
        await Assert.That(string.Join(",", receipt.EnumerateObject().Select(field => $"{field.Name}={field.Value}").Order()))
            .IsEqualTo($"headRevision={receipt.GetProperty("headRevision")},mode=pr,outcome=opened,pullRequestId=1,"
                + "repository=acme/widget,targetBranch=main,version=v1");
        // The delivered commit descends from exact B1, not the moved branch tip.
        var head = receipt.GetProperty("headRevision").GetString()!;
        await Assert.That(AttemptFixture.RunGit(target.Forge, "rev-parse", head + "^").Trim()).IsEqualTo(b1);
        await Assert.That(AttemptFixture.RunGit(target.Forge, "rev-parse", "main").Trim()).IsEqualTo(moved);

        await target.RestartAsync();
        var retained = await StatusAsync(run);
        await Assert.That(retained.Result).IsNotNull();
        await Assert.That(retained.Result!.Succeeded).IsTrue();
        await Assert.That(retained.Result.Output.GetRawText()).IsEqualTo(receipt.GetRawText());
        // The same key and content converge on the existing run without starting another.
        await Assert.That(await SubmitAsync(target, prepared.RequestJson)).IsEqualTo(prepared.IntendedRunId);
    }

    /// <summary>Part A stand-in for the application's HTTP submit: the retained request plus ephemeral credentials.</summary>
    private static async Task<string> SubmitAsync(StockDirectTarget target, string requestJson)
    {
        var request = JsonNode.Parse(requestJson)!.AsObject();
        var credentials = StockDirectTarget.Credentials;
        request["connections"] = new JsonObject
        {
            ["gateway"] = new JsonObject
            {
                ["GATEWAY_BASE_URL"] = credentials["GATEWAY_BASE_URL"], ["GATEWAY_API_KEY"] = credentials["GATEWAY_API_KEY"]
            },
            ["github"] = new JsonObject { ["GH_TOKEN"] = credentials["GH_TOKEN"] }
        };
        request["githubToken"] = credentials["GH_TOKEN"];
        using var budget = DirectTargetBudget.Start(DirectTargetLimits.Submit, TimeProvider.System, default);
        using var http = DirectTargetExchange.CreateClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, target.Origin + "/native-v2/run")
        { Content = DirectTargetExchange.JsonContent(JsonSerializer.SerializeToUtf8Bytes(request)) };
        var (status, body) = await DirectTargetExchange.SendJsonAsync(http, message, budget);
        if (status != HttpStatusCode.OK) throw new InvalidOperationException($"Submission returned {status}");
        return body.GetProperty("runId").GetString()!;
    }

    private static async Task<DirectTargetRunStatus> StatusAsync(NativeRunBinding run)
    {
        using var budget = DirectTargetBudget.Start(DirectTargetLimits.Progress, TimeProvider.System, default);
        await using var session = await DirectTargetSession.OpenAsync(run, budget);
        return await session.StatusAsync(budget);
    }

    /// <summary>Part A stand-in for the application's HTTP wait.</summary>
    private static async Task<NativeResult> WaitAsync(NativeRunBinding run)
    {
        for (var poll = 0; poll < 90; poll++)
        {
            if ((await StatusAsync(run)).Result is { } result) return result;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        throw new TimeoutException("The controlled run did not finish.");
    }
}
