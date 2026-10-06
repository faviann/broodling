using System.Net;
using System.Security.Cryptography;
using System.Text;
using Zeroshot.Native;
using Zeroshot.Native.Contracts;

namespace Broodling;

public enum DirectTargetBootstrapResult
{
    /// <summary>This operation's envelope installed the configured token.</summary>
    Installed,
    /// <summary>The target already accepted the configured token, so nothing was sent or the acknowledgement was lost.</summary>
    AlreadyInstalled
}

/// <summary>A bootstrap that cannot install the configured token; the message is safe operator output.</summary>
public sealed class DirectTargetBootstrapFailed(string message) : BroodlingException("bootstrap_failed", message);

/// <summary>
/// The private control boundary of a DirectTarget started in native's private mode: installing the configured
/// control token through native's one-time encrypted bootstrap, and checking that the target accepts it.
/// Neither ever submits work, touches native state or reports secret material.
/// </summary>
public static class DirectTargetControl
{
    /// <summary>Native private_access.rs: the envelope's purpose-bound associated data.</summary>
    private static readonly byte[] EnvelopeDomain = "zeroshot-capsule-bootstrap-v1"u8.ToArray();
    internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Install <paramref name="access"/>'s control token for <paramref name="origin"/> in the target process now
    /// serving it, which every target-process start needs, including a restart over existing native state.
    /// <paramref name="bootstrapKeyFile"/> holds the same bootstrap key the target was started with. A target
    /// that already accepts the token is left alone, so repeating this after a lost acknowledgement or on a
    /// bootstrapped target sends no envelope. Otherwise one envelope is sent and the result confirmed by
    /// authenticating; a refused, closed or uncertain bootstrap is never resent blindly and fails with
    /// <see cref="DirectTargetBootstrapFailed"/>, leaving the target's state and any installed token unchanged.
    /// One 30-second budget covers it all.
    /// </summary>
    public static Task<DirectTargetBootstrapResult> BootstrapAsync(string origin, DirectTargetAccess access, string bootstrapKeyFile,
        CancellationToken cancellationToken = default) =>
        BootstrapAsync(origin, access, bootstrapKeyFile, null, TimeProvider.System, cancellationToken);

    /// <param name="http">A client that already trusts the root, such as one bound to zeroshot-tls's published port; null connects by name.</param>
    internal static async Task<DirectTargetBootstrapResult> BootstrapAsync(string origin, DirectTargetAccess access, string bootstrapKeyFile,
        HttpClient? http, TimeProvider clock, CancellationToken cancellationToken)
    {
        var target = DirectTargetExchange.CanonicalOrigin(origin)
            ?? throw new UnsupportedRuntime("The DirectTarget origin must be canonical HTTPS or HTTP to exactly 127.0.0.1 or [::1].");
        var token = access.Token(target);
        var key = DirectTargetAccess.ReadSecret(bootstrapKeyFile)
            ?? throw new DirectTargetBootstrapFailed("The bootstrap key file is missing, unreadable or not exactly 64 lowercase hexadecimal characters.");
        if (key == token) throw new DirectTargetBootstrapFailed("The bootstrap key must differ from the control token.");
        var envelope = Envelope(key, token);

        NativeClient native;
        try
        {
            native = NativeClient.ForHttp(new NativeClientOptions
            {
                Origin = target, Transport = new TransportOptions { TrustedRootCertificatePath = http is null ? access.RootCertificate : null }
            }, http);
        }
        catch (ArgumentException) { throw new NativeTransportError(); } // An unreadable root sends nothing.
        await using (native)
        {
            using var budget = DirectTargetBudget.Start(Budget, clock, cancellationToken);
            var discovery = await Discover(native, budget);
            if (await AcceptsAsync(native, discovery, access.Credentials(target), budget)) return DirectTargetBootstrapResult.AlreadyInstalled;
            var attempt = await budget.RunAsync(token => native.Private.BootstrapAsync(discovery, envelope, token));
            var accepted = attempt.Outcome != NativeAttemptOutcome.NotSent
                && await AcceptsAsync(native, discovery, access.Credentials(target), budget);
            return (attempt.Outcome, (attempt.Failure as NativeHttpException)?.StatusCode, accepted) switch
            {
                (NativeAttemptOutcome.Acknowledged or NativeAttemptOutcome.Unknown, _, true) => DirectTargetBootstrapResult.Installed,
                // Closed by another envelope with this token, such as a concurrent or earlier lost one.
                (NativeAttemptOutcome.Rejected, HttpStatusCode.NotFound, true) => DirectTargetBootstrapResult.AlreadyInstalled,
                (NativeAttemptOutcome.Rejected, HttpStatusCode.NotFound, false) => throw new DirectTargetBootstrapFailed(
                    "The target's bootstrap is closed and it refuses the configured token, so it holds another one. Its token "
                    + "is not replaced while it runs: restart the target, keeping its state, and bootstrap it again."),
                (NativeAttemptOutcome.Rejected, HttpStatusCode.BadRequest, _) => throw new DirectTargetBootstrapFailed(
                    "The target refused the bootstrap envelope: the bootstrap key differs from the one it was started with. "
                    + "Its bootstrap remains open."),
                (NativeAttemptOutcome.Unknown, _, false) => throw new DirectTargetBootstrapFailed(
                    "The bootstrap's outcome is unknown and the target does not accept the configured token yet. "
                    + "Run the bootstrap again; it sends nothing once the target accepts the token."),
                (NativeAttemptOutcome.NotSent, _, _) => throw new NativeTransportError(),
                _ => throw new DirectTargetBootstrapFailed("The target answered the bootstrap unexpectedly and does not accept the configured token.")
            };
        }
    }

