using System.Text;
using System.Text.Json;
using Zeroshot;
using Zeroshot.Native;
using Zeroshot.Native.Contracts;

namespace Broodling;

/// <summary>
/// The stock full-run submission through the SDK. It classifies one SDK attempt into Broodling's
/// outcomes only; durable intent, correlation and conflict retention belong to the application.
/// </summary>
internal static class DirectTargetSubmission
{
    /// <summary>
    /// The SDK's retained form of a frozen request: exactly these UTF-8 bytes, validated as a target
    /// submission. Nothing is regenerated, so an export returns the same bytes.
    /// </summary>
    internal static PreparedSubmission Import(string requestJson)
    {
        try { return PreparedSubmission.ImportUtf8(Encoding.UTF8.GetBytes(requestJson)); }
        catch (Exception error) when (error is JsonException or ArgumentException)
        { throw new UnsupportedRuntime("The frozen request is not a supported DirectTarget submission."); }
    }

    /// <summary>
    /// One <see cref="ZeroshotClient.SubmitAttemptAsync"/> of the exact retained bytes with the current
    /// credentials, within the 60-second submit budget. It never retries. Returns only when a valid
    /// acknowledgement names exactly the intended run; another named run is <c>foreign_run</c>, never adopted,
    /// and reported only when it is a canonical UUID. A pinned <c>request.conflict</c> refusal is <see cref="SubmissionConflict"/>; every
    /// other outcome is a fixed <see cref="NativeTransportError"/> kind or the caller's cancellation.
    /// </summary>
    internal static async Task SubmitAsync(Uri origin, string? rootCertificate, PreparedSubmission prepared,
        TargetRunCredentials credentials, TimeProvider clock, CancellationToken caller)
    {
        // Current credentials enter only the SDK's in-memory body, never the retained request or a diagnostic.
        using var deadline = new CancellationTokenSource(DirectTargetLimits.Submit, clock);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
        TargetSubmissionAttempt attempt;
        await using (var client = DirectTargetClient.Open(origin, rootCertificate))
            attempt = await client.SubmitAttemptAsync(prepared, credentials, budget.Token);

        // A captured valid acknowledgement wins a racing cancellation, so it is settled before cancellation.
        if (attempt.AcknowledgedRunId is { Value: var acknowledged })
        {
            if (acknowledged == prepared.RunId.Value) return;
            // The ID is target-controlled text: it is reported only in the canonical form Broodling itself
            // retains (lowercase UUID), never as arbitrary text. A re-cased intended ID is foreign too.
            throw new NativeTransportError("foreign_run",
                Guid.TryParseExact(acknowledged, "D", out var id) && id.ToString() == acknowledged ? acknowledged : null);
        }
        caller.ThrowIfCancellationRequested();
        throw attempt.Failure switch
        {
            NativeHttpException { Problem.Code: "request.conflict" } when attempt.Outcome == NativeAttemptOutcome.Rejected =>
                new SubmissionConflict("The target reports a conflicting immutable submission."),
            _ when deadline.IsCancellationRequested => new NativeTransportError("TimeoutError"),
            NativeHttpException http => new NativeTransportError(http switch
            {
                { Kind: NativeHttpFailureKind.HttpStatus, Problem: not null } => "TargetError",
                { Kind: NativeHttpFailureKind.HttpStatus or NativeHttpFailureKind.Protocol } => "invalid_response",
                { Kind: NativeHttpFailureKind.SizeLimit } when attempt.Outcome == NativeAttemptOutcome.NotSent => "request_too_large",
                { Kind: NativeHttpFailureKind.SizeLimit } => "invalid_response",
                { Kind: NativeHttpFailureKind.Deadline } => "TimeoutError",
                _ => "transport_failed"
            }),
            NativeBindingException => new UnsupportedRuntime("The SDK refuses the DirectTarget binding's native release."),
            _ => new NativeTransportError()
        };
    }
}
