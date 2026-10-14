namespace Broodling;

/// <summary>
/// Entry point for deliberate state lifecycle. Open a separate session per caller;
/// sessions own their connections and are not shared between threads.
/// </summary>
public sealed class BroodlingApplication
{
    public BroodlingStore InitializeStore(string path) => BroodlingStore.Initialize(path);
    /// <summary>
    /// <paramref name="environments"/> is this session's operator environment catalog, whose current connection
    /// material (each target's control token file and the PEM root that HTTPS connections trust) every operation
    /// rereads; opening the store does not. A retained Attempt's origin still decides where it connects and which
    /// token it uses. Without it, retained reads work and no target is contacted.
    /// </summary>
    public BroodlingStore OpenStore(string path, EnvironmentCatalog? environments = null) =>
        BroodlingStore.Open(path, environments);
    public BroodlingStore UpgradeStore(string path) => BroodlingStore.Upgrade(path);
}
