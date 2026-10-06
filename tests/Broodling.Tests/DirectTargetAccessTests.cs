using System.Text;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// The private control-token boundary at the application's own seams, through real SQLite/Git, the pinned SDK and
/// loopback stand-ins for targets in native's private mode: a token is read for exactly the retained origin at
/// each operation and sent only there; missing or refused tokens fail with fixed kinds and keep authority,
/// dispatch uncertainty, the frozen request and retained reads as they are. Native's own enforcement and the
/// agent boundary are qualified on the actual image (NativeTargetStartupTests, the image demonstration).
/// </summary>
public sealed class DirectTargetAccessTests
{
    private static string Origin(StockTarget target) => target.Origin.GetLeftPart(UriPartial.Authority);

    [Test]
    [Arguments("none")]
    [Arguments("other-origin")]
    [Arguments("missing-file")]
    [Arguments("malformed-file")]
    [Arguments("relative-file")]
    public async Task DispatchWithoutAReadableTokenForTheRetainedOriginRecordsAndSendsNothing(string change)
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        var malformed = Path.Combine(fixture.Git.State.Root, "malformed-token");
        File.WriteAllText(malformed, target.Token + "\n");
        var access = change switch
        {
            "none" => null,
            // The configured token belongs to another origin; the retained one gets none.
            "other-origin" => DirectTargetAccess.For("http://127.0.0.1:9", target.TokenFile),
            "missing-file" => DirectTargetAccess.For(Origin(target), Path.Combine(fixture.Git.State.Root, "missing-token")),
            "malformed-file" => DirectTargetAccess.For(Origin(target), malformed),
            _ => DirectTargetAccess.For(Origin(target), "control-token")
        };
        using (var store = fixture.Git.State.Application.OpenStore(fixture.Git.State.Path, access))
        {
            var error = await DirectTargetRunTests.Fails(() => store.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials()),
                "credentials_unavailable");
            await Assert.That(error.Message.Contains(target.Token)).IsFalse();
            await Assert.That(target.Connections).IsEqualTo(0);
            // No dispatch intent: the record is still only prepared.
            await Assert.That(store.FindSubmission(prepared.AttemptId)).IsEqualTo(prepared);
        }
        // With the origin's token configured, the same retained request is sent.
        var correlated = await fixture.Store.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials());
        await Assert.That(correlated.RunId).IsEqualTo(prepared.IntendedRunId);
    }

    /// <summary>
    /// A token the target refuses (one rotated or never bootstrapped) leaves the authorized send unresolved, like
    /// any outcome but the exact acknowledgement. Changing the token changes neither the frozen request nor its
    /// authority: the exact replay with the current token converges on the intended run.
    /// </summary>
    [Test]
    public async Task ARefusedTokenLeavesDispatchUnresolvedAndTheExactReplayWithTheCurrentTokenConverges()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        var former = target.Token;
        target.Token = TestAccess.NewSecret(); // Restarted and bootstrapped with a rotated token.

        var error = await DirectTargetRunTests.Fails(() => fixture.Store.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials()),
            "unauthorized");
        await Assert.That(error.Message.Contains(former) || error.Message.Contains(target.Token)).IsFalse();
        await Assert.That(target.Unauthorized).IsEqualTo(1);
        await Assert.That(target.Runs.IsEmpty).IsTrue();
        var unresolved = fixture.Store.FindSubmission(prepared.AttemptId)!;
        await Assert.That(unresolved.State).IsEqualTo("dispatched");
        await Assert.That(unresolved.RunId).IsNull();
        await Assert.That(unresolved.ReplayBlockedReason).IsNull();
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(1);

        // The operator installs the current token; the next operation reads it, with no restart.
        File.WriteAllText(target.TokenFile, target.Token);
        var correlated = await fixture.Store.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials("rotated"));
        await Assert.That(correlated.State).IsEqualTo("correlated");
        await Assert.That(correlated.RunId).IsEqualTo(prepared.IntendedRunId);
        await Assert.That(correlated.RequestJson).IsEqualTo(prepared.RequestJson);
        await Assert.That(correlated.BindingJson).IsEqualTo(prepared.BindingJson);
        var sent = target.Bodies.Single();
        sent.Remove("connections");
        sent.Remove("githubToken");
        await Assert.That(JsonNode.DeepEquals(sent, JsonNode.Parse(prepared.RequestJson))).IsTrue();
        fixture.Store.Dispose();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(fixture.Git.State.Path)!))
        {
            var content = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            await Assert.That(content.Contains(former) || content.Contains(target.Token)).IsFalse();
        }
    }

    /// <summary>
    /// Observation, wait and stop of a correlated Attempt with a refused or missing token fail with fixed kinds and
    /// keep its authority; stop still records abandonment, its native stop reported as not sent. Retained reads,
    /// including a retained completion, need neither a token nor the target.
    /// </summary>
    [Test]
    public async Task RefusedOrMissingTokensFailRunOperationsSafelyAndRetainedReadsNeedNeither()
    {
        var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        var run = submission.Frozen.Run(submission.IntendedRunId);
        var attempt = fixture.Attempt.AttemptId;
        target.Token = TestAccess.NewSecret();
        using (var none = fixture.Git.State.Application.OpenStore(fixture.Git.State.Path))
        {
            var unconfigured = await none.ObserveAsync(attempt);
            await Assert.That(((NativeObservation.Unavailable)unconfigured!).Reason).IsEqualTo("credentials_unavailable");
            await DirectTargetRunTests.Fails(() => none.WaitAsync(attempt), "credentials_unavailable");
            await Assert.That(target.Connections).IsEqualTo(0);
        }
        var refused = await fixture.Store.ObserveAsync(attempt);
        await Assert.That(((NativeObservation.Unavailable)refused!).Reason).IsEqualTo("unauthorized");
        await DirectTargetRunTests.Fails(() => fixture.Store.WaitAsync(attempt), "unauthorized");
        fixture.Store.RequireCurrentAttempt(attempt);
        await Assert.That(fixture.Store.FindCompletion(attempt)).IsNull();
        await Assert.That(target.Count("run/status") + target.Count("run/force")).IsEqualTo(0);

        // Once the current token is configured the same retained run completes.
        File.WriteAllText(target.TokenFile, target.Token);
        var accepted = fixture.Git.Deliver();
        target.Projections.Enqueue(AttemptCompletionTests.HttpFinished(submission, "succeeded", CompletionFixture.Receipt(head: accepted)));
        var completion = await fixture.Store.WaitAsync(attempt);
        await Assert.That(completion.RunId).IsEqualTo(run.RunId);
        await target.DisposeAsync();
        using var offline = fixture.Git.State.Application.OpenStore(fixture.Git.State.Path);
        await Assert.That(await offline.WaitAsync(attempt)).IsEqualTo(completion);
        await Assert.That(offline.GetAttempt(attempt).AttemptId).IsEqualTo(attempt);
    }

    [Test]
    public async Task StopWithARefusedTokenAbandonsAndReportsTheNativeStopAsNotSent()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        fixture.PrepareAt(target.Origin, "correlated");
        target.Token = TestAccess.NewSecret();
        var error = await Assert.That(async () => await fixture.Store.StopAsync(fixture.Attempt.AttemptId, "operator requested stop"))
            .Throws<CessationUnconfirmed>();
        await Assert.That(error!.Message).Contains("Native stop was not sent (unauthorized)");
        await Assert.That(error.NativeStopRequested).IsFalse();
        await Assert.That(fixture.Store.GetAttempt(fixture.Attempt.AttemptId).Abandonment!.Reason).IsEqualTo("operator requested stop");
        await Assert.That(target.Count("run/force")).IsEqualTo(0);
    }

    /// <summary>
    /// Two separately configured origins with distinct tokens: each target receives only its own token, one
    /// target's token cannot control the other, and a configuration that no longer names an Attempt's retained
    /// origin redirects nothing: its Attempt reaches no target until its own origin is configured again.
    /// </summary>
    [Test]
    public async Task TwoOriginsReceiveOnlyTheirOwnTokensAndAConfigurationChangeRedirectsNoAttempt()
    {
        await using var first = new StockTarget();
        await using var second = new StockTarget();
        using var atFirst = new HttpFixture();
        using var atSecond = new HttpFixture();
        var firstPrepared = atFirst.PrepareAt(first.Origin);
        atSecond.PrepareAt(second.Origin);
        // One configuration naming both origins, each with its own token file.
        var both = new DirectTargetAccess(origin => origin == Origin(first) ? first.TokenFile : origin == Origin(second) ? second.TokenFile : null);
        using (var store = atFirst.Git.State.Application.OpenStore(atFirst.Git.State.Path, both))
            await store.DispatchHttpAsync(firstPrepared.AttemptId, HttpDispatchTests.Credentials());
        using (var store = atSecond.Git.State.Application.OpenStore(atSecond.Git.State.Path, both))
            await store.DispatchHttpAsync(atSecond.Attempt.AttemptId, HttpDispatchTests.Credentials());
        await Assert.That(first.Heads.Any(head => head.Contains(second.Token)) || second.Heads.Any(head => head.Contains(first.Token))).IsFalse();
        await Assert.That(first.Heads.Single().Contains("Authorization: Bearer " + first.Token)).IsTrue();
        await Assert.That(second.Heads.Single().Contains("Authorization: Bearer " + second.Token)).IsTrue();

        // The first target's token, configured for the second origin, does not control the second target.
        var crossed = DirectTargetAccess.For(Origin(second), first.TokenFile);
        using (var store = atSecond.Git.State.Application.OpenStore(atSecond.Git.State.Path, crossed))
            await Assert.That(((NativeObservation.Unavailable)(await store.ObserveAsync(atSecond.Attempt.AttemptId))!).Reason).IsEqualTo("unauthorized");
        await Assert.That(second.Unauthorized).IsEqualTo(1);

        // The operator's configuration now names only the second target: the first Attempt is not redirected there.
        var contacted = (first.Connections, second.Connections);
        var switched = DirectTargetAccess.For(Origin(second), second.TokenFile);
        using (var store = atFirst.Git.State.Application.OpenStore(atFirst.Git.State.Path, switched))
        {
            var attempt = atFirst.Attempt.AttemptId;
            await Assert.That(((NativeObservation.Unavailable)(await store.ObserveAsync(attempt))!).Reason).IsEqualTo("credentials_unavailable");
            await DirectTargetRunTests.Fails(() => store.WaitAsync(attempt), "credentials_unavailable");
            await Assert.That(() => store.PrepareHttpSubmission(attempt, Origin(second)))
                .Throws<SubmissionConflict>();
            await Assert.That(store.FindSubmission(attempt)!.Origin).IsEqualTo(Origin(first));
        }
        await Assert.That((first.Connections, second.Connections)).IsEqualTo(contacted);
        // Its own origin's configuration reaches it again.
        first.Projections.Enqueue(DirectTargetRunTests.Running(atFirst.Store.FindSubmission(atFirst.Attempt.AttemptId)!.Run));
        await Assert.That(await atFirst.Store.ObserveAsync(atFirst.Attempt.AttemptId)).IsTypeOf<NativeObservation.Available>();
    }

    /// <summary>
    /// A bootstrap whose acknowledgement is lost is resolved only by authenticating with the intended token; one
    /// that installed nothing fails without being resent, and the explicit rerun then installs it. Native's own
    /// envelope checks and closing are qualified on the actual image (NativeTargetStartupTests).
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ALostBootstrapAcknowledgementIsResolvedByAuthenticatingNeverByResending(bool installed)
    {
        using var bootstrap = new BootstrapTarget { LoseAcknowledgement = true, Install = installed };
        await using var target = bootstrap.Start();
        if (installed)
        {
            await Assert.That(await bootstrap.RunAsync(target)).IsEqualTo(DirectTargetBootstrapResult.Installed);
            await Assert.That(bootstrap.Envelopes).IsEqualTo(1);
        }
        else
        {
            var failure = await Assert.That(async () => await bootstrap.RunAsync(target)).Throws<DirectTargetBootstrapFailed>();
            await Assert.That(failure!.Message).Contains("outcome is unknown");
            await Assert.That(bootstrap.Envelopes).IsEqualTo(1);
            await Assert.That(bootstrap.Installed).IsNull();
            // The operator's rerun sends a new envelope, since the target does not accept the token.
            (bootstrap.LoseAcknowledgement, bootstrap.Install) = (false, true);
            await Assert.That(await bootstrap.RunAsync(target)).IsEqualTo(DirectTargetBootstrapResult.Installed);
            await Assert.That(bootstrap.Envelopes).IsEqualTo(2);
        }
        // Once the token is accepted, a repeat sends nothing.
        await Assert.That(await bootstrap.RunAsync(target)).IsEqualTo(DirectTargetBootstrapResult.AlreadyInstalled);
        await Assert.That(bootstrap.Envelopes).IsEqualTo(installed ? 1 : 2);
        await Assert.That(bootstrap.Installed).IsEqualTo(bootstrap.Token);
    }

    [Test]
    [Arguments("other-token")]
    [Arguments("public-target")]
    [Arguments("key-is-token")]
    public async Task ABootstrapThatCannotInstallTheTokenFailsWithoutReplacingAnything(string change)
    {
        using var bootstrap = new BootstrapTarget();
        var other = TestAccess.NewSecret();
        switch (change)
        {
            case "other-token": bootstrap.Installed = other; break;
            case "public-target": bootstrap.Authentication = "none"; break;
            default: bootstrap.Key = bootstrap.Token; break;
        }
        await using var target = bootstrap.Start();
        var failure = await Assert.That(async () => await bootstrap.RunAsync(target)).Throws<BroodlingException>();
        await Assert.That(failure!.Message).Contains(change switch
        {
            "other-token" => "holds another one",
            "public-target" => "does not serve native private access",
            _ => "must differ from the control token"
        });
        await Assert.That(failure.Message.Contains(bootstrap.Token) || failure.Message.Contains(bootstrap.Key)).IsFalse();
        await Assert.That(bootstrap.Installed).IsEqualTo(change == "other-token" ? other : null);
        await Assert.That(bootstrap.Envelopes).IsEqualTo(change == "other-token" ? 1 : 0);
    }

    /// <summary>
    /// A raw loopback target answering like native's private mode: public discovery, a session only for the
    /// installed token, and a one-time bootstrap that opens the envelope with its key. It can lose the
    /// acknowledgement after processing the envelope, with or without installing its token.
    /// </summary>
    private sealed class BootstrapTarget : IDisposable
    {
        internal string Token { get; } = TestAccess.NewSecret();
        internal string Key { get; set; } = TestAccess.NewSecret();
        internal string Authentication { get; set; } = "private_capability";
        internal string? Installed { get; set; }
        internal bool LoseAcknowledgement { get; set; }
        internal bool Install { get; set; } = true;
        internal int Envelopes { get; private set; }
        private string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("broodling-bootstrap-").FullName;

        internal StockTarget Start()
        {
            StockTarget? target = null;
            target = new StockTarget { Raw = (stream, token) => Answer(target!, stream, token) };
            return target;
        }

        internal Task<DirectTargetBootstrapResult> RunAsync(StockTarget target)
        {
            File.WriteAllText(Path.Combine(Directory, "token"), Token);
            File.WriteAllText(Path.Combine(Directory, "key"), Key);
            return DirectTargetControl.BootstrapAsync(Origin(target), DirectTargetAccess.For(Origin(target), Path.Combine(Directory, "token")),
                Path.Combine(Directory, "key"));
        }

        private async Task Answer(StockTarget target, Stream stream, CancellationToken cancellation)
        {
            string head;
            lock (target.Heads) head = target.Heads[^1];
            var length = head.Split("\r\n").FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            var body = new byte[length is null ? 0 : int.Parse(length["Content-Length:".Length..].Trim())];
            await stream.ReadExactlyAsync(body, cancellation);
            var bearer = head.Split("\r\n").FirstOrDefault(line => line.StartsWith("Authorization: Bearer ", StringComparison.Ordinal))?["Authorization: Bearer ".Length..];
            var (status, reply) = head.Split(' ')[1] switch
            {
                "/.well-known/zeroshot-native-v2" => (200, $$"""
                    {"kind":"zeroshot.native-v2-target/v2","authentication":"{{Authentication}}","runPath":"/native-v2/run",
                     "sessionPath":"/native-v2/oecp-session","oecpPath":"/native-v2/oecp","audience":"controller"
                     {{(Authentication == "none" ? "" : ",\"privateBootstrapPath\":\"/native-v2/private-bootstrap\"")}}}
                    """),
                "/native-v2/oecp-session" when bearer is not null && bearer == Installed =>
                    (200, $$"""{"endpoint":"ws://{{target.Origin.Authority}}/native-v2/oecp","bearerToken":"{{Installed}}"}"""),
                "/native-v2/oecp-session" => (401, """{"code":"request.unauthorized","message":"unauthorized"}"""),
                "/native-v2/private-bootstrap" => Bootstrap(body),
                _ => (404, """{"code":"request.not_found","message":"target route was not found"}""")
            };
            if (status == 0) return; // The acknowledgement is lost: the connection closes without a reply.
            var bytes = Encoding.UTF8.GetBytes(reply);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} Status\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n")
                .Concat(bytes).ToArray(), cancellation);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

        /// <summary>Native private_access.rs: closed once a token is installed; otherwise open the AES-256-GCM envelope.</summary>
        private (int, string) Bootstrap(byte[] body)
        {
            Envelopes++;
            if (Installed is not null) return (404, """{"code":"request.not_found","message":"target route was not found"}""");
            var envelope = JsonNode.Parse(body)!;
            var sealedToken = Convert.FromHexString((string)envelope["ciphertext"]!);
            var token = new byte[sealedToken.Length - 16];
            using (var aes = new System.Security.Cryptography.AesGcm(Convert.FromHexString(Key), 16))
                aes.Decrypt(Convert.FromHexString((string)envelope["nonce"]!), sealedToken.AsSpan(0, token.Length), sealedToken.AsSpan(token.Length),
                    token, "zeroshot-capsule-bootstrap-v1"u8);
            if (Install) Installed = Encoding.ASCII.GetString(token);
            return LoseAcknowledgement ? (0, "") : (204, "");
        }
    }
}
