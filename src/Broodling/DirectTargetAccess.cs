using Zeroshot.Native;
using Zeroshot.Native.Contracts;

namespace Broodling;

/// <summary>
/// Current DirectTarget connection material from operator configuration, never retained or reported: the PEM
/// root that HTTPS connections trust, and where each origin's private control token is kept. Every operation
/// resolves the exact canonical origin it connects to, which for an existing Attempt is its retained origin,
/// and reads that token file again, so a rotated token or root needs no restart. A token goes only to the
/// origin it is configured for; an origin with none is never contacted.
/// </summary>
/// <param name="ControlTokenFile">
/// The absolute path of the file holding the control token for an exact canonical origin, or null when this
/// configuration has none for it.
/// </param>
/// <param name="RootCertificate">The PEM root for HTTPS connections; null deliberately selects system trust.</param>
public sealed record DirectTargetAccess(Func<string, string?> ControlTokenFile, string? RootCertificate = null)
{
    /// <summary>One configured target: its canonical origin and token file.</summary>
    public static DirectTargetAccess For(string origin, string controlTokenFile, string? rootCertificate = null) =>
        new(candidate => candidate == origin ? controlTokenFile : null, rootCertificate);

    /// <summary>
    /// The private-capability credentials for <paramref name="origin"/>, read now. A missing, unreadable or
    /// malformed token, or an origin without one, fails as <c>credentials_unavailable</c> before any contact.
    /// </summary>
    internal TargetControlCredentials Credentials(Uri origin) => new(TargetAuthentication.PrivateCapability, Token(origin));

    /// <summary>The token itself, for the bootstrap envelope and readiness's comparison only.</summary>
    internal string Token(Uri origin)
    {
        var file = ControlTokenFile(origin.GetLeftPart(UriPartial.Authority));
        return (file is null ? null : ReadSecret(file)) ?? throw new NativeTransportError("credentials_unavailable");
    }

    /// <summary>
    /// A native private secret file: exactly 64 lowercase hexadecimal characters, the form native requires of
    /// both the control token and the bootstrap key. Null when the file is missing, unreadable or malformed.
    /// </summary>
    internal static string? ReadSecret(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.None);
            var bytes = new byte[65];
            var length = file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (length != 64 || !bytes.AsSpan(0, 64).ToArray().All(octet => octet is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f'))
                return null;
            return System.Text.Encoding.ASCII.GetString(bytes, 0, 64);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public override string ToString() => nameof(DirectTargetAccess);
}
