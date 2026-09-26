using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Broodling.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;
using ControlledGateway = Broodling.Tests.BundledProposerTests.ControlledGateway;
using Gateway = Broodling.Tests.IssueSubmissionPreparationTests.Gateway;
using PreparationFixture = Broodling.Tests.RepositoryPreparationTests.RepositoryPreparationFixture;

namespace Broodling.Tests;

/// <summary>
/// The processing server: submit, resume and stop over HTTP, with the host's own attachment of automatic
/// progression and completion observation, over real capture, Git and SQLite with controlled GitHub and gateway
/// peers and the loopback stock-target stand-in. Preparation, progression, observation and stop semantics belong
/// to their owners' tests; these cover the HTTP mapping, the composition and its lifetime.
/// </summary>
public sealed class HttpProcessingTests
{
    private const string Proposal = """{"criteria": [{"criterionId": "export", "statement": "Export the data."}]}""";

    [Test]
    public async Task AcceptedWorkIsProcessedAndItsResultRetainedWithNoClientConnected()
    {
        await using var target = new StockTarget();
        using var fixture = new PreparationFixture();
        using (fixture.State.Initialize()) { }
        fixture.SetIssue(12, RequestAdmissionTests.Request());
        fixture.SetIssue(13, "## Request\n<!-- broodling-request:v1 -->\nAdd JSON export.\n");
        using var store = fixture.State.Open();
        using var entered = new SemaphoreSlim(0);
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var proposing = false;
        var gateway = new Gateway((_, _) =>
        {
            entered.Release();
            return Volatile.Read(ref proposing) ? Task.FromResult(ControlledGateway.Final(Proposal)) : held.Task;
        });
        await using var server = await Server.Start(fixture.State, fixture.RepositoryRoot, target, Peers(fixture, gateway));
        const string issue = "https://github.com/acme/widget/issues/12";

        // A reference that identifies no supported Work Unit is refused before anything is retained.
        var invalid = await server.Client.PostAsJsonAsync("/submissions", new { issueUrl = "https://github.com/acme/widget/pull/12" });
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(store.FindIssueSubmission(issue)).IsNull();

        // Acceptance answers from the committed record before anything is acquired; the client then leaves.
        string submissionId;
        using (var caller = server.NewClient())
        {
            // Nothing progresses until the test advances the scans, so an acknowledgement that waited would not arrive.
            var accepted = await caller.PostAsJsonAsync("/submissions", new { issueUrl = issue }).WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
            submissionId = store.FindIssueSubmission(issue)!.SubmissionId;
            await Assert.That(accepted.Headers.Location!.OriginalString).IsEqualTo($"/submissions/{submissionId}");
            var body = await Body(accepted);
            await Assert.That((string)body["submission"]!["submissionId"]!).IsEqualTo(submissionId);
            await Assert.That((string)body["submission"]!["state"]!).IsEqualTo("accepted");
            await Assert.That(fixture.ReadGhPaths()).IsEmpty();
        }
        await server.Until(() => entered.CurrentCount > 0);
        // Repeating the submission while its preparation is held returns the same work without waiting on it.
        var repeated = await server.Client.PostAsJsonAsync("/submissions", new { issueUrl = issue });
        await Assert.That(repeated.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(repeated.Headers.Location!.OriginalString).IsEqualTo($"/submissions/{submissionId}");

        // A refusal only the operator can resolve stops the submission and its reason is readable.
        held.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") });
        JsonNode read = new JsonObject();
        await server.Until(() => (read = server.Get($"/submissions/{submissionId}"))["progression"]?["state"]?.GetValue<string>() == "stopped");
        await Assert.That((string)read["progression"]!["code"]!).IsEqualTo("contract_proposer_error");
        // The failure message stays in the server log; it can carry process or path detail.
        await Assert.That(read["progression"]!.AsObject().ContainsKey("message")).IsFalse();
        await Assert.That(read["submission"]!["contractRevisionId"]).IsNull();
        await Assert.That(read["observation"]).IsNull();

        // Resume continues that exact submission through the one progression owner.
        Volatile.Write(ref proposing, true);
        var resumed = await server.Client.PostAsync($"/submissions/{submissionId}/resume", null);
        await Assert.That(resumed.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(resumed.Headers.Location!.OriginalString).IsEqualTo($"/submissions/{submissionId}");
        await server.Until(() => store.GetIssueSubmission(submissionId).AttemptIds is [var attempt]
            && store.FindSubmission(attempt) is { State: "correlated" });
        var attemptId = store.GetIssueSubmission(submissionId).AttemptIds.Single();
        var native = store.FindSubmission(attemptId)!;

        // Retained facts and the bounded native observation are reported separately; an unreachable target makes
        // only the observation unavailable.
        target.Reply = Status(() => DirectTargetSessionTests.Running(native.Run!));
        read = server.Get($"/submissions/{submissionId}");
        await Assert.That((string)read["submission"]!["state"]!).IsEqualTo("admitted");
        await Assert.That((bool)read["admission"]!["admitted"]!).IsTrue();
        await Assert.That(read["progression"]).IsNull();
        await Assert.That((string)read["observation"]!["attemptId"]!).IsEqualTo(attemptId);
        await Assert.That((string)read["observation"]!["availability"]!).IsEqualTo("available");
        await Assert.That((string)read["observation"]!["runIdentity"]!).IsEqualTo("confirmed");
        await Assert.That((string)read["observation"]!["phase"]!).IsEqualTo("running");
        target.Session = (503, """{"code":"target.unavailable","message":"down"}""");
        var unavailable = server.Get($"/submissions/{submissionId}");
        await Assert.That((string)unavailable["observation"]!["availability"]!).IsEqualTo("unavailable");
        await Assert.That(JsonNode.DeepEquals(unavailable["submission"], read["submission"])).IsTrue();
        target.Session = null;

        // The host's observer retains the result unasked.
        var acceptedCommit = AttemptFixture.RunGit(fixture.ServiceRepository, "commit-tree",
            native.Frozen.Source!.Revision + "^{tree}", "-p", native.Frozen.Source.Revision, "-m", "accepted").Trim();
        target.Reply = Status(() => AttemptCompletionTests.HttpFinished(native, "succeeded", CompletionFixture.Receipt(head: acceptedCommit)));
        await server.Until(() => store.FindCompletion(attemptId) is not null);
        var retained = server.Get($"/attempts/{attemptId}");
        await Assert.That((string)retained["completion"]!["acceptedRevision"]!).IsEqualTo(acceptedCommit);
        await Assert.That(retained["observation"]).IsNull();

        // Shutdown detaches work in flight: its dispatch stays unresolved for exact replay and nothing is stopped.
        using var sent = new SemaphoreSlim(0);
        target.Submit = _ =>
        {
            sent.Release();
            return new TaskCompletionSource<(int, string)>().Task;
        };
        await server.Client.PostAsJsonAsync("/submissions", new { issueUrl = "https://github.com/acme/widget/issues/13" });
        await server.Until(() => sent.CurrentCount > 0);
        var inFlight = store.FindIssueSubmission("https://github.com/acme/widget/issues/13")!.AttemptIds.Single();
        await server.App.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(server.Processing.ExecuteTask!.IsCompletedSuccessfully).IsTrue();
        await Assert.That(store.RequireCurrentAttempt(inFlight).Abandonment).IsNull();
        await Assert.That(store.FindSubmission(inFlight)!.State).IsEqualTo("dispatched");
        await Assert.That(target.Count("run/force")).IsEqualTo(0);
    }

    [Test]
    public async Task StopsAreBoundToExactWorkAndOutliveTheirCaller()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var native = fixture.PrepareAt(target.Origin, "correlated");
        var attemptId = fixture.Attempt.AttemptId;
        // A submission for the same Contract, whose cancellation is bound to that Attempt.
        var bound = fixture.Store.SubmitIssue("https://github.com/acme/widget/issues/12").SubmissionId;
        fixture.Store.AssociateIssueSubmission(bound, fixture.Attempt.ContractRevisionId);
        var discovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        target.Discovery = () => discovery.Task;
        target.Reply = (request, id) => (string)request["method"]! switch
        {
            "run/status" => Rpc(id, DirectTargetSessionTests.Running(native.Run!)),
            "run/force" => Rpc(id, DirectTargetSessionTests.Projection(new JsonObject
            {
                ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "failed", ["reason"] = "force_stopped" }
            }, native.Run!)),
            _ => null
        };
        var repositories = Directory.CreateDirectory(Path.Combine(fixture.Git.State.Root, "service-repositories")).FullName;
        await using var server = await Server.Start(fixture.Git.State, repositories, target, new(Credentials, new FakeTimeProvider(),
            new GitHubIssueSource("/nonexistent/gh"), new GitHubRepositorySource("/nonexistent/gh"), null));

        // A stop names its reason; without one nothing changes.
        var unexplained = await server.Client.PostAsJsonAsync($"/attempts/{attemptId}/stop", new { reason = " " });
        await Assert.That(unexplained.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(fixture.Store.GetAttempt(attemptId).Abandonment).IsNull();

        // The caller disconnects after abandonment commits, while the native stop waits on the target.
        using (var caller = new CancellationTokenSource())
        {
            var stop = server.Client.PostAsJsonAsync($"/attempts/{attemptId}/stop", new { reason = "operator stop" }, caller.Token);
            await ProvisioningProcessTests.WaitUntil(() => fixture.Store.GetAttempt(attemptId).Abandonment is not null);
            await caller.CancelAsync();
            await Assert.That(async () => await stop).Throws<TaskCanceledException>();
            await Task.Delay(500);
        }
        discovery.SetResult();
        await ProvisioningProcessTests.WaitUntil(() => target.Count("run/force") == 1);
        await Assert.That((string)target.Messages.Last(message => (string)message["method"]! == "run/force")["params"]!["runId"]!)
            .IsEqualTo(native.RunId);

        // The response tells committed abandonment apart from unconfirmed cessation, and replay keeps the first reason.
        var replay = await server.Client.PostAsJsonAsync($"/attempts/{attemptId}/stop", new { reason = "again" });
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var report = await Body(replay);
        await Assert.That((string)report["attempt"]!["abandonment"]!["reason"]!).IsEqualTo("operator stop");
        await Assert.That((string)report["error"]!).IsEqualTo("cessation_unconfirmed");
        await Assert.That((bool)report["quarantined"]!).IsTrue();

        // Cancelling the submission binds and stops that exact Attempt; the committed cancellation answers 200 with
        // the Attempt's stop report even though native cessation stays unconfirmed.
        var withAttempt = await server.Client.PostAsJsonAsync($"/submissions/{bound}/stop", new { reason = "withdrawn" });
        await Assert.That(withAttempt.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var boundStop = await Body(withAttempt);
        await Assert.That((string)boundStop["submission"]!["cancellation"]!["attemptId"]!).IsEqualTo(attemptId);
        await Assert.That((string)boundStop["attempt"]!["attempt"]!["attemptId"]!).IsEqualTo(attemptId);
        await Assert.That((string)boundStop["attempt"]!["attempt"]!["abandonment"]!["reason"]!).IsEqualTo("operator stop");
        await Assert.That((string)boundStop["error"]!).IsEqualTo("cessation_unconfirmed");

        // A submission with no Attempt is cancelled; resuming it hands back that retained fact.
        var pending = fixture.Store.SubmitIssue("https://github.com/acme/widget/issues/77").SubmissionId;
        var cancelled = await server.Client.PostAsJsonAsync($"/submissions/{pending}/stop", new { reason = "withdrawn" });
        await Assert.That(cancelled.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var cancellation = await Body(cancelled);
        await Assert.That((string)cancellation["submission"]!["state"]!).IsEqualTo("cancelled");
        await Assert.That((string)cancellation["submission"]!["cancellation"]!["reason"]!).IsEqualTo("withdrawn");
        await Assert.That(cancellation["attempt"]).IsNull();
        var handedBack = await server.Client.PostAsync($"/submissions/{pending}/resume", null);
        await Assert.That(handedBack.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((bool)(await Body(handedBack))["resumed"]!).IsFalse();
        await Assert.That(target.Count("run/force")).IsEqualTo(3);
    }

    [Test]
    public async Task OnlyAServerThatCanProcessWorkAcceptsIt()
    {
        await using var target = new StockTarget();
        using var fixture = new StoreFixture();
        using (fixture.Initialize()) { }
        var repositories = Directory.CreateDirectory(Path.Combine(fixture.Root, "service-repositories")).FullName;
        var direct = Path.Combine(fixture.Root, "direct.json");
        File.WriteAllText(direct, new JsonObject { ["target"] = "direct", ["directOrigin"] = Origin(target) }.ToJsonString());
        var local = Path.Combine(fixture.Root, "local.json");
        File.WriteAllText(local, new JsonObject
        {
            ["target"] = "local", ["pythonExecutable"] = "/p", ["stateDirectory"] = "/s", ["workspaceRoot"] = "/w",
            ["realCodex"] = "/c", ["profileHome"] = "/h", ["codexHome"] = "/x", ["launcher"] = "/l"
        }.ToJsonString());
        ProcessingPeers Peers(Func<ProgressionCredentials> credentials) => new(credentials, new FakeTimeProvider(),
            new GitHubIssueSource("/nonexistent/gh"), new GitHubRepositorySource("/nonexistent/gh"), null);
        var store = "--Broodling:Store=" + fixture.Path;
        foreach (var (arguments, credentials, code) in new (string[], Func<ProgressionCredentials>, string)[]
        {
            ([store, "--Broodling:RepositoryRoot=" + repositories], Credentials, "unsupported_runtime"),
            ([store, "--Broodling:Invocation=" + local, "--Broodling:RepositoryRoot=" + repositories], Credentials, "unsupported_runtime"),
            ([store, "--Broodling:Invocation=" + direct, "--Broodling:RepositoryRoot=" + Path.Combine(fixture.Root, "missing")], Credentials, "unsupported_runtime"),
            ([store, "--Broodling:Invocation=" + direct, "--Broodling:RepositoryRoot=" + repositories],
                () => Credentials() with { Gateway = new GatewayCredentials(NativeProfile.GatewayBaseUrl, null) }, "contract_proposer_error")
        })
        {
            var refused = await Assert.That(() => BroodlingHost.Build(arguments, Peers(credentials))).Throws<BroodlingException>();
            await Assert.That(refused!.Code).IsEqualTo(code);
        }

        // Without processing configuration the server only reads, and refuses to accept work.
        var (app, client) = await HttpReadTests.Start(fixture.Path);
        await using var _ = app;
        using var __ = client;
        var refusal = await client.PostAsJsonAsync("/submissions", new { issueUrl = "https://github.com/acme/widget/issues/12" });
        await Assert.That(refusal.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That((string)(await Body(refusal))["error"]!).IsEqualTo("processing_not_configured");
        using var reopened = fixture.Open();
        await Assert.That(reopened.FindIssueSubmission("https://github.com/acme/widget/issues/12")).IsNull();
        await Assert.That(app.Services.GetService<Processing>()).IsNull();
    }

    [Test]
    public async Task AServiceThatFailsStopsTheServer()
    {
        await using var target = new StockTarget();
        using var fixture = new StoreFixture();
        using (fixture.Initialize()) { }
        var repositories = Directory.CreateDirectory(Path.Combine(fixture.Root, "service-repositories")).FullName;
        await using var server = await Server.Start(fixture, repositories, target, new(Credentials, new BrokenClock(),
            new GitHubIssueSource("/nonexistent/gh"), new GitHubRepositorySource("/nonexistent/gh"), null), started: false);

        await server.App.WaitForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(server.Processing.Failed).IsTrue();
    }

    private static ProgressionCredentials Credentials() => new(new GitHubRepositoryCredentials("configured-token"),
        new GatewayCredentials(NativeProfile.GatewayBaseUrl, "gateway-key"), HttpDispatchTests.Credentials());

    private static ProcessingPeers Peers(PreparationFixture fixture, HttpMessageHandler gateway) =>
        new(Credentials, new FakeTimeProvider(), new GitHubIssueSource(fixture.Gh), fixture.Source, gateway);

    private static string Origin(StockTarget target) => target.Origin.GetLeftPart(UriPartial.Authority);

    private static Func<JsonObject, string, string?> Status(Func<JsonObject> projection) => (request, id) =>
        (string)request["method"]! == "run/status" ? Rpc(id, projection()) : null;

    private static string Rpc(string id, JsonObject result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();

    private static async Task<JsonNode> Body(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    /// <summary>A clock whose scan timer cannot be created, so both services fail unexpectedly after their first discovery.</summary>
    private sealed class BrokenClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new InvalidOperationException("The scan timer is unavailable.");
    }

    /// <summary>The processing server over a store, configured for the stand-in target, whose scans advance only when a test says so.</summary>
    private sealed class Server : IAsyncDisposable
    {
        private readonly TimeProvider clock;
        internal WebApplication App { get; }
        internal HttpClient Client { get; }
        internal Processing Processing { get; }

        private Server(WebApplication app, TimeProvider clock)
        {
            App = app;
            this.clock = clock;
            Client = NewClient();
            Processing = app.Services.GetRequiredService<Processing>();
        }

        internal static async Task<Server> Start(StoreFixture state, string repositoryRoot, StockTarget target, ProcessingPeers peers,
            bool started = true)
        {
            Directory.CreateDirectory(repositoryRoot);
            var configuration = Path.Combine(state.Root, "invocation.json");
            File.WriteAllText(configuration, new JsonObject { ["target"] = "direct", ["directOrigin"] = Origin(target) }.ToJsonString());
            var app = BroodlingHost.Build(["--urls=http://127.0.0.1:0", "--Broodling:Store=" + state.Path,
                "--Broodling:Invocation=" + configuration, "--Broodling:RepositoryRoot=" + repositoryRoot], peers);
            await app.StartAsync();
            var server = new Server(app, peers.Clock);
            // The startup scans have run, so nothing progresses until the test advances the clock.
            if (started) await ProvisioningProcessTests.WaitUntil(() => server.Processing.Progressor.Scans > 0);
            return server;
        }

        internal HttpClient NewClient() => new() { BaseAddress = new Uri(App.Urls.Single()) };

        internal JsonNode Get(string path) => JsonNode.Parse(Client.GetStringAsync(path).GetAwaiter().GetResult())!;

        /// <summary>Advance both services' scans until the condition holds.</summary>
        internal Task Until(Func<bool> condition) => ProvisioningProcessTests.WaitUntil(() =>
        {
            if (condition()) return true;
            ((FakeTimeProvider)clock).Advance(SubmissionProgressor.Cadence);
            return false;
        });

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
}
