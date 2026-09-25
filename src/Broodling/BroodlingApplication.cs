namespace Broodling;

/// <summary>
/// Entry point for deliberate state lifecycle. Open a separate session per caller;
/// sessions own their connections and are not shared between threads.
/// </summary>
public sealed class BroodlingApplication
{
    public BroodlingStore InitializeStore(string path) => BroodlingStore.Initialize(path);
    /// <summary>
    /// <paramref name="directTargetRootCertificate"/> optionally names an absolute path to the PEM root that
    /// this session's HTTPS DirectTarget connections trust instead of system trust. Each new TLS connection
    /// rereads it; opening the store does not. A retained Attempt's origin still decides where it connects.
    /// </summary>
    public BroodlingStore OpenStore(string path, string? directTargetRootCertificate = null) =>
        BroodlingStore.Open(path, directTargetRootCertificate);
    public BroodlingStore UpgradeStore(string path) => BroodlingStore.Upgrade(path);
}
