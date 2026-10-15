using AegiNext.Core.Media;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.I18n;
using AegiNext.Export;
using AegiNext.Media.Probing;

namespace AegiNext.Desktop.Workspace;

internal sealed class ExportAudioCompatibility(WorkbenchSession session,
    Func<string, CancellationToken, Task<MediaAssetInfo>>? mediaProbe = null)
{
    private readonly Func<string, CancellationToken, Task<MediaAssetInfo>> probe = mediaProbe ?? ProbeAsync;

    internal void RefreshSource()
    {
        session.ViewModel.Export.UpdateSourceAudioCodec(GetCachedMedia()?.AudioCodecName);
    }

    internal async Task PrepareAsync()
    {
        RefreshSource();
        var model = session.ViewModel.Export;
        if (!model.IsBurnIn || !model.IsWebM || model.AudioMode != (int)AudioExportMode.Copy)
        {
            return;
        }

        var cached = GetCachedMedia();
        if (cached is not null && (cached.AudioStreamIndex is null || !string.IsNullOrWhiteSpace(cached.AudioCodecName)))
        {
            return;
        }

        var document = session.Editor.Snapshot;
        if (document.Media is not { } binding)
        {
            return;
        }

        var projectDirectory = session.ProjectDirectory;
        var inputRevision = session.TaskInputRevision;
        var codec = model.SelectedCodec;
        var sourcePath = ProjectAssetLocation.Resolve(document.Assets.Single(asset => asset.Id == binding.AssetId), projectDirectory);
        var token = session.ProjectOperationsToken;
        MediaAssetInfo source;
        try
        {
            source = await probe(sourcePath, token);
        }
        catch (Exception) when (!IsCurrent(document, projectDirectory, inputRevision, codec))
        {
            throw new OperationCanceledException("The export source or settings changed during audio probing.", token);
        }
        token.ThrowIfCancellationRequested();
        if (!IsCurrent(document, projectDirectory, inputRevision, codec))
        {
            throw new OperationCanceledException("The export source or settings changed during audio probing.");
        }

        model.UpdateSourceAudioCodec(ReadAudioCodec(source, binding.AudioStreamIndex));
    }

    private bool IsCurrent(ProjectDocument document, string projectDirectory, long inputRevision, VideoCodec codec)
    {
        var model = session.ViewModel.Export;
        return !session.IsClosing && ReferenceEquals(document, session.Editor.Snapshot) &&
            projectDirectory == session.ProjectDirectory && inputRevision == session.TaskInputRevision &&
            model.IsBurnIn && model.IsWebM && model.SelectedCodec == codec && model.AudioMode == (int)AudioExportMode.Copy;
    }

    internal static string? ReadAudioCodec(MediaAssetInfo source, int? audioStreamIndex)
    {
        var selected = audioStreamIndex is { } index
            ? source.Streams.Single(stream => stream.Index == index)
            : source.Streams.FirstOrDefault(stream => stream.CodecType == "audio");
        if (selected is null)
        {
            return null;
        }
        if (selected.CodecType != "audio")
        {
            throw new InvalidDataException("The selected audio track is not an audio stream.");
        }
        if (string.IsNullOrWhiteSpace(selected.CodecName))
        {
            throw new InvalidDataException(Localization.Get("Workbench.ExportAudioCodecUnavailable"));
        }

        return selected.CodecName;
    }

    private VideoPreviewMedia? GetCachedMedia()
    {
        var document = session.Editor.Snapshot;
        if (document.Media is not { } binding || session.Controller.MediaInfo is not { } media ||
            binding.AudioStreamIndex != media.AudioStreamIndex || session.Controller.Snapshot.FilePath is not { } previewPath)
        {
            return null;
        }

        var sourcePath = ProjectAssetLocation.Resolve(document.Assets.Single(asset => asset.Id == binding.AssetId), session.ProjectDirectory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(previewPath), sourcePath, comparison) ? media : null;
    }

    private static async Task<MediaAssetInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var probe = new FfprobeMediaProbe(new(MediaToolchain.ResolveFfprobe()));
        var report = await probe.ProbeAsync(path, cancellationToken).ConfigureAwait(false);
        return report.Asset;
    }
}
