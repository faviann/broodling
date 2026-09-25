using System.Diagnostics;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// The actual DirectTarget image built from the pinned SDK's native binary, and its controlled
/// provider/forge layer. Each is built once per test run and removed afterwards. The pinned
/// zeroshot-tls image is pulled only when absent, and then removed afterwards too.
/// </summary>
internal static class TargetImage
{
    internal static readonly Lazy<Task<string>> Direct = new(BuildDirect);
    internal static readonly Lazy<Task<string>> Controlled = new(BuildControlled);
    internal static readonly Lazy<Task<string>> Tls = new(PullTls);
    private static string? pulledTls;

    private static async Task<string> BuildDirect()
    {
        var deployment = Path.Combine(NativeFixture.RepositoryRoot, "deployment");
        var tag = "broodling-startup-test:" + Guid.NewGuid().ToString("N");
        RequireSuccess(await DockerCommand("build", "--tag", tag, "--file", Path.Combine(deployment, "DirectTarget.Dockerfile"), deployment));
        return tag;
    }

    private static async Task<string> BuildControlled()
    {
        var tag = "broodling-stock-target-test:" + Guid.NewGuid().ToString("N");
        RequireSuccess(await DockerCommand("build", "--tag", tag, "--build-arg", "BASE=" + await Direct.Value,
            Path.Combine(NativeFixture.RepositoryRoot, "tests", "fixtures", "stock-target")));
        return tag;
    }

    private static async Task<string> PullTls()
    {
        if ((await DockerCommand("image", "inspect", TargetReadiness.TlsImage)).Code != 0)
        {
            RequireSuccess(await DockerCommand("pull", "--quiet", TargetReadiness.TlsImage));
            var pulled = await DockerCommand("image", "inspect", "--format", "{{.Id}}", TargetReadiness.TlsImage);
            RequireSuccess(pulled);
            pulledTls = pulled.Output.Trim();
        }
        return TargetReadiness.TlsImage;
    }

    [After(Assembly)]
    public static async Task RemoveImages()
    {
        foreach (var image in new[] { Controlled, Direct })
            if (image.IsValueCreated && image.Value.IsCompletedSuccessfully)
                RequireSuccess(await DockerCommand("image", "rm", await image.Value));
        // Best effort: a concurrent run may still use the pinned image it found present, and then keeps it.
        if (pulledTls is not null) await DockerCommand("image", "rm", pulledTls);
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
