using Zeroshot;
using Zeroshot.Native;

namespace Broodling;

/// <summary>
/// Broodling's one configuration of the Zeroshot SDK for a DirectTarget: the SDK's own transport,
/// trusting the configured root, bound to the DirectTarget binding's native release and carrying that
/// origin's current private control credentials. Submission, the run reader and the stopper all connect
/// through it.
/// </summary>
internal static class DirectTargetClient
{
    /// <summary>The native release the SDK must be bound to; the SDK refuses any other before sending.</summary>
    internal static readonly NativeBinding Binding =
        NativeBinding.CallerSupplied(DirectTargetBinding.NativeRelease, DirectTargetBinding.NativeSourceRevision);

    /// <summary>
    /// A client owned by one operation; disposal closes its connections and never stops a run. Its control
    /// credentials are <paramref name="access"/>'s token for exactly <paramref name="origin"/>, read now: with
    /// none, nothing is sent and it fails as <c>credentials_unavailable</c>. For an HTTPS origin, the configured
    /// root replaces system trust on every HTTP and OECP connection, and each TLS handshake reads it again.
    /// The SDK reads it once here too: a missing or unreadable root refuses, before anything is sent, as
    /// <c>transport_failed</c>.
    /// </summary>
    internal static ZeroshotClient Open(Uri origin, DirectTargetAccess? access)
    {
        var credentials = (access ?? throw new NativeTransportError("credentials_unavailable")).Credentials(origin);
        try
        {
            return new ZeroshotClient(new ZeroshotClientOptions
            {
                Target = origin,
                NativeBinding = Binding,
                TargetCredentials = credentials,
                // The per-request ceiling; each Broodling operation's own budget is the binding limit.
                Transport = new TransportOptions { RequestTimeout = DirectTargetLimits.Submit, TrustedRootCertificatePath = access.RootCertificate }
            });
        }
        catch (ArgumentException) { throw new NativeTransportError(); }
    }
}
