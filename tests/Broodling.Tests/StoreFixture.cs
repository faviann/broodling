using Microsoft.Data.Sqlite;

namespace Broodling.Tests;

internal sealed class StoreFixture : IDisposable
{
    internal string Root { get; } = Directory.CreateDirectory(System.IO.Path.Combine(
        Environment.GetEnvironmentVariable("BROODLING_TEST_WORKSPACE_ROOT")
            ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "broodling-tests"),
        "dotnet-" + Guid.NewGuid().ToString("N"))).FullName;
    internal string Path => System.IO.Path.Combine(Root, "state", "broodling.sqlite3");
    internal BroodlingApplication Application { get; } = new();
    internal BroodlingStore Initialize() => Application.InitializeStore(Path);
    internal BroodlingStore Open() => Application.OpenStore(Path);

    internal SqliteConnection Connect()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, ForeignKeys = true
        }.ToString());
        connection.Open();
        return connection;
    }

    internal void Execute(string sql)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
