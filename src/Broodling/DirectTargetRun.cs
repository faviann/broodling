using System.Text.Json;
using Zeroshot;
using Zeroshot.Native;
using Zeroshot.Native.Contracts;

namespace Broodling;

public sealed record NativeResult(string RunId, bool Succeeded, JsonElement Output, string? Failure);

/// <summary>The SDK's current run phase and the nodes of its active executions.</summary>
public sealed record NativeProgress(string Phase, IReadOnlyList<string> ActiveNodes);

/// <summary>Whether a stop sent force: never, possibly without a confirmed outcome, or with a terminal result.</summary>
internal enum DirectTargetForce { NotSent, Uncertain, Terminal }

/// <summary>
/// A stop's transport outcome. <see cref="Reason"/> is a fixed error kind unless force reached
/// <see cref="DirectTargetForce.Terminal"/>. None proves physical cessation.
/// </summary>
internal sealed record DirectTargetStop(DirectTargetForce Force, string? Reason);

/// <summary>
/// Persistence-free DirectTarget run operations, each over its own SDK client reconnected by exactly
/// the retained target, run ID and native binding. They decide only budgets, sequencing and
/// Broodling's fixed failure kinds; what a result or stop means belongs to the application.
/// Reading a run never establishes correlation or consumes a result.
/// </summary>
internal static class DirectTargetRun
{
    private static readonly string[] PassedFailures = ["force_stopped", "runtime_lost", "runtime_failed"];
    private static readonly JsonElement Null = JsonDocument.Parse("null").RootElement.Clone();

    /// <summary>One status read within <paramref name="bound"/>, setup included.</summary>
    internal static async Task<NativeProgress> ProgressAsync(NativeRunBinding run, DirectTargetAccess? access, TimeSpan bound,
        TimeProvider clock, CancellationToken caller)
    {
        using var budget = DirectTargetBudget.Start(bound, clock, caller);
        var (client, handle) = Reconnect(run, access);
        await using (client) return Progress(await StatusAsync(run, handle, budget));
    }

    /// <summary>
    /// Setup and a first status read, which must name the retained run, share 30 seconds; a terminal
    /// status returns at once. Otherwise the SDK's <see cref="Run.WaitAsync(TimeSpan?, CancellationToken)"/>
    /// waits with no overall deadline, and the caller may stop observing at any time. Cancellation or
    /// any failure detaches the caller without stopping the run.
    /// </summary>
    internal static async Task<NativeResult> WaitAsync(NativeRunBinding run, DirectTargetAccess? access, TimeProvider clock,
        CancellationToken caller)
    {
        var (client, handle) = Reconnect(run, access);
        await using (client)
        {
            RunStatusResult first;
            using (var setup = DirectTargetBudget.Start(DirectTargetLimits.WaitSetup, clock, caller))
                first = await StatusAsync(run, handle, setup);
            using var waiting = DirectTargetBudget.Start(Timeout.InfiniteTimeSpan, clock, caller);
            return Result(RunResult.FromStatus(first) ?? await waiting.RunAsync(token => Sdk(() => handle.WaitAsync(null, token))));
        }
    }

    /// <summary>
    /// One 30-second budget covers setup, the intended-identity precheck, one force and waiting for its
    /// terminal result. An intended run is forced only after a valid matching status, even a finished one.
    /// A failure before force is sent is <see cref="DirectTargetForce.NotSent"/>; once it may have been
    /// sent, <see cref="DirectTargetForce.Uncertain"/>. Caller cancellation propagates.
    /// </summary>
    internal static async Task<DirectTargetStop> StopAsync(NativeRunBinding run, NativeRunIdentity identity, DirectTargetAccess? access,
        TimeProvider clock, CancellationToken caller)
    {
        using var budget = DirectTargetBudget.Start(DirectTargetLimits.Stop, clock, caller);
        var force = DirectTargetForce.NotSent;
        try
        {
            var (client, handle) = Reconnect(run, access);
            await using (client)
            {
                if (identity == NativeRunIdentity.Intended) await StatusAsync(run, handle, budget);
                var forced = await budget.RunAsync(async token =>
                {
                    var attempt = await handle.ForceAttemptAsync(token);
                    if (attempt.Outcome != NativeAttemptOutcome.NotSent) force = DirectTargetForce.Uncertain;
                    return attempt.Response ?? throw (Failure(attempt.Failure!) ?? attempt.Failure!);
                });
                Require(run, forced.Title, forced.Source, forced.Size);
                if (RunResult.FromForce(forced) is null) await budget.RunAsync(token => Sdk(() => handle.WaitAsync(null, token)));
                return new(DirectTargetForce.Terminal, null);
            }
        }
        catch (NativeTransportError error) { return new(force, error.Kind); }
        catch (UnsupportedRuntime error) { return new(force, error.Code); }
    }

