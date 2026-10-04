using Zeroshot;
using Zeroshot.Native;

namespace Broodling;

/// <summary>
/// Broodling's one configuration of the Zeroshot SDK for a DirectTarget: the SDK's own transport,
/// trusting the configured root, bound to the DirectTarget binding's native release. Submission,
/// the run reader and the stopper all connect through it.
/// </summary>
internal static class DirectTargetClient
{
    /// <summary>The native release the SDK must be bound to; the SDK refuses any other before sending.</summary>
    internal static readonly NativeBinding Binding =
        NativeBinding.CallerSupplied(DirectTargetBinding.NativeRelease, DirectTargetBinding.NativeSourceRevision);

    /// <summary>
    /// A client owned by one operation; disposal closes its connections and never stops a run. For an
    /// HTTPS <paramref name="origin"/>, a <paramref name="rootCertificate"/> replaces system trust on every
    /// HTTP and OECP connection, and each TLS handshake reads it again. The SDK reads it once here too:
    /// a missing or unreadable root refuses, before anything is sent, as <c>transport_failed</c>.
    /// </summary>
    internal static ZeroshotClient Open(Uri origin, string? rootCertificate)
    {
        try
        {
            return new ZeroshotClient(new ZeroshotClientOptions
            {
                Target = origin,
                NativeBinding = Binding,
                // The per-request ceiling; each Broodling operation's own budget is the binding limit.
                Transport = new TransportOptions { RequestTimeout = DirectTargetLimits.Submit, TrustedRootCertificatePath = rootCertificate }
            });
        }
        catch (ArgumentException) { throw new NativeTransportError(); }
    }
}
