using Microsoft.Data.Sqlite;
using System.Text;

namespace Broodling;

/// <summary>
/// One application session over durable SQLite facts. Dispose after use and do not
/// share across threads. Separate sessions serialize writes in SQLite.
/// </summary>
public sealed partial class BroodlingStore : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly SqliteConnection connection;
    public string Path { get; }
    public StoreInformation Information { get; private set; } = null!;

    private BroodlingStore(string path)
    {
        Path = path;
        connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 10
        }.ToString());
    }

    internal static BroodlingStore Initialize(string path)
    {
        var target = StorePath(path);
        if (File.Exists(target + "-wal") || File.Exists(target + "-shm") || File.Exists(target + "-journal"))
            throw new StoreStateException("store_exists", "SQLite sidecar state already exists; inspect or restore it.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        try
        {
            using var reservation = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) when (File.Exists(target) || Directory.Exists(target))
        {
            throw new StoreStateException("store_exists", "Initialization requires a new path; existing state was not replaced.");
        }
        var store = new BroodlingStore(target);
        try
        {
            store.connection.Open();
            using (var transaction = store.connection.BeginTransaction(deferred: false))
            {
                store.Execute(StoreSchema.Sql, transaction);
                var now = Now();
                store.Execute("INSERT INTO store_metadata VALUES (1, $p0, $p1, $p2, $p3, $p4)", transaction,
                    StoreSchema.Format, StoreSchema.Version, StoreSchema.DefinitionHash,
                    StoreSchema.ManifestHash(store.connection, transaction), now);
                transaction.Commit();
                store.Information = new(StoreSchema.Format, StoreSchema.Version, now);
            }
            store.Configure();
            return store;
        }
        catch
        {
            // A partial new file is retained for inspection, never silently replaced.
            store.Dispose();
            throw;
        }
    }

    internal static BroodlingStore Open(string path)
    {
        var target = StorePath(path);
        if (!File.Exists(target))
            throw new StoreStateException("store_missing", "The store does not exist; initialize a new installation explicitly or restore existing state.");
        var store = new BroodlingStore(target);
        try
        {
            store.connection.Open();
            store.RequireCurrentSchema();
            store.Configure();
            return store;
        }
        catch (SqliteException)
        {
            store.Dispose();
            throw new StoreStateException("incompatible_store", "The file is not a readable, compatible .NET Broodling store.");
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    /// <summary>Upgrade recognized .NET state explicitly; never import Python state.</summary>
    internal static BroodlingStore Upgrade(string path)
    {
        var target = StorePath(path);
        if (!File.Exists(target))
            throw new StoreStateException("store_missing", "The store does not exist; initialize new state explicitly.");
        var store = new BroodlingStore(target);
        try
        {
            store.connection.Open();
            using (var transaction = store.connection.BeginTransaction(deferred: false))
            {
                store.RequireCurrentSchema(transaction, allowUpgrade: true);
                if (store.Information.SchemaVersion == 1)
                    store.Execute(StoreSchema.AdmissionSql, transaction);
                if (store.Information.SchemaVersion < 3)
                    store.Execute(StoreSchema.AttemptSql, transaction);
                if (store.Information.SchemaVersion < 4)
                    store.Execute(StoreSchema.ProvisioningSql, transaction);
                if (store.Information.SchemaVersion < 5)
                    store.Execute(StoreSchema.DispatchSql, transaction);
                if (store.Information.SchemaVersion < 6)
                    store.Execute(StoreSchema.CompletionSql, transaction);
                if (store.Information.SchemaVersion < 7)
                    store.Execute(StoreSchema.RetirementSql, transaction);
                if (store.Information.SchemaVersion < 8)
                    store.Execute(StoreSchema.IssueSubmissionSql, transaction);
                if (store.Information.SchemaVersion < StoreSchema.Version)
                {
                    store.Execute("UPDATE store_metadata SET version = $p0, definition_hash = $p1, manifest_hash = $p2 WHERE singleton = 1",
                        transaction, StoreSchema.Version, StoreSchema.DefinitionHash,
                        StoreSchema.ManifestHash(store.connection, transaction));
                }
                transaction.Commit();
            }
            store.RequireCurrentSchema();
            store.Configure();
            return store;
        }
        catch (SqliteException)
        {
            store.Dispose();
            throw new StoreStateException("incompatible_store", "The file is not a readable, compatible .NET Broodling store.");
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    public void Dispose() => connection.Dispose();

    private void RequireCurrentSchema(SqliteTransaction? transaction = null, bool allowUpgrade = false)
    {
        using var command = Command("SELECT format, version, definition_hash, manifest_hash, initialized_at FROM store_metadata WHERE singleton = 1", transaction);
        using var reader = command.ExecuteReader();
        if (!reader.Read()
            || reader.GetValue(0) is not string format || format != StoreSchema.Format
            || reader.GetValue(1) is not long version || (version != StoreSchema.Version && !(allowUpgrade && version is 1 or 2 or 3 or 4 or 5 or 6 or 7))
            || reader.GetValue(2) is not string definition
                || definition != (version switch { 1 => StoreSchema.VersionOneDefinitionHash, 2 => StoreSchema.VersionTwoDefinitionHash, 3 => StoreSchema.VersionThreeDefinitionHash, 4 => StoreSchema.VersionFourDefinitionHash, 5 => StoreSchema.VersionFiveDefinitionHash, 6 => StoreSchema.VersionSixDefinitionHash, 7 => StoreSchema.VersionSevenDefinitionHash, _ => StoreSchema.DefinitionHash })
            || reader.GetValue(3) is not string manifest || manifest != StoreSchema.ManifestHash(connection, transaction)
            || reader.GetValue(4) is not string initializedAt || string.IsNullOrEmpty(initializedAt))
            throw new StoreStateException("incompatible_store", "The store format or schema is incompatible; explicit supported upgrades are required.");
        Information = new(format, (int)version, initializedAt);
    }

    private void Configure()
    {
        Execute("PRAGMA synchronous = FULL");
        Execute("PRAGMA journal_mode = WAL");
    }

    private static string StorePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new StoreStateException("invalid_store_path", "A filesystem store path is required.");
        string current;
        try { current = PhysicalPaths.Resolve(path); }
        catch (IOException error) { throw new StoreStateException("invalid_store_path", error.Message); }
        if (PhysicalPaths.IsWithinDisposable(current))
            throw new StoreStateException("invalid_store_location", "The application store must outlive disposable Attempt worktrees.");
        return current;
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("O");

    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null, params object?[] values)
    {
        // Provider replacement applies to lookup keys as well as retained text.
        // Validate before binding so malformed callers cannot alias existing facts.
        try
        {
            foreach (var value in values)
                if (value is string text)
                    StrictUtf8.GetByteCount(text);
        }
        catch (EncoderFallbackException)
        {
            throw new BroodlingException("invalid_text", "Store text contains invalid Unicode.");
        }
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        for (var index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue($"$p{index}", values[index] ?? DBNull.Value);
        return command;
    }

    private void Execute(string sql, SqliteTransaction? transaction = null, params object?[] values)
    {
        using var command = Command(sql, transaction, values);
        command.ExecuteNonQuery();
    }

    public WorkUnit ResolveWorkUnit(WorkReference reference, string? expectedWorkUnitId = null)
    {
        if (expectedWorkUnitId is not null && expectedWorkUnitId != reference.WorkUnitId)
            throw new WorkUnitIdentityConflict("The asserted Work Unit does not match the submitted reference.");
        using var transaction = connection.BeginTransaction(deferred: false);
        var stored = ReadWorkUnit(reference.WorkUnitId, transaction);
        if (stored is null)
        {
            Execute("""
                INSERT INTO work_units VALUES ($p0, $p1, $p2, $p3, $p4, $p5, $p6, $p7, $p8, $p9)
                """, transaction, reference.WorkUnitId, reference.Key, reference.Host, reference.Owner,
                reference.Repository, reference.IssueNumber, reference.IssueLocator,
                reference.RepositoryIdentity, reference.IssueIdentity, Now());
        }
        else
        {
            CheckPins(stored, reference);
            Execute("""
                UPDATE work_units SET repository_identity = COALESCE(repository_identity, $p0),
                    issue_identity = COALESCE(issue_identity, $p1) WHERE work_unit_id = $p2
                """, transaction, reference.RepositoryIdentity, reference.IssueIdentity, reference.WorkUnitId);
        }
        Execute("INSERT INTO work_submissions VALUES ($p0, $p1, $p2, $p3, $p4)", transaction,
            "sub-" + Guid.NewGuid().ToString("N"), reference.WorkUnitId,
            reference.SubmittedRepository, reference.SubmittedIssue, Now());
        var result = ReadWorkUnit(reference.WorkUnitId, transaction)!;
        transaction.Commit();
        return result;
    }

    public WorkUnit GetWorkUnit(string workUnitId) => ReadWorkUnit(workUnitId)
        ?? throw new UnknownRecord("Unknown Work Unit.");

    /// <summary>Observe identity without creating, pinning, or recording a submission.</summary>
    public WorkUnit? FindWorkUnit(WorkReference reference)
    {
        var result = ReadWorkUnit(reference.WorkUnitId);
        if (result is not null)
            CheckPins(result, reference);
        return result;
    }

    private static void CheckPins(WorkUnit stored, WorkReference reference)
    {
        if ((stored.RepositoryIdentity is not null && reference.RepositoryIdentity is not null
                && stored.RepositoryIdentity != reference.RepositoryIdentity)
            || (stored.IssueIdentity is not null && reference.IssueIdentity is not null
                && stored.IssueIdentity != reference.IssueIdentity))
            throw new WorkUnitIdentityConflict("The submitted upstream identities conflict with this Work Unit's retained pins.");
    }

    private WorkUnit? ReadWorkUnit(string id, SqliteTransaction? transaction = null)
    {
        using var command = Command("SELECT * FROM work_units WHERE work_unit_id = $p0", transaction, id);
        using var row = command.ExecuteReader();
        return !row.Read() ? null : new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3),
            row.GetString(4), row.GetInt64(5), row.GetString(6), row.IsDBNull(7) ? null : row.GetString(7),
            row.IsDBNull(8) ? null : row.GetString(8), row.GetString(9));
    }

    public IReadOnlyList<WorkSubmission> ListWorkSubmissions(string workUnitId)
    {
        using var command = Command("SELECT * FROM work_submissions WHERE work_unit_id = $p0 ORDER BY received_at, submission_id", null, workUnitId);
        using var row = command.ExecuteReader();
        var result = new List<WorkSubmission>();
        while (row.Read())
            result.Add(new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4)));
        return result;
    }

    public EntitledSource EntitleSource(string workUnitId, SourceSubmission submission)
    {
        var work = GetWorkUnit(workUnitId);
        var grant = submission.EvaluateEntitlement();
        if (submission.Kind == "primary_issue" && submission.Locator != work.IssueLocator)
            throw new SourceNotEntitled("The primary issue source must name this Work Unit's canonical issue locator.");
        var bytes = submission.Content;
        var hash = Digests.Bytes(bytes);
        var id = "src-" + Digests.Parts("broodling.entitled-source.v1", workUnitId, submission.Kind, submission.Locator, hash);
        using var transaction = connection.BeginTransaction(deferred: false);
        var now = Now();
        Execute("""
            INSERT INTO entitled_sources VALUES ($p0, $p1, $p2, $p3, $p4, $p5, $p6, $p7, $p8, $p9, $p10, $p11)
            ON CONFLICT (source_id) DO NOTHING
            """, transaction, id, workUnitId, submission.Kind, submission.Locator, bytes, hash,
            submission.MediaType, submission.Origin, grant.GrantedBy, grant.Basis,
            string.IsNullOrEmpty(submission.RetrievedAt) ? now : submission.RetrievedAt, now);
        var result = ReadSource(id, transaction);
        transaction.Commit();
        return result;
    }

    public EntitledSource GetEntitledSource(string sourceId) => ReadSource(sourceId);

    private EntitledSource ReadSource(string sourceId, SqliteTransaction? transaction = null)
    {
        using var command = Command("SELECT * FROM entitled_sources WHERE source_id = $p0", transaction, sourceId);
        using var row = command.ExecuteReader();
        if (!row.Read())
            throw new UnknownRecord("Unknown entitled source.");
        return new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), (byte[])row[4],
            row.GetString(5), row.GetString(6), row.GetString(7), row.GetString(8), row.GetString(9),
            row.GetString(10), row.GetString(11));
    }

    public IReadOnlyList<EntitledSource> ListEntitledSources(string workUnitId)
    {
        using var command = Command("SELECT source_id FROM entitled_sources WHERE work_unit_id = $p0 ORDER BY source_id", null, workUnitId);
        using var row = command.ExecuteReader();
        var result = new List<EntitledSource>();
        while (row.Read())
            result.Add(ReadSource(row.GetString(0)));
        return result;
    }
}
