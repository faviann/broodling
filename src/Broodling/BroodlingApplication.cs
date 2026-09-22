namespace Broodling;

/// <summary>
/// Entry point for deliberate state lifecycle. Open a separate session per caller;
/// sessions own their connections and are not shared between threads.
/// </summary>
public sealed class BroodlingApplication
{
    public BroodlingStore InitializeStore(string path) => BroodlingStore.Initialize(path);
    public BroodlingStore OpenStore(string path) => BroodlingStore.Open(path);
    public BroodlingStore UpgradeStore(string path) => BroodlingStore.Upgrade(path);
}
