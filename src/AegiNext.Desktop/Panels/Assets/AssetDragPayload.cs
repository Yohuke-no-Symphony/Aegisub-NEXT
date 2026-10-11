using Avalonia.Input;

namespace AegiNext.Desktop.Panels.Assets;

internal sealed record AssetDragPayload(Guid ProjectId, Guid Id, bool IsFolder)
{
    private static DataFormat<string> NativeFormat { get; } = DataFormat.CreateStringApplicationFormat("AegiNext.Asset.Drag");
    internal static DataFormat<AssetDragPayload> Format { get; } = DataFormat.CreateInProcessFormat<AssetDragPayload>("AegiNext.Asset");

    internal DataTransferItem CreateTransferItem()
    {
        var item = DataTransferItem.Create(Format, this);
        item.Set(NativeFormat, Id.ToString("D"));
        return item;
    }
}
