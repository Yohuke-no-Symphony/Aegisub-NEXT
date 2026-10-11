using AegiNext.Core.Projects;
using AegiNext.Rendering.Images;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

internal sealed class AssetThumbnail : Image
{
    public static readonly StyledProperty<ProjectAsset?> AssetProperty = AvaloniaProperty.Register<AssetThumbnail, ProjectAsset?>(nameof(Asset));
    public static readonly StyledProperty<string?> DirectoryProperty = AvaloniaProperty.Register<AssetThumbnail, string?>(nameof(Directory));
    private Bitmap? bitmap;
    private long revision;

    public ProjectAsset? Asset { get => GetValue(AssetProperty); set => SetValue(AssetProperty, value); }
    public string? Directory { get => GetValue(DirectoryProperty); set => SetValue(DirectoryProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AssetProperty || change.Property == DirectoryProperty)
        {
            LoadThumbnail();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LoadThumbnail();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        revision++;
        Source = null;
        bitmap?.Dispose();
        bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    private async void LoadThumbnail()
    {
        var request = ++revision;
        if (!this.IsAttachedToVisualTree() || Asset is not { } asset || Directory is not { } directory)
        {
            return;
        }
        byte[]? bytes = null;
        try
        {
            bytes = await Task.Run(() =>
            {
                using var stream = new DirectoryProjectAssetResolver(directory).Open(asset);
                using var image = new SkiaImageDecoder().CreateThumbnail(stream, 112);
                using var encoded = image.Encode();
                return encoded.ToArray();
            });
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException or UnauthorizedAccessException)
        {
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (request != revision || !this.IsAttachedToVisualTree())
            {
                return;
            }
            using var stream = bytes is null ? null : new MemoryStream(bytes, false);
            var next = stream is null ? null : new Bitmap(stream);
            Source = next;
            bitmap?.Dispose();
            bitmap = next;
        });
    }
}
