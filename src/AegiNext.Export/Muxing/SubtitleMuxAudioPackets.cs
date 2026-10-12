using System.Globalization;
using AegiNext.Core.Timing;

namespace AegiNext.Export.Muxing;

internal sealed record SubtitleMuxAudioPackets(long PacketCount, MediaTime? End, long InitialPadding,
    long SkipSamples, long DiscardPadding)
{
    internal static async Task<SubtitleMuxAudioPackets> ReadAsync(string ffprobe, string path, int streamIndex,
        MediaTimeBase? timeBase, CancellationToken cancellationToken)
    {
        var packetCount = 0L;
        var initialPadding = 0L;
        var skipSamples = 0L;
        var discardPadding = 0L;
        MediaTime? end = null;
        await SubtitleMuxProcess.RunProbeAsync(ffprobe,
            ["-v", "error", "-select_streams", streamIndex.ToString(CultureInfo.InvariantCulture), "-show_packets", "-show_streams",
                "-show_entries", "packet=pts,duration:packet_side_data=skip_samples,discard_padding:stream=initial_padding:stream_disposition=:stream_tags=",
                "-of", "compact", path], line =>
            {
                var fields = line.Split('|');
                if (fields[0] == "stream")
                {
                    initialPadding = ReadValue(fields, "initial_padding") ?? 0;
                }
                else if (fields[0] == "packet")
                {
                    packetCount = checked(packetCount + 1);
                    skipSamples = checked(skipSamples + (ReadValue(fields, "skip_samples") ?? 0));
                    discardPadding = checked(discardPadding + (ReadValue(fields, "discard_padding") ?? 0));
                    if (timeBase is { } known && ReadValue(fields, "pts") is { } pts)
                    {
                        var packetEnd = new MediaTimestamp(checked(pts + (ReadValue(fields, "duration") ?? 0)), known).ToMediaTime();
                        if (end is null || packetEnd > end.Value)
                        {
                            end = packetEnd;
                        }
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        return new(packetCount, end, initialPadding, skipSamples, discardPadding);
    }

    private static long? ReadValue(string[] fields, string name)
    {
        foreach (var field in fields)
        {
            var separator = field.IndexOf('=');
            if (separator > 0 && (field.AsSpan(0, separator).SequenceEqual(name) ||
                field.AsSpan(0, separator).EndsWith(":" + name, StringComparison.Ordinal)))
            {
                if (long.TryParse(field.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }
            }
        }

        return null;
    }
}
