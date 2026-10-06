namespace Broodling;

/// <summary>
/// Entry point for deliberate state lifecycle. Open a separate session per caller;
/// sessions own their connections and are not shared between threads.
/// </summary>
public sealed class BroodlingApplication
{
    public BroodlingStore InitializeStore(string path) => BroodlingStore.Initialize(path);
    /// <summary>
    /// <paramref name="directTarget"/> is this session's current DirectTarget connection material: the control
    /// token file for each origin and the PEM root that HTTPS connections trust. Each operation rereads both;
    /// opening the store does not. A retained Attempt's origin still decides where it connects and which token
    /// it uses. Without it, retained reads work and no target is contacted.
    /// </summary>
    public BroodlingStore OpenStore(string path, DirectTargetAccess? directTarget = null) =>
        BroodlingStore.Open(path, directTarget);
    public BroodlingStore UpgradeStore(string path) => BroodlingStore.Upgrade(path);
}
