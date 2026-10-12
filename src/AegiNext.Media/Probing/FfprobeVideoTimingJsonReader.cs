using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using AegiNext.Core.Media;
using AegiNext.Core.Timing;

namespace AegiNext.Media.Probing;

internal static class FfprobeVideoTimingJsonReader
{
    internal static VideoTimingIndex Read(string json, int streamIndex, MediaTime origin,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            RequireObject(root, "root");
            var streams = RequireArray(root, "streams");
            if (streams.GetArrayLength() != 1)
            {
                throw Invalid("streams", "Exactly one target video stream is required.");
            }

            var stream = streams[0];
            RequireObject(stream, "stream");
            if (ReadInt64(stream, "index") != streamIndex ||
                !stream.TryGetProperty("codec_type", out var codecType) || codecType.GetString() != "video")
            {
                throw Invalid("stream", "The target stream index or type does not match the request.");
            }

            var timeBase = ReadTimeBase(stream);
            var frames = RequireArray(root, "frames");
            var count = frames.GetArrayLength();
            if (count == 0 || count > VideoTimingIndex.MaximumFrameCount)
            {
                throw Invalid("frames", $"The video frame count must be between 1 and {VideoTimingIndex.MaximumFrameCount}.");
            }

            var frameTimes = ImmutableArray.CreateBuilder<MediaTime>(count);
            var keyframes = ImmutableArray.CreateBuilder<int>();
            foreach (var frame in frames.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireObject(frame, "frame");
                if (ReadInt64(frame, "stream_index") != streamIndex)
                {
                    throw Invalid("stream_index", "The frame does not belong to the target video stream.");
                }

                var pts = ReadInt64(frame, "pts");
                if (frame.TryGetProperty("best_effort_timestamp", out var bestEffort) &&
                    bestEffort.ValueKind != JsonValueKind.Null && ReadInt64(frame, "best_effort_timestamp") != pts)
                {
                    throw Invalid("best_effort_timestamp", "The display time does not match the actual frame PTS; an exact index cannot be built.");
                }

                var time = new MediaTimestamp(pts, timeBase).ToMediaTime() - origin;
                if (frameTimes.Count > 0 && time <= frameTimes[^1])
                {
                    throw Invalid("pts", $"The timestamp of display-order frame {frameTimes.Count} is not strictly increasing; frames cannot be inserted or inferred from an average frame rate.");
                }

                var keyframe = ReadInt64(frame, "key_frame");
                if (keyframe is not (0 or 1))
                {
                    throw Invalid("key_frame", "The keyframe flag must be 0 or 1.");
                }

                if (keyframe == 1)
                {
                    keyframes.Add(frameTimes.Count);
                }

                frameTimes.Add(time);
            }

            if (keyframes.Count == 0 || keyframes[^1] != count - 1)
            {
                keyframes.Add(count - 1);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new(frameTimes.MoveToImmutable(), keyframes.ToImmutable());
        }
        catch (Exception exception) when (exception is JsonException or OverflowException or InvalidOperationException)
        {
            throw new InvalidDataException("The FFprobe video frame timing report is invalid or exceeds the exact time representation range.", exception);
        }
    }

    private static MediaTimeBase ReadTimeBase(JsonElement stream)
    {
        if (!stream.TryGetProperty("time_base", out var element) || element.ValueKind != JsonValueKind.String)
        {
            throw Invalid("time_base", "The video stream time base is missing.");
        }

        var text = element.GetString()!;
        var separator = text.IndexOf('/');
        if (separator <= 0 || separator != text.LastIndexOf('/') ||
            !long.TryParse(text.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var numerator) ||
            !long.TryParse(text.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var denominator) ||
            numerator <= 0 || denominator <= 0)
        {
            throw Invalid("time_base", "The video stream time base must be an exactly representable positive rational number.");
        }

        return new(numerator, denominator);
    }

    private static long ReadInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            throw Invalid(name, "An actual frame field is missing; estimated timing cannot replace it.");
        }

        var text = element.ValueKind switch
        {
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => element.GetString(),
            _ => null
        };
        if (text is null || text.Length > 20 ||
            !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ||
            value == long.MinValue)
        {
            throw Invalid(name, "A valid Int64 integer is required, and it cannot be the undefined PTS sentinel.");
        }

        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(name, "An array is missing.");
        }

        return element;
    }

    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(name, "An object is required.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw Invalid(property.Name, "Duplicate field within the same object.");
            }
        }
    }

    private static InvalidDataException Invalid(string field, string message)
    {
        return new($"FFprobe video frame field {field}: {message}");
    }
}
