using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

/// <summary>
/// Two environments whose manifests state incompatible OS identities, each served by its own target: the adopted
/// dotnet-r6 as <c>dotnet</c>, the default, and the test-only T (<see cref="TestEnvironmentImage"/>) as
/// <c>t235</c>, pinned by digest and adopted through the same record rules. Each target is its own container with
/// its own state and home. The controlled stock targets run the selected unmodified native under the application;
/// the readiness case runs both actual images behind one zeroshot-tls.
/// </summary>
public sealed class TwoEnvironmentTests
{
    private const string Name = TestEnvironmentImage.Name;
    private static DispatchCredentials Credentials => StockDirectTargetTests.Credentials;

    /// <summary>
    /// The controlled installation: one stock target per environment, each over its own forge under
    /// <paramref name="git"/>'s root, and the catalog naming them by their actual containers and volumes.
    /// </summary>
    private sealed record Targets(StockDirectTarget Dotnet, StockDirectTarget Test, EnvironmentEntry DotnetEntry, EnvironmentEntry TestEntry,
        AdoptedEnvironment TestAdoption, EnvironmentCatalog Catalog) : IAsyncDisposable
    {
        internal static async Task<Targets> StartAsync(AttemptFixture git)
        {
            var adoption = await TestEnvironmentImage.Adopted;
            var dotnet = await StockDirectTarget.StartAsync(Path.Combine(git.State.Root, "dotnet"));
            StockDirectTarget? test = null;
            try
            {
                test = await StockDirectTarget.StartAsync(Path.Combine(git.State.Root, Name), await ControlledOver(adoption.Image));
                var (dotnetEntry, testEntry) = (await dotnet.EntryAsync("dotnet", TestEnvironments.Adopted.Image), await test.EntryAsync(Name, adoption.Image));
                // Connection material derived from the catalog itself: each origin's own token file.
                var catalog = EnvironmentCatalog.Create([dotnetEntry, testEntry], "dotnet", null, await TestEnvironmentImage.SetAsync());
                return new(dotnet, test, dotnetEntry, testEntry, adoption, catalog);
            }
            catch
            {
                if (test is not null) await test.DisposeAsync();
                await dotnet.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Test.DisposeAsync();
            await Dotnet.DisposeAsync();
        }
    }

    /// <summary>
    /// A repository at <paramref name="path"/> with one commit, whose <c>acme/widget</c> origin resolves to
    /// <paramref name="target"/>'s forge, pushed there. Returns its head.
    /// </summary>
    private static async Task<string> Repository(string path, StockDirectTarget target, Action<string>? change = null)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            AttemptFixture.RunGit(path, "init", "--quiet", "--initial-branch=main");
            AttemptFixture.RunGit(path, "config", "user.email", "test@example.invalid");
            AttemptFixture.RunGit(path, "config", "user.name", "Broodling test");
            AttemptFixture.RunGit(path, "config", "gc.auto", "0");
            File.WriteAllText(Path.Combine(path, "original.txt"), "original selected bytes\n");
            AttemptFixture.RunGit(path, "add", ".");
            AttemptFixture.RunGit(path, "commit", "--quiet", "-m", "original");
        }
        change?.Invoke(path);
        AttemptFixture.RunGit(path, "remote", "add", "origin", "https://github.com/acme/widget.git");
        AttemptFixture.RunGit(path, "config", "url." + target.Forge + ".insteadOf", "https://github.com/acme/widget.git");
        await target.PushAsync(path, "main");
        return AttemptFixture.RunGit(path, "rev-parse", "HEAD").Trim();
    }

    /// <summary>Repository A: the fixture's repository, declaring T at its head, which is B1.</summary>
    private static Task<string> DeclaringT(AttemptFixture git, Targets targets) => Repository(git.Repository, targets.Test,
        _ => EnvironmentSelectionTests.Commit(git, EnvironmentSelectionTests.Declaration, EnvironmentSelectionTests.Declare(Name)));

    private static BroodlingStore Open(AttemptFixture git, EnvironmentCatalog catalog) => git.State.Application.OpenStore(git.State.Path, catalog);

    private static byte[] Document(AttemptFixture git, string sha256)
    {
        using var connection = git.State.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT content FROM environment_documents WHERE sha256 = $sha256";
        command.Parameters.AddWithValue("$sha256", sha256);
        return (byte[])command.ExecuteScalar()!;
    }

    /// <summary>
    /// Repository A declares T and repository B declares nothing, so B takes the default. Each Attempt retains its
    /// own environment: adoption, full manifest and its digest, image digest, OS and target identity with origin.
    /// Each run exists only in its own target's native ledger, and each agent observed its own environment's OS.
    /// </summary>
    [Test]
    public async Task IncompatibleOsManifestsRunInSeparateTargetsThatEachAttemptRetains()
    {
        using var git = new AttemptFixture();
        await using var targets = await Targets.StartAsync(git);
        var b1A = await DeclaringT(git, targets);
        var repositoryB = Path.Combine(git.State.Root, "source-b");
        var b1B = await Repository(repositoryB, targets.Dotnet);
        targets.Dotnet.ReportOs();
        targets.Test.ReportOs();
        using var store = Open(git, targets.Catalog);
        var referenceB = WorkReference.Parse("acme/widget", 13);
        var revisionB = store.AdmitSources(referenceB, [new SourceSubmission("primary_issue", referenceB.IssueLocator,
                "Another complete reviewed request.\n"u8.ToArray(), entitlement: new("caller", "Reviewed supplied issue bytes"))],
            ContractIngressTests.Propose, ContractIngressTests.PullRequest).Revision.ContractRevisionId;

        var invocation = new Invocation(store, targets.Catalog);
        var a = (await invocation.ResumeAsync(git.RevisionId, git.Repository, b1A, Credentials)).Submissions.Single();
        var b = (await invocation.ResumeAsync(revisionB, repositoryB, b1B, Credentials)).Submissions.Single();

        var dotnet = TestEnvironments.Adopted;
        await Assert.That(targets.TestAdoption.Environment.Os).IsEqualTo(TestEnvironmentImage.Os);
        await Assert.That(targets.TestAdoption.Environment.Os).IsNotEqualTo(dotnet.Environment.Os);
        foreach (var (submission, entry, adoption, declared) in new[]
            { (a, targets.TestEntry, targets.TestAdoption, true), (b, targets.DotnetEntry, dotnet, false) })
        {
            await Assert.That(submission.State).IsEqualTo("correlated");
            await Assert.That(submission.Origin).IsEqualTo(entry.Origin);
            await Assert.That(submission.Environment).IsEqualTo(new RetainedEnvironment(entry.Name, adoption.Image, adoption.RecordSha256,
                adoption.ManifestSha256, new TargetIdentity(entry.Name, entry.Origin, entry.Container, entry.StateMount, entry.HomeMount)));
            var environment = JsonNode.Parse(submission.BindingJson)!["environment"]!;
            await Assert.That((string?)environment["requirement"]!["name"]).IsEqualTo(declared ? Name : null);
            var resolution = environment["resolution"]!;
            await Assert.That((string)resolution["adoption"]!["release"]!).IsEqualTo(adoption.Release);
            await Assert.That((string)resolution["manifest"]!["sha256"]!).IsEqualTo(adoption.ManifestSha256);
            await Assert.That((string)resolution["image"]!).IsEqualTo(adoption.Image);
            await Assert.That((string)resolution["os"]!).IsEqualTo(adoption.Environment.Os);
            await Assert.That((string)resolution["target"]!["origin"]!).IsEqualTo(entry.Origin);
            // The full adoption record and manifest, by their digests.
            await Assert.That(Document(git, adoption.RecordSha256).SequenceEqual(adoption.Record)).IsTrue();
            await Assert.That(Document(git, adoption.ManifestSha256).SequenceEqual(adoption.Manifest)).IsTrue();
        }
        await Assert.That(targets.TestEntry.Container).IsNotEqualTo(targets.DotnetEntry.Container);
        await Assert.That(await targets.Test.RunIdsAsync()).IsEquivalentTo([a.RunId!]);
        await Assert.That(await targets.Dotnet.RunIdsAsync()).IsEquivalentTo([b.RunId!]);

        foreach (var (submission, repository, os) in new[]
            { (a, git.Repository, TestEnvironmentImage.Os), (b, repositoryB, dotnet.Environment.Os) })
        {
            await StockDirectTargetTests.Finished(store, submission.AttemptId);
            var completion = await store.WaitAsync(submission.AttemptId);
            await Assert.That(completion.Outcome).IsEqualTo("SUCCEEDED");
            await Assert.That(AttemptFixture.RunGit(repository, "show", completion.AcceptedRevision + ":observed-os"))
                .IsEqualTo($"PRETTY_NAME=\"{os}\"\n");
        }
    }

    /// <summary>
    /// An acknowledgement lost after T's target recorded the run; then the repository's branch moves to declare the
    /// default environment instead. After a restart the exact replay reaches only T's target, which converges on the
    /// intended run, and the default target receives nothing.
    /// </summary>
    [Test]
    public async Task ALostAcknowledgementReplaysOnlyToTheRetainedTarget()
    {
        using var git = new AttemptFixture();
        await using var targets = await Targets.StartAsync(git);
        var b1 = await DeclaringT(git, targets);
        NativeSubmission unresolved;
        using (var store = Open(git, targets.Catalog))
            unresolved = await StockDirectTargetTests.LoseAcknowledgementAsync(store, git, new Invocation(store, targets.Catalog),
                git.RevisionId, b1, targets.Test);
        await Assert.That(await targets.Test.RunIdsAsync()).IsEquivalentTo([unresolved.IntendedRunId]);
        EnvironmentSelectionTests.Commit(git, EnvironmentSelectionTests.Declaration, EnvironmentSelectionTests.Declare("dotnet"));

        using var restarted = Open(git, targets.Catalog);
        var replayed = (await new Invocation(restarted, targets.Catalog).ResumeAsync(git.RevisionId, credentials: Credentials))
            .Submissions.Single();

        await Assert.That(replayed.State).IsEqualTo("correlated");
        await Assert.That(replayed.RunId).IsEqualTo(unresolved.IntendedRunId);
        await Assert.That(replayed.BindingJson).IsEqualTo(unresolved.BindingJson);
        await Assert.That(replayed.Origin).IsEqualTo(targets.TestEntry.Origin);
        await Assert.That(await targets.Test.RunIdsAsync()).IsEquivalentTo([unresolved.IntendedRunId]);
        await Assert.That(await targets.Dotnet.RunCountAsync()).IsEqualTo(0);
    }

    /// <summary>
    /// Both actual images behind one zeroshot-tls, each target on its own origin, state and home: readiness reports
    /// both ready with their own OS, and refuses once the two targets' state mounts are swapped.
    /// </summary>
    [Test]
    public async Task BothActualTargetsAreReadyWithTheirOwnOsAndSwappedStateMountsAreRefused()
    {
        var adoption = await TestEnvironmentImage.Adopted;
        await using var stack = await TargetStack.CreateAsync((TargetStack.DefaultEnvironment, await Direct.Value), (Name, adoption.Image));
        await stack.InitializeAll();
        foreach (var target in stack.Targets) await stack.StartTarget(target);
        await stack.StartBroodling();
        foreach (var target in stack.Targets)
        {
            await stack.Serving(target);
            await stack.Bootstrap(target: target);
        }
        var catalog = stack.Catalog(await TestEnvironmentImage.SetAsync());
        var readiness = new TargetReadiness();

        var report = await readiness.CheckAsync(await stack.Inventory(), catalog);
        await Assert.That(string.Join("; ", report.Targets.Select(target => $"{target.Environment} {target.Release} {target.Os}")))
            .IsEqualTo($"dotnet dotnet-r6 {TestEnvironments.Adopted.Environment.Os}; {Name} {TestEnvironmentImage.Release} {TestEnvironmentImage.Os}");

        var (dotnet, test) = (stack.Targets[0], stack.Targets[1]);
        string[] Swapped(TargetStack.StackTarget target, TargetStack.StackTarget other) =>
        [
            .. TargetStack.Bind(other.State, "/state"), .. TargetStack.Bind(target.Home, "/home/node"),
            .. TargetStack.Bind(stack.RootCertificate, "/tls-root", true),
            .. TargetStack.Bind(target.BootstrapKeyFile, TargetReadiness.BootstrapKeySecret, true)
        ];
        foreach (var target in stack.Targets) await stack.StopTarget(target);
        await stack.StartTarget(dotnet, Swapped(dotnet, test));
        await stack.StartTarget(test, Swapped(test, dotnet));
        foreach (var target in stack.Targets) await stack.Serving(target);

        var refused = await Assert.That(async () => await readiness.CheckAsync(await stack.Inventory(), catalog)).Throws<TargetNotReady>();
        await Assert.That(refused!.Message).IsEqualTo("Environment dotnet: Target persistent mounts differ from the catalog and inventory.");
    }
}
