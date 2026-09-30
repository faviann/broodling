using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Zeroshot;
using Zeroshot.Native;

namespace Broodling;

/// <summary>
/// Broodling's one configuration of the Zeroshot SDK for a DirectTarget: the SDK's supported HTTP
/// handler with Broodling's TLS trust and no proxy, and the DirectTarget binding's native release.
/// Submission goes through it. The run reader, stopper and readiness discovery still use
/// <see cref="DirectTargetExchange"/>, which shares only the trust below, until they move here.
/// </summary>
internal static class DirectTargetClient
{
    /// <summary>The native release the SDK must be bound to; the SDK refuses any other before sending.</summary>
    internal static readonly NativeBinding Binding =
        NativeBinding.CallerSupplied(DirectTargetBinding.NativeRelease, DirectTargetBinding.NativeSourceRevision);

    /// <summary>
    /// A client owned by one operation, so every connection it makes is new and rereads the configured
    /// root. Disposal closes its connections and never stops a run.
    /// </summary>
    internal static ZeroshotClient Open(Uri origin, string? rootCertificate)
    {
        // The per-request ceiling; each Broodling operation's own budget is the binding limit.
        var transport = new TransportOptions { RequestTimeout = DirectTargetLimits.Submit };
        var handler = NativeClient.CreateHttpHandler(transport);
        handler.UseProxy = false; // An ambient proxy would receive dispatch credentials.
        Trust(handler.SslOptions, origin, rootCertificate);
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        NativeClient? native = null;
        try
        {
            native = NativeClient.ForHttp(new NativeClientOptions { Origin = origin, Transport = transport }, http, ownsHttpClient: true);
            return new ZeroshotClient(native, Binding, ownsClient: true);
        }
        catch
        {
            // Ownership transfers only on success.
            if (native is null) http.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Refuses, before anything is recorded or sent, when the configured root of an HTTPS origin cannot be read.
    /// Each later TLS handshake reads it again.
    /// </summary>
    internal static void RequireReadableRoot(Uri origin, string? rootCertificate)
    {
        if (rootCertificate is not null && origin.Scheme == Uri.UriSchemeHttps) ReadRoot(rootCertificate).Dispose();
    }

    /// <summary>
    /// TLS is always verified: by system trust, or, for an HTTPS <paramref name="origin"/> with a
    /// <paramref name="rootCertificate"/>, by exactly that PEM root, reread for each new TLS connection.
    /// </summary>
    internal static void Trust(SslClientAuthenticationOptions ssl, Uri origin, string? rootCertificate)
    {
        if (rootCertificate is not null && origin.Scheme == Uri.UriSchemeHttps)
            ssl.RemoteCertificateValidationCallback = (_, certificate, presented, errors) =>
                ChainsToRoot(rootCertificate, certificate, presented, errors);
    }

    /// <summary>
    /// One TLS handshake's validation against the configured root, read now: any failure other than the
    /// system-trust chain (a host name mismatch, no certificate) refuses, and only a chain from the
    /// presented certificate and intermediates to exactly that root, for server authentication, accepts.
    /// The system store plays no part. Revocation is unchecked, as for ordinary TLS. An unreadable root refuses.
    /// </summary>
    private static bool ChainsToRoot(string rootCertificate, X509Certificate? certificate, X509Chain? presented, SslPolicyErrors errors)
    {
        if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None || certificate is not X509Certificate2 leaf)
            return false;
        X509Certificate2 root;
        try { root = ReadRoot(rootCertificate); }
        catch (NativeTransportError) { return false; }
        using (root)
        using (var chain = new X509Chain())
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            chain.ChainPolicy.CustomTrustStore.Add(root);
            if (presented is not null) chain.ChainPolicy.ExtraStore.AddRange(presented.ChainPolicy.ExtraStore);
            return chain.Build(leaf);
        }
    }

    /// <summary>The configured PEM root; missing, unreadable or not a certificate is a transport failure.</summary>
    private static X509Certificate2 ReadRoot(string rootCertificate)
    {
        try { return X509Certificate2.CreateFromPem(File.ReadAllText(rootCertificate)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        { throw new NativeTransportError(); }
    }
}
