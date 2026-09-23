using Microsoft.Data.Sqlite;

namespace Broodling;

/// <summary>
/// Persisted pause state and durable initiation evidence. The counts are outstanding
/// initiation rows that have not recorded completion; they do not assert that an
/// owning process is currently live or that an unobserved external request stopped.
/// </summary>
public sealed record InstallationStatus(bool IsPaused, string ChangedAt,
    int OutstandingAdmissions, int OutstandingPreparations, int OutstandingDispatches, int UnresolvedDispatches)
{
    public int OutstandingInitiations => OutstandingAdmissions + OutstandingPreparations + OutstandingDispatches;
    public bool InFlightInitiationDrained => OutstandingInitiations == 0;
}

public sealed partial class BroodlingStore
{
    private enum InitiationKind
    {
        Admission,
        Preparation,
        Dispatch
    }

    private sealed class InitiationLease(BroodlingStore store, string initiationId) : IDisposable
    {
        private BroodlingStore? owner = store;
        private readonly string initiationId = initiationId;
        private int retainOutstanding;

        public void RetainOutstanding() => Volatile.Write(ref retainOutstanding, 1);

        public void Complete()
        {
            Volatile.Write(ref retainOutstanding, 0);
            Interlocked.Exchange(ref owner, null)?.EndInitiation(initiationId);
        }

        public void Dispose()
        {
            var store = Interlocked.Exchange(ref owner, null);
            if (store is not null && Volatile.Read(ref retainOutstanding) == 0)
                store.EndInitiation(initiationId);
        }
    }

    public InstallationStatus GetInstallationStatus()
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        var result = ReadInstallationStatus(transaction);
        transaction.Commit();
        return result;
    }

    public InstallationStatus PauseInstallation() => SetInstallationPause(true);

    public InstallationStatus ReleaseInstallation() => SetInstallationPause(false);

    private InstallationStatus SetInstallationPause(bool paused)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        var current = ReadInstallationControl(transaction);
        if (current.IsPaused != paused)
            Execute("UPDATE installation_control SET admission_dispatch_paused = $p0, changed_at = $p1 WHERE singleton = 1",
                transaction, paused ? 1 : 0, Now());
        var result = ReadInstallationStatus(transaction);
        transaction.Commit();
        return result;
    }

    private InitiationLease BeginInitiation(InitiationKind kind, string? attemptId = null, bool permitWhilePaused = false)
    {
        var initiationId = "init-" + Guid.NewGuid().ToString("N");
        using var transaction = connection.BeginTransaction(deferred: false);
        if (!permitWhilePaused && ReadInstallationControl(transaction).IsPaused)
            throw new InstallationPaused();
        Execute("INSERT INTO installation_initiations VALUES ($p0, $p1, $p2, $p3)", transaction,
            initiationId, KindName(kind), attemptId, Now());
        transaction.Commit();
        return new(this, initiationId);
    }

    private void EndInitiation(string initiationId)
    {
        try
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            Execute("DELETE FROM installation_initiations WHERE initiation_id = $p0", transaction, initiationId);
            transaction.Commit();
        }
        catch (SqliteException)
        {
            // Do not claim quiescence when cleanup cannot be durably recorded. The
            // row remains outstanding; this scope has no safe adoption or clearing
            // operation because caller loss does not prove external work stopped.
        }
    }

    private InstallationStatus ReadInstallationStatus(SqliteTransaction transaction)
    {
        var control = ReadInstallationControl(transaction);
        var counts = new int[3];
        var unknownDispatches = 0;
        using var command = Command("""
            SELECT i.initiation_id, i.kind, s.state
            FROM installation_initiations AS i
            LEFT JOIN native_submissions AS s ON s.attempt_id = i.attempt_id
            """, transaction);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            var kind = rows.GetString(1) switch
            {
                "admission" => 0,
                "preparation" => 1,
                "dispatch" => 2,
                _ => throw new StoreStateException("incompatible_store", "The installation initiation state is invalid.")
            };
            if (kind == 2 && !rows.IsDBNull(2) && rows.GetString(2) == "dispatched")
                unknownDispatches++;
            counts[kind]++;
        }
        return new(control.IsPaused, control.ChangedAt, counts[0], counts[1], counts[2], unknownDispatches);
    }

    private (bool IsPaused, string ChangedAt) ReadInstallationControl(SqliteTransaction transaction)
    {
        using var command = Command("SELECT admission_dispatch_paused, changed_at FROM installation_control WHERE singleton = 1", transaction);
        using var row = command.ExecuteReader();
        if (!row.Read() || row.GetInt64(0) is not (0 or 1) || string.IsNullOrEmpty(row.GetString(1)))
            throw new StoreStateException("incompatible_store", "The installation control state is invalid.");
        return (row.GetInt64(0) == 1, row.GetString(1));
    }

    private static string KindName(InitiationKind kind) => kind switch
    {
        InitiationKind.Admission => "admission",
        InitiationKind.Preparation => "preparation",
        InitiationKind.Dispatch => "dispatch",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

}