    /// <summary>
    /// The one place a retained binding names its target: its canonical origin, HTTPS or HTTP to exactly
    /// 127.0.0.1 or [::1], addressed through the DirectTarget binding.
    /// </summary>
    private static (ZeroshotClient Client, Run Handle) Reconnect(NativeRunBinding run, DirectTargetAccess? access)
    {
        if (DirectTargetExchange.CanonicalOrigin(run.Origin) is not { } origin)
            throw new UnsupportedRuntime("The retained DirectTarget binding is unsupported.");
        var client = DirectTargetClient.Open(origin, access);
        try { return (client, client.GetRun(new RunReference(origin, new RunId(run.RunId), DirectTargetClient.Binding))); }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<RunStatusResult> StatusAsync(NativeRunBinding run, Run handle, DirectTargetBudget budget)
    {
        var status = await budget.RunAsync(token => Sdk(() => handle.StatusAsync(token)));
        Require(run, status.Title, status.Source, status.Size);
        return status;
    }

    /// <summary>The SDK checks the run ID; the frozen title, size and PR source must match too.</summary>
    private static void Require(NativeRunBinding run, RunTitle title, ResolvedSource source, RunSize size)
    {
        if (title.Value != run.Title || size.ToString().ToLowerInvariant() != run.Size || source.Repository.Value != run.Source.Repository
            || source.Branch.Value != run.Source.Branch || source.Revision.Value != run.Source.Revision)
            throw new NativeTransportError("foreign_run");
    }

    private static NativeProgress Progress(RunStatusResult status) => status.Status switch
    {
        AdmittedRunStatus => new("admitted", []),
        RunningRunStatus running => new("running", [.. running.ActiveExecutions.Select(execution => execution.Node.Value)]),
        StoppingRunStatus stopping => new("stopping", [.. stopping.ActiveExecutions.Select(execution => execution.Node.Value)]),
        FinishedRunStatus => new("finished", []),
        _ => throw new NativeTransportError("invalid_response")
    };

    /// <summary>Native failure labels outside the fixed allowlist become <c>native_failed</c>.</summary>
    private static NativeResult Result(RunResult result) => result.IsSuccess
        ? new(result.RunId.Value, true, result.Output ?? Null, null)
        : new(result.RunId.Value, false, Null, PassedFailures.Contains(result.FailureReason!.Value) ? result.FailureReason.Value : "native_failed");

    private static async Task<T> Sdk<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (Exception error) when (Failure(error) is { } fixedKind) { throw fixedKind; }
    }

    /// <summary>
    /// Only fixed classifications leave; remote messages and details never do. Caller cancellation and
    /// budget expiry are decided by <see cref="DirectTargetBudget"/>. Anything else is unexpected and propagates.
    /// </summary>
    private static Exception? Failure(Exception error) => error switch
    {
        RunWaitException { InnerException: { } inner } => Failure(inner),
        RunWaitException => new NativeTransportError("invalid_response"), // The watch ended without a terminal result.
        RunObservationException { Kind: RunObservationFailureKind.Protocol } => new NativeTransportError("invalid_response"),
        RunObservationException { InnerException: NativeOecpException or NativeHttpException } observation => Failure(observation.InnerException!),
        RunObservationException => new NativeTransportError(),
        NativeOecpException { RpcError.Data.Code: "NOT_FOUND" } => new NativeTransportError("RunNotFoundError"),
        NativeOecpException { RpcError.Data.Code: "UNSUPPORTED_PROTOCOL_VERSION" } =>
            new UnsupportedRuntime("Target OECP does not support the required protocol."),
        NativeOecpException oecp => new NativeTransportError(oecp.Kind switch
        {
            NativeOecpFailureKind.RpcError => "TargetError",
            NativeOecpFailureKind.Deadline => "TimeoutError",
            NativeOecpFailureKind.Protocol or NativeOecpFailureKind.SizeLimit => "invalid_response",
            _ => "transport_failed"
        }),
        NativeHttpException http => new NativeTransportError(http switch
        {
            // The target refuses these control credentials: missing bootstrap, a rotated token or another target.
            { Kind: NativeHttpFailureKind.HttpStatus, StatusCode: System.Net.HttpStatusCode.Unauthorized } => "unauthorized",
            { Kind: NativeHttpFailureKind.HttpStatus, Problem: not null } => "TargetError",
            { Kind: NativeHttpFailureKind.HttpStatus or NativeHttpFailureKind.Protocol or NativeHttpFailureKind.SizeLimit } => "invalid_response",
            { Kind: NativeHttpFailureKind.Deadline } => "TimeoutError",
            _ => "transport_failed"
        }),
        NativeBindingException => new UnsupportedRuntime("The SDK refuses the DirectTarget binding's native release."),
        _ => null
    };
}
