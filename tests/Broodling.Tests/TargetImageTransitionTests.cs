using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;
using static Broodling.Tests.StockDirectTargetTests;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

/// <summary>
/// Native state survives a restart of this revision's DirectTarget image (or the candidate named by
/// <c>BROODLING_TEST_TARGET_IMAGE</c>) and an update to it from each published target image listed in
/// <c>deployment/native-state-transitions.json</c>, on the same state and home mounts and origin, through
/// the entrypoint's ordinary startup. A listed image must carry the binding's own native: transitions are
/// established only within one native release. Only the application observes the result. Provider, forge
/// and PR receipt are controlled, as in the stock witness.
/// </summary>
public sealed class TargetImageTransitionTests
{
    public static IEnumerable<Func<SourceTarget>> Sources() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestRepository.Root, "deployment", "native-state-transitions.json")))!["from"]!
            .AsArray().Select(source => (Func<SourceTarget>)(() => new((string)source!["image"]!,
                (string)source["native"]!["version"]!, (string)source["native"]!["linuxX64ExecutableSha256"]!)))
            .Prepend(() => new(null, DirectTargetBinding.NativeVersion, DirectTargetBinding.NativeExecutableSha256))
            .ToList();

    [Test]
    [MethodDataSource(nameof(Sources))]
    public async Task NativeStateSurvivesTheRestartOrUpdateToThisImage(SourceTarget source)
    {
        await Assert.That((source.NativeVersion, source.NativeSha256))
            .IsEqualTo((DirectTargetBinding.NativeVersion, DirectTargetBinding.NativeExecutableSha256));
        var image = source.Image is null ? await Direct.Value : await Pinned(source.Image);
        // The native version recorded for the source image is the one it carries.
        var native = await DockerCommand("run", "--rm", "--network", "none", "--entrypoint", "/bin/sh", image, "-ec",
            "zeroshot --version; sha256sum < /usr/local/bin/zeroshot");
        RequireSuccess(native);
        await Assert.That(native.Output).IsEqualTo($"{source.NativeVersion}\n{source.NativeSha256}  -\n");
        using var delivered = new AttemptFixture();
        using var unacknowledged = new AttemptFixture();
        await using var target = await StockDirectTarget.StartAsync(delivered.State.Root, image: await ControlledOver(image));

        // On the source image: one correlated run that finished, and one send whose acknowledgement was lost.
        var admitted = Admit(delivered, target);
        using var store = admitted.Store;
        await target.PushAsync(delivered.Repository, "main");
        var correlated = await new Invocation(store, new InvocationTarget(target.Origin))
            .ResumeAsync(admitted.Revision, delivered.Repository, delivered.Head, Credentials);
        var attempt = correlated.Attempts.Single().AttemptId;
        await Finished(store, attempt);
        var reference = await TerminalAsync(correlated.Submissions.Single().Run!);
        var pending = Admit(unacknowledged, target);
        using var pendingStore = pending.Store;
        var replay = new Invocation(pendingStore, new InvocationTarget(target.Origin));
        // The target records the run, but its acknowledgement is lost.
        var unresolved = await LoseAcknowledgementAsync(pendingStore, unacknowledged, replay, pending.Revision, unacknowledged.Head, target);
        await Assert.That(await target.RunCountAsync()).IsEqualTo(2);

        await target.RestartAsync(await Controlled.Value);

        // Wait reconnects by the retained run identity and consumes the result the source image produced.
        var completion = await store.WaitAsync(attempt);
        await Assert.That(completion.Outcome).IsEqualTo("SUCCEEDED");
        await Assert.That(completion.DeliveryReceipt.GetRawText()).IsEqualTo(reference.GetRawText());
        // The exact replay converges on the run the source image recorded for that submission key.
        var replayed = await replay.ResumeAsync(pending.Revision, credentials: Credentials);
        await Assert.That(replayed.Submissions.Single().RunId).IsEqualTo(unresolved.IntendedRunId);
        await Assert.That(await target.RunCountAsync()).IsEqualTo(2);
    }

    /// <summary>
    /// A published target image by digest, or this revision's image itself (<c>null</c>, a restart), and the
    /// native it carries; displayed as its reference.
    /// </summary>
    public sealed record SourceTarget(string? Image, string NativeVersion, string NativeSha256)
    {
        public override string ToString() => Image ?? "this image (restart)";
    }
}
