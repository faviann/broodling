using System.Diagnostics;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// The actual DirectTarget image and its controlled provider/forge layer. The DirectTarget image is
/// the prebuilt candidate named by <c>BROODLING_TEST_TARGET_IMAGE</c>, which is kept afterwards, or
/// else is built from this revision. Each image is made once per test run, and everything the run
/// built or pulled is removed afterwards.
/// </summary>
internal static class TargetImage
{
    internal static readonly Lazy<Task<string>> Direct = new(BuildDirect);
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
            Path.Combine(NativeFixture.RepositoryRoot, "tests", "fixtures", "stock-target")));
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

    private static async Task<string> BuildDirect()
    {
        if (Environment.GetEnvironmentVariable("BROODLING_TEST_TARGET_IMAGE") is { Length: > 0 } candidate) return candidate;
        var deployment = Path.Combine(NativeFixture.RepositoryRoot, "deployment");
        var tag = "broodling-startup-test:" + Guid.NewGuid().ToString("N");
        RequireSuccess(await DockerCommand("build", "--tag", tag, "--file", Path.Combine(deployment, "DirectTarget.Dockerfile"), deployment));
        lock (built) built.Add(tag);
        return tag;
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
