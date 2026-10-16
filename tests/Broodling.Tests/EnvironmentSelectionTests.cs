using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// An Attempt's environment: the repository declaration read at original B1, its resolution against the operator's
/// catalog, fixed once with the Prepared submission and read back from retained content only.
/// </summary>
public sealed class EnvironmentSelectionTests
{
    internal const string Declaration = ".broodling/environment.json";

    internal static byte[] Declare(string name) => Encoding.UTF8.GetBytes($$"""{"version": 1, "environment": "{{name}}"}""" + "\n");

    /// <summary>Write <paramref name="content"/> at <paramref name="path"/> in the repository and commit it.</summary>
    internal static string Commit(AttemptFixture git, string path, byte[] content)
    {
        var file = Path.Combine(git.Repository, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, content);
        git.Git("add", "--", path);
        git.Git("commit", "-m", "change " + path);
        return git.Git("rev-parse", "HEAD").Trim();
    }

    /// <summary>Two approved environments, <c>dotnet</c> (the default unless none is given) and <c>approved-alt</c>.</summary>
    private static EnvironmentCatalog Catalog(string first, string second, string? defaultEnvironment = "dotnet") =>
        TestEnvironments.Catalog([TestEnvironments.Entry("dotnet", first), TestEnvironments.Entry("approved-alt", second)], defaultEnvironment);

    private static BroodlingStore Open(HttpFixture fixture, EnvironmentCatalog? environments) =>
        fixture.Git.State.Application.OpenStore(fixture.Git.State.Path, environments);

    private static byte[] Document(HttpFixture fixture, string sha256)
    {
        using var connection = fixture.Git.State.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT content FROM environment_documents WHERE sha256 = $sha256";
        command.Parameters.AddWithValue("$sha256", sha256);
        return (byte[])command.ExecuteScalar()!;
    }

    [Test]
    public async Task ExactB1DeclarationSelectsAndItsBytesAndSourceAreRetained()
    {
        await using var first = new StockTarget();
        await using var second = new StockTarget();
        var declared = Declare("approved-alt");
        using var fixture = new HttpFixture(source: git => Commit(git, Declaration, declared));
        var b1 = fixture.Attempt.B1.CommitOid;
        var blob = fixture.Git.Git("rev-parse", b1 + ":" + Declaration).Trim();
        // After B1 the branch moves and the working tree changes the declaration: only B1's bytes count.
        Commit(fixture.Git, Declaration, Declare("dotnet"));
        File.WriteAllBytes(Path.Combine(fixture.Git.Repository, Declaration), Declare("unapproved-later"));

        var prepared = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, Catalog(first.Origin.GetLeftPart(UriPartial.Authority),
            second.Origin.GetLeftPart(UriPartial.Authority)));

