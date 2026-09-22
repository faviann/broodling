namespace Broodling;

internal static class PhysicalPaths
{
    internal static bool Contains(string parent, string path) =>
        path == parent || path.StartsWith(parent.TrimEnd('/') + "/", StringComparison.Ordinal);

    internal static bool IsWithinDisposable(string path)
    {
        for (DirectoryInfo? ancestor = new(path); ancestor is not null; ancestor = ancestor.Parent)
            if (File.Exists(System.IO.Path.Combine(ancestor.FullName, ".broodling-disposable-worktree")))
                return true;
        return false;
    }

    // Resolve components before '..', including links introduced by other link targets.
    // ResolveLinkTarget(true) can leave symlinks in a target's parent path.
    internal static string Resolve(string path)
    {
        var followedLinks = 0;
        string Visit(string absolute)
        {
            var resolved = System.IO.Path.GetPathRoot(absolute)!;
            foreach (var part in absolute[resolved.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".") continue;
                if (part == "..") { resolved = System.IO.Path.GetDirectoryName(resolved) ?? resolved; continue; }
                var candidate = System.IO.Path.Combine(resolved, part);
                FileSystemInfo info = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
                if (info.LinkTarget is not { } target) { resolved = candidate; continue; }
                if (++followedLinks > 40) throw new IOException("Too many symbolic links in the filesystem path.");
                resolved = Visit(System.IO.Path.IsPathRooted(target) ? target : System.IO.Path.Combine(resolved, target));
            }
            return resolved;
        }
        return Visit(System.IO.Path.IsPathFullyQualified(path) ? path : System.IO.Path.Combine(Environment.CurrentDirectory, path));
    }
}
