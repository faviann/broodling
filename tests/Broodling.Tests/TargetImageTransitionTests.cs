using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;
using static Broodling.Tests.StockDirectTargetTests;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

/// <summary>
/// An update of the DirectTarget image over retained native state: state written by a published target
/// image listed in <c>deployment/native-state-transitions.json</c> is then served by this revision's
/// image (or the candidate named by <c>BROODLING_TEST_TARGET_IMAGE</c>) on the same state and home
/// mounts and origin, through the entrypoint's ordinary startup. Only the application observes the
/// result. Provider, forge and PR receipt are controlled, as in the stock witness.
/// </summary>
public sealed class TargetImageTransitionTests
{
    public static IEnumerable<Func<PublishedTarget>> ListedSources() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(NativeFixture.RepositoryRoot, "deployment", "native-state-transitions.json")))!["from"]!
            .AsArray().Select(source => (Func<PublishedTarget>)(() => new((string)source!["image"]!,
                (string)source["native"]!["version"]!, (string)source["native"]!["linuxX64ExecutableSha256"]!)))
            .ToList();

    [Test]
    [MethodDataSource(nameof(ListedSources))]
    public async Task NativeStateFromAListedPublishedImageSurvivesTheUpdateToThisImage(PublishedTarget source)
    {
        var published = await Pinned(source.Image);
        // The native version recorded for the published image is the one it carries.
        var native = await DockerCommand("run", "--rm", "--network", "none", "--entrypoint", "/bin/sh", published, "-ec",
            "zeroshot --version; sha256sum < /usr/local/bin/zeroshot");
        RequireSuccess(native);
        await Assert.That(native.Output).IsEqualTo($"{source.NativeVersion}\n{source.NativeSha256}  -\n");
        using var delivered = new AttemptFixture();
        using var unacknowledged = new AttemptFixture();
        await using var target = await StockDirectTarget.StartAsync(delivered.State.Root, image: await ControlledOver(published));

        // On the published image: one correlated run that finished, and one send whose acknowledgement was lost.
        var admitted = Admit(delivered, target);
        using var store = admitted.Store;
        await target.PushAsync(delivered.Repository, "main");
        var correlated = await new Invocation(store, new InvocationTarget.Direct(target.Origin))
            .ResumeAsync(admitted.Revision, delivered.Repository, delivered.Head, Credentials);
        var attempt = correlated.Attempts.Single().AttemptId;
        await Finished(store, attempt);
        var reference = await TerminalAsync(correlated.Submissions.Single().Run!);
        var pending = Admit(unacknowledged, target);
        using var pendingStore = pending.Store;
        var replay = new Invocation(pendingStore, new InvocationTarget.Direct(target.Origin));
        // A B1 missing from the forge: the target records the run but does not acknowledge it.
        var b1 = unacknowledged.Commit("never published\n");
        await Assert.That(async () => await replay.ResumeAsync(pending.Revision, unacknowledged.Repository, b1, Credentials))
            .Throws<NativeTransportError>();
        var unresolved = pendingStore.FindSubmission(pendingStore.Status(pending.Revision).Attempts.Single().AttemptId)!;
        await Assert.That(unresolved.RunId).IsNull();
        await Assert.That(await target.RunCountAsync()).IsEqualTo(2);

        await target.RestartAsync(await Controlled.Value);

        // Wait reconnects by the retained run identity and consumes the result the published image produced.
        var completion = await store.WaitAsync(attempt, null);
        await Assert.That(completion.Outcome).IsEqualTo("SUCCEEDED");
        await Assert.That(completion.DeliveryReceipt.GetRawText()).IsEqualTo(reference.GetRawText());
        // The exact replay converges on the run the published image recorded for that submission key.
        var replayed = await replay.ResumeAsync(pending.Revision, credentials: Credentials);
        await Assert.That(replayed.Submissions.Single().RunId).IsEqualTo(unresolved.IntendedRunId);
        await Assert.That(await target.RunCountAsync()).IsEqualTo(2);
    }

    /// <summary>A published target image by digest and the native it carries; displayed as its reference.</summary>
    public sealed record PublishedTarget(string Image, string NativeVersion, string NativeSha256)
    {
        public override string ToString() => Image;
    }
}
