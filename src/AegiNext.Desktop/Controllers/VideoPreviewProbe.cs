using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;
using AegiNext.Media.Probing;

namespace AegiNext.Desktop.Controllers;

internal static class VideoPreviewProbe
{
    private static readonly string[] unsupportedSideData =
        ["dynamic", "dovi", "dolby", "stereo", "icc", "raw color", "ambient", "display matrix"];

    internal static async Task<VideoPreviewMedia> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        var probe = new FfprobeMediaProbe(new(MediaToolchain.ResolveFfprobe()));
        var report = await probe.ProbeAsync(filePath, cancellationToken).ConfigureAwait(false);
        return Read(report.Asset);
    }

    internal static VideoPreviewMedia Read(MediaAssetInfo asset)
    {
        var selected = asset.Streams
            .Where(stream => stream.CodecType == "video" && stream.Video is not null && stream.Disposition.GetValueOrDefault("attached_pic") == 0)
            .OrderByDescending(stream => stream.Disposition.GetValueOrDefault("default"))
            .ThenBy(stream => stream.Index)
            .FirstOrDefault() ?? throw new InvalidDataException(Localization.Get("Preview.NoVideo"));
        if (!selected.Video!.DisplayMatrices.IsDefaultOrEmpty)
        {
            throw new NotSupportedException(Localization.Get("Preview.DisplayMatrix"));
        }

        foreach (var name in selected.Video.SideDataTypes)
        {
            if (unsupportedSideData.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NotSupportedException($"{Localization.Get("Preview.UnsupportedMetadata")} {name}");
            }
        }

        var timing = selected.Timing;
        var start = ReadStart(timing) ?? asset.ReportedStart;
        var duration = ReadDuration(timing) ?? asset.ReportedDuration;
        var audio = asset.Streams.Where(stream => stream.CodecType == "audio")
            .OrderByDescending(stream => stream.Disposition.GetValueOrDefault("default"))
            .ThenBy(stream => stream.Index).FirstOrDefault();
        var playbackOrigin = asset.ReportedStart ?? asset.Streams
            .Where(stream => (stream.CodecType is "video" or "audio") && stream.Disposition.GetValueOrDefault("attached_pic") == 0)
            .Select(stream => ReadStart(stream.Timing))
            .Where(timestamp => timestamp.HasValue)
            .Min();
        return new(selected.Index, start, duration is { } value && value > MediaTime.Zero ? value : null, audio?.Index,
            selected.Video.Width, selected.Video.Height, selected.Video.AverageFrameRate ?? selected.Video.FrameRate)
        {
            PlaybackOrigin = playbackOrigin,
            AudioCodecName = audio?.CodecName
        };
    }

    private static MediaTime? ReadStart(MediaStreamTiming timing)
    {
        return timing.StartTimestamp?.ToMediaTime() ?? timing.ReportedStart;
    }

    private static MediaTime? ReadDuration(MediaStreamTiming timing)
    {
        if (timing.DurationTicks is > 0 && timing.TimeBase is { } timeBase)
        {
            return new MediaTimestamp(timing.DurationTicks.Value, timeBase).ToMediaTime();
        }

        return timing.ReportedDuration;
    }

}