    /// <summary>
    /// Whether the target accepts <paramref name="credentials"/> for control: one authenticated OECP session
    /// request, which issues a session but neither submits nor changes anything. A refusal is false; any other
    /// failure is a fixed <see cref="NativeTransportError"/>.
    /// </summary>
    internal static async Task<bool> AcceptsAsync(NativeClient native, TargetDiscoveryDocument discovery,
        TargetControlCredentials credentials, DirectTargetBudget budget)
    {
        try
        {
            await budget.RunAsync(token => native.Target.CreateOecpSessionAsync(discovery, null, credentials, token));
            return true;
        }
        catch (NativeHttpException error) when (error is { Kind: NativeHttpFailureKind.HttpStatus, StatusCode: HttpStatusCode.Unauthorized })
        { return false; }
        catch (NativeHttpException) { throw new NativeTransportError(); }
        catch (ArgumentException) { throw new UnsupportedRuntime("Target discovery differs from the supported private DirectTarget."); }
    }

    /// <summary>The target's discovery, which must advertise native's private capability and bootstrap route.</summary>
    internal static async Task<TargetDiscoveryDocument> Discover(NativeClient native, DirectTargetBudget budget)
    {
        TargetDiscoveryDocument discovery;
        try { discovery = await budget.RunAsync(token => native.Target.DiscoverAsync(token)); }
        catch (NativeHttpException) { throw new NativeTransportError(); }
        if (discovery.Authentication != TargetAuthentication.PrivateCapability || discovery.PrivateBootstrapPath != DirectTargetDiscovery.BootstrapPath)
            throw new UnsupportedRuntime("The target does not serve native private access; start it with its bootstrap key.");
        return discovery;
    }

    /// <summary>Native's caller-prepared AES-256-GCM envelope of the 64-character token under the bootstrap key.</summary>
    private static TargetPrivateBootstrapRequest Envelope(string key, string token)
    {
        var secret = Convert.FromHexString(key);
        var plaintext = Encoding.ASCII.GetBytes(token);
        var ciphertext = new byte[plaintext.Length + AesGcm.TagByteSizes.MaxSize];
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
            using var aes = new AesGcm(secret, AesGcm.TagByteSizes.MaxSize);
            aes.Encrypt(nonce, plaintext, ciphertext.AsSpan(0, plaintext.Length), ciphertext.AsSpan(plaintext.Length), EnvelopeDomain);
            return new() { Nonce = Convert.ToHexStringLower(nonce), Ciphertext = Convert.ToHexStringLower(ciphertext) };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
