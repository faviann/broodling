using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// <c>deployment/application-schemas.json</c> freezes each released application schema identity; a
/// <c>v*</c> tag publishes only a frozen one. A frozen definition is never edited again, and every frozen
/// version older than the current one has an explicit <c>upgrade-store</c> disposition.
/// </summary>
public sealed class ApplicationSchemaFreezeTests
{
    [Test]
    public async Task FrozenIdentitiesAreUnchangedAndEachOlderOneHasAnUpgradeStoreDisposition()
    {
        var file = JsonNode.Parse(File.ReadAllText(Path.Combine(NativeFixture.RepositoryRoot, "deployment", "application-schemas.json")))!;
        await Assert.That(file["format"]!.GetValue<string>()).IsEqualTo(StoreSchema.Format);
        var frozen = file["frozen"]!.AsArray().Select(entry => new SchemaIdentity(StoreSchema.Format,
            entry!["schemaVersion"]!.GetValue<int>(), entry["definitionSha256"]!.GetValue<string>())).ToArray();
        await Assert.That(frozen.DistinctBy(identity => identity.SchemaVersion).Count()).IsEqualTo(frozen.Length);

        foreach (var identity in frozen)
        {
            // A schema change after a release increments the version; it never edits a frozen definition.
            await Assert.That(identity.SchemaVersion).IsLessThanOrEqualTo(StoreSchema.Version);
            if (identity.SchemaVersion == StoreSchema.Version)
                await Assert.That(identity).IsEqualTo(StoreSchema.Current);
            else
                await Assert.That(StoreSchema.UpgradesFrom.Contains(identity)
                    || (StoreSchema.Refused.TryGetValue(identity, out var reason) && !string.IsNullOrWhiteSpace(reason))).IsTrue();
        }
    }
}
