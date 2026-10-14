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
    private const string Declaration = ".broodling/environment.json";

    private static byte[] Declare(string name) => Encoding.UTF8.GetBytes($$"""{"version": 1, "environment": "{{name}}"}""" + "\n");

    /// <summary>Write <paramref name="content"/> at <paramref name="path"/> in the repository and commit it.</summary>
    private static string Commit(AttemptFixture git, string path, byte[] content)
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

    private static string Column(HttpFixture fixture, string column) =>
        fixture.Scalar($"SELECT ifnull({column}, 'null') FROM native_submissions WHERE attempt_id = '{fixture.Attempt.AttemptId}'");

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
        await Assert.That((string)environment["resolution"]!["basis"]!).IsEqualTo("declared");
        await Assert.That((string)environment["resolution"]!["name"]!).IsEqualTo("approved-alt");
        await Assert.That(prepared.Origin).IsEqualTo(second.Origin.GetLeftPart(UriPartial.Authority));
        await Assert.That(Column(fixture, "declaration_sha256")).IsEqualTo(sha256);
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
        await Assert.That((string)environment["resolution"]!["basis"]!).IsEqualTo("default");
        await Assert.That((string)environment["resolution"]!["name"]!).IsEqualTo("dotnet");
        await Assert.That(prepared.Origin).IsEqualTo("http://127.0.0.1:9");
        await Assert.That(Column(fixture, "declaration_sha256")).IsEqualTo("null");
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
            _ => git => Commit(git, Declaration, Declare("dotnet-r3"))
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
            ["basis"] = "default", ["name"] = "dotnet",
            ["adoption"] = new JsonObject
            {
                ["kind"] = "broodling.target-environment-adoption/v1", ["release"] = "dotnet-r2", ["sha256"] = adopted.RecordSha256
            },
            ["manifest"] = new JsonObject
            {
                ["schema"] = "zeroshot-environment.manifest/v2",
                ["sha256"] = "8d1168f339f599d1719dcdc4f4cae8c44966d3f642a3a7e6a3cf81f7e3c66f6e"
            },
            ["image"] = "ghcr.io/faviann/zeroshot-environments/dotnet@sha256:5d36313b1d38ad06c194c53b8f08a71d4bc61527e4e06d56b96c9a835ba54a1d",
            ["platform"] = "linux/amd64", ["os"] = manifest.Os,
            ["components"] = new JsonObject
            {
                ["native"] = DirectTargetBinding.NativeRelease, ["codex"] = DirectTargetBinding.CodexVersion,
                ["claude"] = manifest.Components.Claude, ["copilot"] = manifest.Components.Copilot,
                ["harness-node"] = DirectTargetBinding.CodexNodeVersion, ["node"] = manifest.Components.Node,
                ["gh"] = manifest.Components.Gh, ["git"] = manifest.Components.Git, ["python"] = manifest.Components.Python,
                ["dotnetSdks"] = new JsonArray([.. manifest.Components.DotnetSdks.Select(sdk => JsonValue.Create(sdk))]),
                ["dotnetRuntimes"] = new JsonArray([.. manifest.Components.DotnetRuntimes.Select(runtime => JsonValue.Create(runtime))])
            },
            ["target"] = new JsonObject
            {
                ["environment"] = "dotnet", ["origin"] = origin, ["container"] = "broodling-test-dotnet",
                ["stateMount"] = "/srv/broodling-test/dotnet/state", ["homeMount"] = "/srv/broodling-test/dotnet/home"
            }
        })).IsTrue();
        await Assert.That(manifest.Components.DotnetSdks).Contains("10.0.401");
        // The native/asset binding is retained beside it.
        await Assert.That(JsonNode.DeepEquals(binding["native"], DirectTargetBinding.Native())).IsTrue();
        await Assert.That(prepared.AssetSha256).IsEqualTo(DirectTargetBinding.AssetSha256);
        // The full adoption record and producer manifest, by digest, exactly as bundled.
        await Assert.That(Column(fixture, "environment_name")).IsEqualTo("dotnet");
        await Assert.That(Column(fixture, "adoption_sha256")).IsEqualTo(adopted.RecordSha256);
        await Assert.That(Column(fixture, "manifest_sha256")).IsEqualTo(adopted.ManifestSha256);
        await Assert.That(Document(fixture, adopted.RecordSha256).SequenceEqual(adopted.Record)).IsTrue();
        await Assert.That(Document(fixture, adopted.ManifestSha256).SequenceEqual(adopted.Manifest)).IsTrue();
        await Assert.That(Column(fixture, "environment_sha256"))
            .IsEqualTo(Digests.Bytes(Encoding.UTF8.GetBytes(resolution.ToJsonString())));

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
        await Assert.That(fixture.Scalar($"SELECT count(DISTINCT environment_sha256) FROM native_submissions")).IsEqualTo("1");
        await Assert.That(fixture.Scalar("SELECT count(*) FROM environment_documents")).IsEqualTo("2");
    }

    [Test]
    public async Task SqlKeepsTheResolutionAndItsDocumentsImmutable()
    {
        using var fixture = new HttpFixture(source: git => Commit(git, Declaration, Declare("approved-alt")));
        var prepared = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, Catalog("http://127.0.0.1:9", "http://127.0.0.1:10"));
        async Task Refused(string sql) => await Assert.That(() => fixture.Git.State.Execute(sql)).Throws<SqliteException>();

        foreach (var change in new[]
        {
            "environment_name = 'dotnet'", $"environment_sha256 = '{new string('0', 64)}'", "declaration_sha256 = NULL",
            "adoption_sha256 = manifest_sha256", "binding_json = json_set(binding_json, '$.environment.resolution.name', 'dotnet')"
        })
            await Refused("UPDATE native_submissions SET " + change);
        await Refused("UPDATE environment_documents SET content = x'00'");
        await Refused("DELETE FROM environment_documents");
        // Another Attempt's record whose columns disagree with its binding cannot be inserted either.
        var reference = WorkReference.Parse("acme/widget", 13);
        var revision = fixture.Store.AdmitSources(reference, [new("primary_issue", reference.IssueLocator,
            "Another request.\n"u8.ToArray(), entitlement: new("caller", "Reviewed"))], ContractIngressTests.Propose, ContractIngressTests.PullRequest).Revision;
        var other = fixture.Store.AdmitHttpAttempt(revision.ContractRevisionId, fixture.Git.Repository, fixture.Attempt.B1.CommitOid).AttemptId;
        var run = Guid.CreateVersion7().ToString();
        string Copy(string column = "", string value = "")
        {
            var values = new Dictionary<string, string>
            {
                ["attempt_id"] = $"'{other}'", ["submission_key"] = $"'broodling:http:v1:{other}'",
                ["request_json"] = $"json_set(request_json, '$.runId', '{run}', '$.submission.submissionKey', 'broodling:http:v1:{other}')",
                ["state"] = "state", ["intended_run_id"] = $"'{run}'"
            };
            foreach (var retained in new[] { "asset_sha256", "binding_json", "environment_name", "environment_sha256", "adoption_sha256",
                "manifest_sha256", "declaration_sha256" })
                values[retained] = retained;
            if (column != "") values[column] = value;
            return $"INSERT INTO native_submissions ({string.Join(", ", values.Keys)}) SELECT {string.Join(", ", values.Values)} "
                + $"FROM native_submissions WHERE attempt_id = '{prepared.AttemptId}'";
        }
        foreach (var (column, value) in new[]
        {
            ("environment_name", "'dotnet'"), ("declaration_sha256", "NULL"), ("adoption_sha256", "manifest_sha256"),
            ("binding_json", "json_set(binding_json, '$.origin', 'http://127.0.0.1:9')"),
            ("binding_json", "json_set(binding_json, '$.environment.requirement.declaration.commit', 'other')")
        })
            await Refused(Copy(column, value));
        fixture.Git.State.Execute(Copy()); // Only the disagreement was refused.
    }

    [Test]
    public async Task ARetainedDocumentThatDiffersRefusesTheRecord()
    {
        using var fixture = new HttpFixture(source: git => Commit(git, Declaration, Declare("approved-alt")));
        var catalog = Catalog("http://127.0.0.1:9", "http://127.0.0.1:10");
        var prepared = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, catalog);
        var declaration = Column(fixture, "declaration_sha256");
        fixture.Git.State.Execute("DROP TRIGGER environment_documents_no_update; "
            + $"UPDATE environment_documents SET content = CAST('{{\"version\": 1, \"environment\": \"dotnet\"}}' AS BLOB) WHERE sha256 = '{declaration}'");

        await Assert.That(() => fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, catalog)).Throws<SubmissionConflict>();
        await Assert.That(fixture.Store.FindSubmission(fixture.Attempt.AttemptId)).IsEqualTo(prepared);
    }
}
