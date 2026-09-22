using Broodling.Host;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class StoreLifecycleTests
{
    [Test]
    public async Task VersionOneRequiresExplicitUpgradeAndRetainsEveryIdentitySubmissionAndSourceFact()
    {
        using var fixture = new StoreFixture();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fixture.Path)!);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Path, Pooling = false, ForeignKeys = false
        }.ToString()))
        {
            connection.Open();
            using var restore = connection.CreateCommand();
            restore.CommandText = File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "dotnet-v1.sql"));
            restore.ExecuteNonQuery();
        }
        var before = RetainedVersionOneFacts(fixture);
        var oldFile = File.ReadAllBytes(fixture.Path);
        await Assert.That(() => fixture.Open()).Throws<StoreStateException>();
        await Assert.That(File.ReadAllBytes(fixture.Path).SequenceEqual(oldFile)).IsTrue();
        StoreInformation upgradedInformation;
        using (var upgraded = fixture.Application.UpgradeStore(fixture.Path))
        {
            upgradedInformation = upgraded.Information;
            await Assert.That(upgradedInformation.SchemaVersion).IsEqualTo(2);
            await Assert.That(upgradedInformation.InitializedAt).IsEqualTo("2026-09-22T15:07:04.8538767+00:00");
            await Assert.That(RetainedVersionOneFacts(fixture)).IsEqualTo(before);
            var status = upgraded.AdmitSources(ContractIngressTests.Reference,
                [ContractIngressTests.Primary([0, 255, 13, 10])], ContractIngressTests.Propose, []);
            await Assert.That(status.Decision!.Admitted).IsTrue();
            await Assert.That(status.WorkUnit.RepositoryIdentity).IsEqualTo("repository-v1");
            await Assert.That(status.WorkUnit.IssueIdentity).IsEqualTo("issue-v1");
            await Assert.That(status.Sources.Single().EntitlementBasis).IsEqualTo("Retain reviewed original bytes");
            await Assert.That(status.Sources.Single().SourceId).IsEqualTo("src-e54323dbacab9b95b3d0ae762814376a97085a1df604b02719de6c81ee3b4692");
        }
        using var reopened = fixture.Open();
        using var repeatedUpgrade = fixture.Application.UpgradeStore(fixture.Path);
        await Assert.That(repeatedUpgrade.Information).IsEqualTo(upgradedInformation);
        await Assert.That(reopened.History(ContractIngressTests.Reference).Single().Decision!.Admitted).IsTrue();
    }

    private static string RetainedVersionOneFacts(StoreFixture fixture)
    {
        using var connection = fixture.Connect();
        var facts = new List<object?[]>();
        foreach (var table in new[] { "work_units", "work_submissions", "entitled_sources" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY 1";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                facts.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
        }
        return JsonSerializer.Serialize(facts);
    }

    [Test]
    public async Task InitializationRequiresNewStateAndExplicitUpgradeOfCurrentStorePreservesIdentity()
    {
        using var fixture = new StoreFixture();
        StoreInformation information;
        WorkUnit first;
        using (var store = fixture.Initialize())
        {
            information = store.Information;
            first = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 12));
        }
        await Assert.That(() => fixture.Initialize()).Throws<StoreStateException>();
        using var upgraded = fixture.Application.UpgradeStore(fixture.Path);
        await Assert.That(upgraded.Information).IsEqualTo(information);
        await Assert.That(upgraded.GetWorkUnit(first.WorkUnitId)).IsEqualTo(first);
        await Assert.That(information.Format).IsEqualTo("broodling.dotnet");
        await Assert.That(information.SchemaVersion).IsEqualTo(2);
    }

    [Test]
    public async Task MissingStoreIsNeverCreatedByOpenOrUpgrade()
    {
        using var fixture = new StoreFixture();
        await Assert.That(() => fixture.Open()).Throws<StoreStateException>();
        await Assert.That(() => fixture.Application.UpgradeStore(fixture.Path)).Throws<StoreStateException>();
        await Assert.That(Directory.Exists(System.IO.Path.GetDirectoryName(fixture.Path))).IsFalse();
    }

    [Test]
    [Arguments("empty")]
    [Arguments("non-sqlite")]
    [Arguments("foreign")]
    [Arguments("python")]
    [Arguments("malformed-metadata")]
    [Arguments("version")]
    [Arguments("definition")]
    [Arguments("missing-trigger")]
    public async Task UnrecognizedOrIncompatibleStateRefusesEveryLifecycleOperationWithoutReplacement(string state)
    {
        using var fixture = new StoreFixture();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fixture.Path)!);
        if (state is "empty" or "non-sqlite")
            File.WriteAllBytes(fixture.Path, state == "empty" ? [] : "retained non-sqlite state"u8.ToArray());
        else if (state is "python" or "foreign" or "malformed-metadata")
        {
            using var connection = new SqliteConnection($"Data Source={fixture.Path};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = state switch
            {
                "python" => "CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL) STRICT; INSERT INTO schema_meta VALUES ('schema_version', '11');",
                "malformed-metadata" => "CREATE TABLE store_metadata (singleton, format, version, definition_hash, manifest_hash, initialized_at); INSERT INTO store_metadata VALUES (1, 'broodling.dotnet', 'not a version', '', '', 'now');",
                _ => "CREATE TABLE unrelated (value TEXT); INSERT INTO unrelated VALUES ('retain');"
            };
            command.ExecuteNonQuery();
        }
        else
        {
            using (fixture.Initialize()) { }
            fixture.Execute(state switch
            {
                "version" => "UPDATE store_metadata SET version = 99",
                "definition" => "UPDATE store_metadata SET definition_hash = 'changed'",
                _ => "DROP TRIGGER sources_no_update"
            });
        }
        var before = File.ReadAllBytes(fixture.Path);
        await Assert.That(() => fixture.Open()).Throws<StoreStateException>();
        await Assert.That(() => fixture.Application.UpgradeStore(fixture.Path)).Throws<StoreStateException>();
        await Assert.That(() => fixture.Initialize()).Throws<StoreStateException>();
        await Assert.That(File.ReadAllBytes(fixture.Path).SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task StoreCannotBePlacedInsideDisposableWorktreeEvenThroughParentSymlink()
    {
        using var fixture = new StoreFixture();
        var disposable = System.IO.Path.Combine(fixture.Root, "disposable");
        var nested = System.IO.Path.Combine(disposable, "nested");
        Directory.CreateDirectory(nested);
        var existing = System.IO.Path.Combine(nested, "existing.sqlite3");
        using (fixture.Application.InitializeStore(existing)) { }
        File.WriteAllText(System.IO.Path.Combine(disposable, ".broodling-disposable-worktree"), "attempt");
        var link = System.IO.Path.Combine(fixture.Root, "link");
        Directory.CreateSymbolicLink(link, disposable);
        var nestedLink = System.IO.Path.Combine(fixture.Root, "nested-link");
        Directory.CreateSymbolicLink(nestedLink, nested);
        var indirectLink = System.IO.Path.Combine(fixture.Root, "indirect-link");
        Directory.CreateSymbolicLink(indirectLink, System.IO.Path.Combine(link, "nested"));
        var fileLink = System.IO.Path.Combine(fixture.Root, "file-link.sqlite3");
        File.CreateSymbolicLink(fileLink, existing);
        foreach (var root in new[] { disposable, link, nestedLink, indirectLink })
        {
            var path = System.IO.Path.Combine(root, "state", "broodling.sqlite3");
            await Assert.That(() => fixture.Application.InitializeStore(path)).Throws<StoreStateException>();
            await Assert.That(() => fixture.Application.OpenStore(path)).Throws<StoreStateException>();
            await Assert.That(File.Exists(path)).IsFalse();
        }
        foreach (var path in new[] { existing, System.IO.Path.Combine(nestedLink, "existing.sqlite3"),
            System.IO.Path.Combine(indirectLink, "existing.sqlite3"), fileLink })
        {
            await Assert.That(() => fixture.Application.OpenStore(path)).Throws<StoreStateException>();
            await Assert.That(() => fixture.Application.UpgradeStore(path)).Throws<StoreStateException>();
        }
    }

    [Test]
    public async Task StoreUsesPhysicalPathThroughLinksAndRefusesLinkCycles()
    {
        using var fixture = new StoreFixture();
        var physical = System.IO.Path.Combine(fixture.Root, "physical");
        Directory.CreateDirectory(System.IO.Path.Combine(physical, "nested"));
        var link = System.IO.Path.Combine(fixture.Root, "alias");
        Directory.CreateSymbolicLink(link, System.IO.Path.Combine(physical, "nested"));
        using (var store = fixture.Application.InitializeStore(System.IO.Path.Combine(link, "..", "state.sqlite3")))
            await Assert.That(store.Path).IsEqualTo(System.IO.Path.Combine(physical, "state.sqlite3"));
        var cycle = System.IO.Path.Combine(fixture.Root, "cycle");
        Directory.CreateSymbolicLink(cycle, cycle);
        await Assert.That(() => fixture.Application.InitializeStore(System.IO.Path.Combine(cycle, "state.sqlite3")))
            .Throws<StoreStateException>();
    }

    [Test]
    public async Task LifecycleOperatorUsesApplicationAndProducesSafeErrorsWithoutStartingHost()
    {
        using var fixture = new StoreFixture();
        var output = new StringWriter();
        var error = new StringWriter();
        await Assert.That(StoreCommands.Run(["initialize-store", fixture.Path], fixture.Application, output, error)).IsEqualTo(0);
        await Assert.That(output.ToString().Contains("broodling.dotnet")).IsTrue();
        output.GetStringBuilder().Clear();
        await Assert.That(StoreCommands.Run(["upgrade-store", fixture.Path], fixture.Application, output, error)).IsEqualTo(0);
        await Assert.That(StoreCommands.Run(["initialize-store", fixture.Path], fixture.Application, output, error)).IsEqualTo(1);
        await Assert.That(error.ToString().Contains("store_exists")).IsTrue();
        await Assert.That(error.ToString().Contains(fixture.Path)).IsFalse();
        await Assert.That(error.ToString().Contains("Exception")).IsFalse();
        await Assert.That(StoreCommands.Run(["initialize-store"], fixture.Application, output, error)).IsEqualTo(2);
        using var reopened = fixture.Open();
        await Assert.That(reopened.Information.SchemaVersion).IsEqualTo(2);
    }
}
