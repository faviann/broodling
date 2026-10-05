using System.Diagnostics;

namespace Broodling.Tests;

/// <summary>The checked-out repository that the test host runs from.</summary>
internal static class TestRepository
{
    internal static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Broodling.sln"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Cannot locate test repository.");
    }
}

internal static class Polling
{
    internal static async Task WaitUntil(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("The condition did not settle.");
            await Task.Delay(10);
        }
    }
}
