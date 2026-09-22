using System.Diagnostics;
using System.Text;

namespace Broodling.Tests;

/// <summary>Create files a test later executes without this process holding a write descriptor to them.</summary>
/// <remarks>A concurrent fork inherits every open descriptor until it execs, and Linux refuses to execute a file
/// that remains open for writing (ETXTBSY). A child writer's descriptor is never shared with this process's other
/// children (#151).</remarks>
internal static class ExecutableFile
{
    internal static void Write(string path, string content) => Run(content, "cat > \"$1\"", path);

    internal static void Copy(string source, string path) => Run(null, "cp \"$1\" \"$2\"", source, path);

    private static void Run(string? input, string script, params string[] arguments)
    {
        var start = new ProcessStartInfo("/bin/sh") { RedirectStandardInput = true };
        foreach (var argument in new[] { "-c", script, "sh" }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var writer = Process.Start(start)!;
        if (input is not null) writer.StandardInput.BaseStream.Write(Encoding.UTF8.GetBytes(input));
        writer.StandardInput.Close();
        writer.WaitForExit();
        if (writer.ExitCode != 0) throw new IOException("Cannot create test executable " + arguments[^1]);
    }
}
