namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private AssetLibraryCoordinator? assetLibrary;
    internal AssetLibraryCoordinator Assets => assetLibrary ??= new(this, dialogs);
}