        var environment = JsonNode.Parse(prepared.BindingJson)!["environment"]!;
        var sha256 = Digests.Bytes(declared);
        await Assert.That(JsonNode.DeepEquals(environment["requirement"], new JsonObject
        {
            ["name"] = "approved-alt",
            ["declaration"] = new JsonObject { ["path"] = Declaration, ["commit"] = b1, ["blob"] = blob, ["sha256"] = sha256 }
        })).IsTrue();
        await Assert.That((string)environment["resolution"]!["target"]!["environment"]!).IsEqualTo("approved-alt");
        await Assert.That(prepared.Origin).IsEqualTo(second.Origin.GetLeftPart(UriPartial.Authority));
        await Assert.That(Document(fixture, sha256).SequenceEqual(declared)).IsTrue();
        await Assert.That(first.Connections + second.Connections).IsEqualTo(0);
    }

    [Test]
    public async Task AMissingDeclarationUsesTheConfiguredDefault()
    {
        using var fixture = new HttpFixture();
        var prepared = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, Catalog("http://127.0.0.1:9", "http://127.0.0.1:10"));

        var environment = JsonNode.Parse(prepared.BindingJson)!["environment"]!;
        await Assert.That(JsonNode.DeepEquals(environment["requirement"], new JsonObject { ["name"] = null, ["declaration"] = null })).IsTrue();
        await Assert.That((string)environment["resolution"]!["target"]!["environment"]!).IsEqualTo("dotnet");
        await Assert.That(prepared.Origin).IsEqualTo("http://127.0.0.1:9");
    }

    /// <summary>
    /// Every refusal is visible by its code before anything is retained or any target is contacted, even through
    /// an invocation that would dispatch. No nearest name, default or other entry is substituted.
    /// </summary>
    [Test]
    [Arguments("no-default", "environment_default_missing")]
    [Arguments("not-json", "environment_declaration_invalid")]
    [Arguments("byte-order-mark", "environment_declaration_invalid")]
    [Arguments("not-utf8", "environment_declaration_invalid")]
    [Arguments("oversized", "environment_declaration_invalid")]
    [Arguments("not-an-object", "environment_declaration_invalid")]
    [Arguments("no-version", "environment_declaration_invalid")]
    [Arguments("no-name", "environment_declaration_invalid")]
    [Arguments("symlink", "environment_declaration_invalid")]
    [Arguments("directory", "environment_declaration_invalid")]
    [Arguments("submodule", "environment_declaration_invalid")]
    [Arguments("parent-is-a-file", "environment_declaration_invalid")]
    [Arguments("uppercase-name", "environment_declaration_invalid")]
    [Arguments("long-name", "environment_declaration_invalid")]
    [Arguments("repeated-member", "environment_declaration_conflict")]
    [Arguments("version-2", "environment_requirement_unsupported")]
    [Arguments("sdk-member", "environment_requirement_unsupported")]
    [Arguments("unapproved", "environment_unapproved")]
    public async Task RefusalsAreVisibleBeforeAnythingIsRetainedOrContacted(string change, string code)
    {
        await using var first = new StockTarget();
        await using var second = new StockTarget();
        Action<AttemptFixture>? source = change switch
        {
            "no-default" => null,
            "not-json" => git => Commit(git, Declaration, """{"version": 1, "environment": "dotnet" """u8.ToArray()),
            "byte-order-mark" => git => Commit(git, Declaration, [0xEF, 0xBB, 0xBF, .. Declare("dotnet")]),
            "not-utf8" => git => Commit(git, Declaration, [.. """{"version": 1, "environment": "dotnet", "x": " """u8, 0xFF, .. "\"}"u8]),
            "oversized" => git => Commit(git, Declaration, [.. Declare("dotnet"), .. Enumerable.Repeat((byte)' ', 1024)]),
            "not-an-object" => git => Commit(git, Declaration, """["dotnet"]"""u8.ToArray()),
            "no-version" => git => Commit(git, Declaration, """{"environment": "dotnet"}"""u8.ToArray()),
            "no-name" => git => Commit(git, Declaration, """{"version": 1}"""u8.ToArray()),
            "symlink" => git =>
            {
                Directory.CreateDirectory(Path.Combine(git.Repository, ".broodling"));
                File.CreateSymbolicLink(Path.Combine(git.Repository, Declaration), "../original.txt");
                git.Git("add", "--", Declaration);
                git.Git("commit", "-m", "symlink");
            },
            "directory" => git => Commit(git, Declaration + "/environment.json", Declare("dotnet")),
            "submodule" => git =>
            {
                Directory.CreateDirectory(Path.Combine(git.Repository, Declaration));
                git.Git("update-index", "--add", "--cacheinfo", "160000," + git.Head + "," + Declaration);
                git.Git("commit", "-m", "submodule");
            },
            "parent-is-a-file" => git => Commit(git, ".broodling", Declare("dotnet")),
            "uppercase-name" => git => Commit(git, Declaration, Declare("Dotnet")),
            "long-name" => git => Commit(git, Declaration, Declare(new string('a', 55))),
            "repeated-member" => git => Commit(git, Declaration, """{"version": 1, "environment": "dotnet", "environment": "approved-alt"}"""u8.ToArray()),
            "version-2" => git => Commit(git, Declaration, """{"version": 2, "environment": "dotnet"}"""u8.ToArray()),
            "sdk-member" => git => Commit(git, Declaration, """{"version": 1, "environment": "dotnet", "sdk": "10.0.401"}"""u8.ToArray()),
            // The catalog approves dotnet, never a nearest release of it.
            _ => git => Commit(git, Declaration, Declare("dotnet-r4"))
        };
        using var fixture = new HttpFixture(source: source);
        var catalog = Catalog(first.Origin.GetLeftPart(UriPartial.Authority), second.Origin.GetLeftPart(UriPartial.Authority),
            change == "no-default" ? null : "dotnet");
        using var store = Open(fixture, catalog);

        var refused = await Assert.That(async () => await new Invocation(store, catalog)
            .ResumeAsync(fixture.Attempt.ContractRevisionId, credentials: HttpDispatchTests.Credentials())).Throws<EnvironmentRefused>();
        await Assert.That(refused!.Code).IsEqualTo(code);
        await Assert.That(fixture.Scalar("SELECT count(*) FROM native_submissions")).IsEqualTo("0");
        await Assert.That(fixture.Scalar("SELECT count(*) FROM environment_documents")).IsEqualTo("0");
        await Assert.That(first.Connections + second.Connections).IsEqualTo(0);
        // The refusal is not retained either: preparation stays possible once the repository or catalog is fixed.
        await Assert.That(store.GetAttempt(fixture.Attempt.AttemptId).IsCurrent).IsTrue();
    }

    [Test]
    public async Task GlobalJsonIsNeverInterpreted()
    {
        using var fixture = new HttpFixture(source: git =>
        {
            Commit(git, "global.json", """{"sdk": {"version": "8.0.100", "rollForward": "disable"}}"""u8.ToArray());
            Commit(git, Declaration, Declare("approved-alt"));
        });
        var prepared = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, Catalog("http://127.0.0.1:9", "http://127.0.0.1:10"));

        // The bundled environment has SDK 10 only; the declared name alone decides, and global.json is not retained.
        await Assert.That(prepared.Origin).IsEqualTo("http://127.0.0.1:10");
        await Assert.That(prepared.BindingJson.Contains("global.json") || prepared.BindingJson.Contains("8.0.100")).IsFalse();
    }

    [Test]
    public async Task TheDeclarationIsExecutionContextNeverAuthority()
    {
        using var fixture = new HttpFixture(source: git => Commit(git, Declaration, Declare("approved-alt")));
        var contract = fixture.Store.GetContractRevision(fixture.Attempt.ContractRevisionId).CanonicalBytes;
        var prepared = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, Catalog("http://127.0.0.1:9", "http://127.0.0.1:10"));

        // The request sent to the target and the admitted Contract carry no part of it; only the binding retains it.
        await Assert.That(prepared.RequestJson.Contains("approved-alt") || prepared.RequestJson.Contains(".broodling")).IsFalse();
        await Assert.That(fixture.Store.GetContractRevision(fixture.Attempt.ContractRevisionId).CanonicalBytes.SequenceEqual(contract)).IsTrue();
        await Assert.That(prepared.BindingJson.Contains("approved-alt")).IsTrue();
    }

    [Test]
    public async Task TheRetainedResolutionIsCompleteAndCarriesNoCredentials()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var origin = target.Origin.GetLeftPart(UriPartial.Authority);
        var catalog = TestEnvironments.Configured(TestEnvironments.Entry("dotnet", origin, target.TokenFile));
        var prepared = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, catalog);

        var adopted = TestEnvironments.Adopted;
        var manifest = adopted.Environment;
        var binding = JsonNode.Parse(prepared.BindingJson)!;
        var resolution = binding["environment"]!["resolution"]!;
        await Assert.That(JsonNode.DeepEquals(resolution, new JsonObject
        {
            ["adoption"] = new JsonObject
            {
                ["kind"] = "broodling.target-environment-adoption/v1", ["release"] = "dotnet-r4", ["sha256"] = adopted.RecordSha256
            },
            ["manifest"] = new JsonObject
            {
                ["schema"] = "zeroshot-environment.manifest/v2",
                ["sha256"] = "cd12d897da384cc4aa802892c7841a7d29bc1c04f883f3f122a6bea0171c673e"
            },
            ["image"] = "ghcr.io/faviann/zeroshot-environments/dotnet@sha256:6536a8b9c3e7b80b5396b36e0f40e5dc9eda2ed9743eba6821a1b40bc56bb156",
            ["platform"] = "linux/amd64", ["os"] = manifest.Os,
            ["components"] = JsonNode.Parse("""
                {"native": ["10.12.0"], "codex": ["0.155.0"], "claude": ["2.1.276"], "copilot": ["1.0.86"],
                 "node": ["v24.22.0"], "harness-node": ["v24.22.0"], "npm": ["11.19.1"], "yarn": ["1.22.22"],
                 "corepack": ["0.36.0"], "python": ["Python 3.12.14"], "pip": ["pip 25.0.1"],
                 "rust": ["rustc 1.97.0 (2d8144b78 2026-07-07)"], "rustup": ["rustup 1.29.0 (28d1352db 2026-03-05)"],
                 "rust-components": ["1.97.0-x86_64-unknown-linux-gnu: cargo-x86_64-unknown-linux-gnu clippy-x86_64-unknown-linux-gnu rust-std-x86_64-unknown-linux-gnu rustc-x86_64-unknown-linux-gnu rustfmt-x86_64-unknown-linux-gnu"],
                 "gcc": ["14.2.0"], "git": ["git version 2.47.3"], "gh": ["gh version 2.101.0 (2026-09-15)"],
                 "docker-cli": ["Docker version 26.1.5+dfsg1, build a72d7cd"], "dotnet-sdks": ["10.0.401"],
                 "dotnet-runtimes": ["Microsoft.AspNetCore.App 10.0.12", "Microsoft.NETCore.App 10.0.12"]}
                """),
            ["target"] = new JsonObject
            {
                ["environment"] = "dotnet", ["origin"] = origin, ["container"] = "broodling-test-dotnet",
                ["stateMount"] = "/srv/broodling-test/dotnet/state", ["homeMount"] = "/srv/broodling-test/dotnet/home"
            }
        })).IsTrue();
        // The native/asset binding is retained beside it.
        await Assert.That(JsonNode.DeepEquals(binding["native"], DirectTargetBinding.Native())).IsTrue();
        await Assert.That(prepared.AssetSha256).IsEqualTo(DirectTargetBinding.AssetSha256);
        // The full adoption record and producer manifest, by digest, exactly as bundled.
        await Assert.That(Document(fixture, adopted.RecordSha256).SequenceEqual(adopted.Record)).IsTrue();
        await Assert.That(Document(fixture, adopted.ManifestSha256).SequenceEqual(adopted.Manifest)).IsTrue();

        // Neither the control token nor its file appears anywhere in the store.
        fixture.Store.Dispose();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(fixture.Git.State.Path)!))
        {
            var content = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            await Assert.That(content.Contains(target.Token) || content.Contains(target.TokenFile)).IsFalse();
        }
    }

    [Test]
    public async Task ReopeningIgnoresTodaysRepositoryCatalogAndDefault()
    {
        using var fixture = new HttpFixture(source: git => Commit(git, Declaration, Declare("approved-alt")));
        var prepared = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, Catalog("http://127.0.0.1:9", "http://127.0.0.1:10"));
        fixture.Store.Dispose();
        // The repository is gone and the catalog now names other targets and another default.
        Directory.Delete(fixture.Git.Repository, recursive: true);
        var today = TestEnvironments.Catalog([TestEnvironments.Entry("dotnet", "http://127.0.0.1:11")], "dotnet");

        using (var unconfigured = fixture.Git.State.Application.OpenStore(fixture.Git.State.Path))
            await Assert.That(unconfigured.Status(fixture.Attempt.ContractRevisionId).Submissions.Single()).IsEqualTo(prepared);
        using var reopened = Open(fixture, today);
        await Assert.That(reopened.PrepareHttpSubmission(fixture.Attempt.AttemptId, today)).IsEqualTo(prepared);
        await Assert.That(reopened.FindSubmission(fixture.Attempt.AttemptId)!.Origin).IsEqualTo("http://127.0.0.1:10");
    }

    [Test]
    public async Task ConcurrentFirstPreparationsPersistTheChoiceOnce()
    {
        using var fixture = new HttpFixture();
        // Two sessions whose catalogs default to different targets both read nothing retained before either commits.
        using var barrier = new Barrier(2);
        NativeSubmission Prepare(EnvironmentCatalog catalog)
        {
            using var store = Open(fixture, catalog);
            return store.PrepareHttpSubmission(fixture.Attempt.AttemptId, catalog, () =>
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                return ExecutionAsset.Load(fixture.Assets);
            });
        }
        var results = await Task.WhenAll(
            Task.Run(() => Prepare(Catalog("http://127.0.0.1:9", "http://127.0.0.1:10"))),
            Task.Run(() => Prepare(Catalog("http://127.0.0.1:9", "http://127.0.0.1:10", "approved-alt"))));

        await Assert.That(results[1]).IsEqualTo(results[0]);
        await Assert.That(fixture.Scalar("SELECT count(*) FROM native_submissions")).IsEqualTo("1");
        await Assert.That(fixture.Store.FindSubmission(fixture.Attempt.AttemptId)).IsEqualTo(results[0]);
    }

    [Test]
    public async Task CompatibleWorkSharesItsEnvironmentsTarget()
    {
        using var fixture = new HttpFixture();
        var reference = WorkReference.Parse("acme/widget", 13);
        var revision = fixture.Store.AdmitSources(reference, [new("primary_issue", reference.IssueLocator,
            "Another request.\n"u8.ToArray(), entitlement: new("caller", "Reviewed"))], ContractIngressTests.Propose, ContractIngressTests.PullRequest).Revision;
        var other = fixture.Store.AdmitHttpAttempt(revision.ContractRevisionId, fixture.Git.Repository, fixture.Git.Head);
        var catalog = Catalog("http://127.0.0.1:9", "http://127.0.0.1:10");

        var first = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, catalog);
        var second = fixture.Store.PrepareHttpSubmission(other.AttemptId, catalog);

        await Assert.That(second.Origin).IsEqualTo(first.Origin);
        await Assert.That(fixture.Scalar("SELECT count(DISTINCT json_extract(binding_json, '$.environment.resolution')) FROM native_submissions"))
            .IsEqualTo("1");
        await Assert.That(fixture.Scalar("SELECT count(*) FROM environment_documents")).IsEqualTo("2");
    }

    [Test]
    public async Task SqlKeepsTheResolutionAndItsDocumentsImmutable()
    {
        using var fixture = new HttpFixture(source: git => Commit(git, Declaration, Declare("approved-alt")));
        var prepared = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, Catalog("http://127.0.0.1:9", "http://127.0.0.1:10"));
        async Task Refused(string sql) => await Assert.That(() => fixture.Git.State.Execute(sql)).Throws<SqliteException>();

        await Refused("UPDATE native_submissions SET binding_json = json_set(binding_json, '$.environment.resolution.image', 'other')");
        await Refused("UPDATE environment_documents SET content = x'00'");
        await Refused("DELETE FROM environment_documents");
        // Another Attempt at the same B1 cannot retain a declaration read at another commit.
        var reference = WorkReference.Parse("acme/widget", 13);
        var revision = fixture.Store.AdmitSources(reference, [new("primary_issue", reference.IssueLocator,
            "Another request.\n"u8.ToArray(), entitlement: new("caller", "Reviewed"))], ContractIngressTests.Propose, ContractIngressTests.PullRequest).Revision;
        var other = fixture.Store.AdmitHttpAttempt(revision.ContractRevisionId, fixture.Git.Repository, fixture.Attempt.B1.CommitOid).AttemptId;
        var run = Guid.CreateVersion7().ToString();
        string Copy(string binding) => $"""
            INSERT INTO native_submissions (attempt_id, submission_key, request_json, state, intended_run_id, asset_sha256, binding_json)
            SELECT '{other}', 'broodling:http:v1:{other}',
                json_set(request_json, '$.runId', '{run}', '$.submission.submissionKey', 'broodling:http:v1:{other}'),
                state, '{run}', asset_sha256, {binding} FROM native_submissions WHERE attempt_id = '{prepared.AttemptId}'
            """;
        await Refused(Copy("json_set(binding_json, '$.environment.requirement.declaration.commit', 'other')"));
        fixture.Git.State.Execute(Copy("binding_json"));
    }

    [Test]
    public async Task ARetainedDocumentThatDiffersRefusesTheRecord()
    {
        using var fixture = new HttpFixture(source: git => Commit(git, Declaration, Declare("approved-alt")));
        var catalog = Catalog("http://127.0.0.1:9", "http://127.0.0.1:10");
        var prepared = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, catalog);
        var declaration = Digests.Bytes(Declare("approved-alt"));
        fixture.Git.State.Execute("DROP TRIGGER environment_documents_no_update; "
            + $"UPDATE environment_documents SET content = CAST('{{\"version\": 1, \"environment\": \"dotnet\"}}' AS BLOB) WHERE sha256 = '{declaration}'");

        await Assert.That(() => fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, catalog)).Throws<SubmissionConflict>();
        await Assert.That(fixture.Store.FindSubmission(fixture.Attempt.AttemptId)).IsEqualTo(prepared);
    }

    /// <summary>
    /// The bundled adoption plus a variant at another image digest, adopted as a stable name's image swap would be;
    /// its native, Claude Code and startup interface are the bundled one's.
    /// </summary>
    internal static IReadOnlyList<AdoptedEnvironment> WithSwappedImage(string directory)
    {
        var bundled = TestEnvironments.Adopted;
        var manifest = JsonNode.Parse(bundled.Manifest)!;
        manifest["environment"]!["release"] = "r235";
        manifest["image"]!["digest"] = "sha256:" + new string('5', 64);
        var bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        var record = JsonNode.Parse(bundled.Record)!;
        record["environment"] = "dotnet-r235";
        record["manifest"]!["path"] = "swapped.manifest.json";
        record["manifest"]!["sha256"] = Digests.Bytes(bytes);
        record["image"] = (string)manifest["image"]!["repository"]! + "@sha256:" + new string('5', 64);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "swapped.manifest.json"), bytes);
        File.WriteAllText(Path.Combine(directory, "swapped.adoption.json"), record.ToJsonString());
        return [bundled, .. AdoptedEnvironments.Load(directory)];
    }

    /// <summary>
    /// After a restart, a catalog that removed the retained entry, swapped its image or reassigned its origin cannot
    /// rebind the Attempt: dispatch refuses with a useful reason before intent or contact. A changed default alone
    /// changes nothing for it, and the send reaches the retained target.
    /// </summary>
    [Test]
    [Arguments("entry-removed")]
    [Arguments("image-changed")]
    [Arguments("origin-reassigned")]
    [Arguments("default-changed")]
    public async Task ARestartAfterCatalogChangesNeverRebindsAPreparedAttempt(string change)
    {
        await using var first = new StockTarget();
        await using var second = new StockTarget();
        using var fixture = new HttpFixture();
        var (a, b) = (first.Origin.GetLeftPart(UriPartial.Authority), second.Origin.GetLeftPart(UriPartial.Authority));
        var adopted = WithSwappedImage(Path.Combine(fixture.Git.State.Root, "adopted"));
        var prepared = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, Catalog(a, b));
        fixture.Store.Dispose();
        var today = change switch
        {
            "entry-removed" => TestEnvironments.Catalog([TestEnvironments.Entry("approved-alt", b)], "approved-alt"),
            "image-changed" => TestEnvironments.Catalog([TestEnvironments.Entry("dotnet", a, image: adopted[1].Image),
                TestEnvironments.Entry("approved-alt", b)], "dotnet", adopted),
            "origin-reassigned" => TestEnvironments.Catalog([TestEnvironments.Entry("dotnet", b), TestEnvironments.Entry("approved-alt", a)], "dotnet"),
            _ => Catalog(a, b, "approved-alt")
        };

        using var store = Open(fixture, today);
        var resume = () => new Invocation(store, today).ResumeAsync(fixture.Attempt.ContractRevisionId, credentials: HttpDispatchTests.Credentials());
        if (change == "default-changed")
        {
            await resume();
            await Assert.That(store.FindSubmission(fixture.Attempt.AttemptId)!.RunId).IsEqualTo(prepared.IntendedRunId);
            await Assert.That(first.Bodies.Count).IsEqualTo(1);
            await Assert.That(second.Connections).IsEqualTo(0);
            return;
        }
        var refused = await Assert.That(async () => await resume()).Throws<EnvironmentRefused>();
        await Assert.That(refused!.Code).IsEqualTo("environment_changed");
        await Assert.That(refused.Message).Contains($"prepared for dotnet ({TestEnvironments.Adopted.Image}) at {a}");
        await Assert.That(store.FindSubmission(fixture.Attempt.AttemptId)).IsEqualTo(prepared);
        await Assert.That(first.Connections + second.Connections).IsEqualTo(0);
    }

    /// <summary>
    /// An acknowledgement lost after the target accepted the run, then a catalog whose default moved to another
    /// target: the exact replay reaches only the retained target, which converges on the intended run.
    /// </summary>
    [Test]
    public async Task AReplayAfterALostAcknowledgementReachesOnlyTheRetainedTarget()
    {
        await using var first = new StockTarget();
        await using var second = new StockTarget();
        using var fixture = new HttpFixture();
        var (a, b) = (first.Origin.GetLeftPart(UriPartial.Authority), second.Origin.GetLeftPart(UriPartial.Authority));
        var catalog = Catalog(a, b);
        first.Submit = body =>
        {
            first.Accept(body);
            return Task.FromResult((503, """{"code":"target.unavailable","message":"unavailable"}"""));
        };
        using (var store = Open(fixture, catalog))
            await Assert.That(async () => await new Invocation(store, catalog).ResumeAsync(fixture.Attempt.ContractRevisionId,
                credentials: HttpDispatchTests.Credentials())).Throws<NativeTransportError>();
        var unresolved = fixture.Store.FindSubmission(fixture.Attempt.AttemptId)!;
        await Assert.That(unresolved.State).IsEqualTo("dispatched");
        first.Submit = body => Task.FromResult(first.Accept(body));

        var moved = Catalog(a, b, "approved-alt");
        using (var restarted = Open(fixture, moved))
            await new Invocation(restarted, moved).ResumeAsync(fixture.Attempt.ContractRevisionId, credentials: HttpDispatchTests.Credentials());

        var correlated = fixture.Store.FindSubmission(fixture.Attempt.AttemptId)!;
        await Assert.That(correlated.RunId).IsEqualTo(unresolved.IntendedRunId);
        await Assert.That(correlated.BindingJson).IsEqualTo(unresolved.BindingJson);
        await Assert.That(first.Bodies.Count).IsEqualTo(2);
        await Assert.That(first.Runs.Values.Single()).IsEqualTo(unresolved.IntendedRunId);
        await Assert.That(second.Connections).IsEqualTo(0);
    }

    /// <summary>
    /// An image swapped under the same name at the same origin still lets a correlated Attempt's run be read and
    /// waited on there, since reading starts no tools; a catalog that no longer names it at that origin sends nothing.
    /// </summary>
    [Test]
    public async Task ACorrelatedWaitAfterAnImageSwapReadsTheRetainedRun()
    {
        var target = new StockTarget();
        await using var other = new StockTarget();
        using var fixture = new HttpFixture();
        var (a, b) = (target.Origin.GetLeftPart(UriPartial.Authority), other.Origin.GetLeftPart(UriPartial.Authority));
        var adopted = WithSwappedImage(Path.Combine(fixture.Git.State.Root, "adopted"));
        fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, Catalog(a, b));
        fixture.Git.State.Execute("UPDATE native_submissions SET state = 'dispatched'");
        fixture.Git.State.Execute("UPDATE native_submissions SET state = 'correlated', run_id = intended_run_id");
        var submission = fixture.Store.FindSubmission(fixture.Attempt.AttemptId)!;

        // Another catalog moved the name elsewhere: nothing is read, waited on or stopped there.
        var elsewhere = TestEnvironments.Catalog([TestEnvironments.Entry("dotnet", b)], "dotnet");
        using (var store = Open(fixture, elsewhere))
        {
            await Assert.That(((NativeObservation.Unavailable)(await store.ObserveAsync(submission.AttemptId))!).Reason)
                .IsEqualTo("environment_unavailable");
            await DirectTargetRunTests.Fails(() => store.WaitAsync(submission.AttemptId), "environment_unavailable");
        }
        await Assert.That(target.Connections + other.Connections).IsEqualTo(0);

        var swapped = TestEnvironments.Catalog([TestEnvironments.Entry("dotnet", a, image: adopted[1].Image)], "dotnet", adopted);
        var accepted = fixture.Git.Deliver();
        target.Projections.Enqueue(AttemptCompletionTests.HttpFinished(submission, "succeeded", CompletionFixture.Receipt(head: accepted)));
        using (var store = Open(fixture, swapped))
        {
            var completion = await store.WaitAsync(submission.AttemptId);
            await Assert.That(completion.RunId).IsEqualTo(submission.IntendedRunId);
            await Assert.That(completion.AcceptedRevision).IsEqualTo(accepted);
        }
        await Assert.That(other.Connections).IsEqualTo(0);
        await target.DisposeAsync();
    }

    /// <summary>A stop under a catalog that no longer names the Attempt's environment at its origin abandons it and sends nothing.</summary>
    [Test]
    public async Task AStopUnderAnotherCatalogAbandonsAndSendsNothing()
    {
        await using var target = new StockTarget();
        await using var other = new StockTarget();
        using var fixture = new HttpFixture();
        var (a, b) = (target.Origin.GetLeftPart(UriPartial.Authority), other.Origin.GetLeftPart(UriPartial.Authority));
        fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, Catalog(a, b));
        fixture.Git.State.Execute("UPDATE native_submissions SET state = 'dispatched'");

        using var store = Open(fixture, Catalog(b, a));
        var error = await Assert.That(async () => await store.StopAsync(fixture.Attempt.AttemptId, "operator requested stop"))
            .Throws<CessationUnconfirmed>();
        await Assert.That(error!.Message).Contains("Native stop was not sent (environment_unavailable)");
        await Assert.That(store.GetAttempt(fixture.Attempt.AttemptId).Abandonment!.Reason).IsEqualTo("operator requested stop");
        await Assert.That(target.Connections + other.Connections).IsEqualTo(0);
    }
}
