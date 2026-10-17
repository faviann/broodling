using System.Diagnostics;
using System.Text.Json.Nodes;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// The adopted target environment as its committed adoption record,
/// <c>src/Broodling/environments/dotnet-r6.adoption.json</c>, names it: the image by digest and the retained
/// manifest bytes with their digest.
/// </summary>
internal static class Adopted
{
    internal static readonly string Directory = Path.Combine(TestRepository.Root, "src", "Broodling", "environments");
    internal static string RecordFile => Path.Combine(Directory, "dotnet-r6.adoption.json");
    internal static JsonNode Record { get; } = JsonNode.Parse(File.ReadAllText(RecordFile))!;
    internal static string Release => (string)Record["environment"]!;
    internal static string Image => (string)Record["image"]!;
    internal static string Manifest => Path.Combine(Directory, (string)Record["manifest"]!["path"]!);
    internal static string ManifestSha256 => (string)Record["manifest"]!["sha256"]!;
}

/// <summary>
/// The actual DirectTarget image, the adopted environment image pulled by its digest, and its controlled
/// provider/forge layer. Each image is made once per test run, and everything the run built or pulled is
/// removed afterwards.
/// </summary>
internal static class TargetImage
{
    internal static readonly Lazy<Task<string>> Direct = new(() => Pinned(Adopted.Image));
    internal static readonly Lazy<Task<string>> Controlled = new(async () => await ControlledOver(await Direct.Value));
    internal static readonly Lazy<Task<string>> Tls = new(() => Pinned(TargetReadiness.TlsImage));
    private static readonly Dictionary<string, Lazy<Task<string>>> made = [];
    /// <summary>Removed in reverse, so a controlled layer goes before its base.</summary>
    private static readonly List<string> built = [];
    private static readonly List<string> pulled = [];

    /// <summary>The controlled provider/forge layer over <paramref name="image"/>; the native binary is untouched.</summary>
    internal static Task<string> ControlledOver(string image) => Once("controlled " + image, async () =>
    {
        var tag = "broodling-stock-target-test:" + Guid.NewGuid().ToString("N");
        RequireSuccess(await DockerCommand("build", "--tag", tag, "--build-arg", "BASE=" + image,
            Path.Combine(TestRepository.Root, "tests", "fixtures", "stock-target")));
        lock (built) built.Add(tag);
        return tag;
    });

    /// <summary>
    /// A published image by digest reference, pulled only when absent. An image this run pulled is removed
    /// afterwards on a best-effort basis: a concurrent run may still use the image it found present.
    /// </summary>
    internal static Task<string> Pinned(string reference) => Once("pinned " + reference, async () =>
    {
        if ((await DockerCommand("image", "inspect", reference)).Code != 0)
        {
            RequireSuccess(await DockerCommand("pull", "--quiet", reference));
            var id = await DockerCommand("image", "inspect", "--format", "{{.Id}}", reference);
            RequireSuccess(id);
            lock (pulled) pulled.Add(id.Output.Trim());
        }
        return reference;
    });

    /// <summary>An image this run made some other way, removed afterwards in reverse order with the images it built.</summary>
    internal static void Owned(string image)
    {
        lock (built) built.Add(image);
    }

    private static Task<string> Once(string key, Func<Task<string>> make)
    {
        lock (made)
        {
            if (!made.TryGetValue(key, out var image)) made[key] = image = new(make);
            return image.Value;
        }
    }

    [After(Assembly)]
    public static async Task RemoveImages()
    {
        foreach (var image in Enumerable.Reverse(built)) RequireSuccess(await DockerCommand("image", "rm", image));
        foreach (var image in pulled) await DockerCommand("image", "rm", image);
    }

    internal sealed record Result(int Code, string Output, string Error);
    internal static Task<Result> DockerCommand(params string[] arguments) => Run("docker", arguments);
    internal static void RequireSuccess(Result result)
    {
        if (result.Code != 0) throw new InvalidOperationException($"Command exited {result.Code}: {result.Output}{result.Error}");
    }
    private static async Task<Result> Run(string executable, params string[] arguments)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return new(process.ExitCode, await output, await error);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
