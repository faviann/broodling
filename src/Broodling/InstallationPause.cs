using Microsoft.Data.Sqlite;

namespace Broodling;

/// <summary>
/// Persisted pause state. Admission, preparation and dispatch check the pause in the
/// same SQLite write transaction that commits them, so none can take effect after a
/// pause commits. <see cref="UnresolvedDispatches"/> counts durable <c>dispatched</c>
/// submissions whose native run is unknown; that uncertainty can remain forever.
/// <see cref="InFlightInitiationDrained"/> is false while any process still holds the
/// initiation lock that every dispatch takes before its intent commits. Neither proves
/// that a native process, target request or container stopped.
/// </summary>
public sealed record InstallationStatus(bool IsPaused, string ChangedAt, int UnresolvedDispatches, bool InFlightInitiationDrained);

public sealed partial class BroodlingStore
{
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

    /// <summary>Without a transaction this only fails fast; the committing transaction must check again.</summary>
    private void RequireUnpaused(SqliteTransaction? transaction = null)
    {
        if (ReadInstallationControl(transaction).IsPaused)
            throw new InstallationPaused();
    }

    /// <summary>The explicit replacement path may allocate and prepare, never dispatch, while paused.</summary>
    private void RequireUnpausedUnlessReplacement(AttemptRecord attempt, SqliteTransaction transaction)
    {
        if (attempt.Retry is null)
            RequireUnpaused(transaction);
    }

    private InstallationStatus ReadInstallationStatus(SqliteTransaction transaction)
    {
        var control = ReadInstallationControl(transaction);
        using var command = Command("SELECT count(*) FROM native_submissions WHERE state = 'dispatched'", transaction);
        return new(control.IsPaused, control.ChangedAt, Convert.ToInt32(command.ExecuteScalar()),
            AdministrativeGitProcess.EnclosureLock.IsFree(Path));
    }

    private AdministrativeGitProcess.EnclosureLock HoldInitiation() =>
        AdministrativeGitProcess.EnclosureLock.AcquireExisting(Path, shared: true);

    private (bool IsPaused, string ChangedAt) ReadInstallationControl(SqliteTransaction? transaction)
    {
        using var command = Command("SELECT admission_dispatch_paused, changed_at FROM installation_control WHERE singleton = 1", transaction);
        using var row = command.ExecuteReader();
        if (!row.Read() || row.GetInt64(0) is not (0 or 1) || string.IsNullOrEmpty(row.GetString(1)))
            throw new StoreStateException("incompatible_store", "The installation control state is invalid.");
        return (row.GetInt64(0) == 1, row.GetString(1));
    }
}
