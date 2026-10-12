using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using AegiNext.Core.Media;
using AegiNext.Core.Timing;

namespace AegiNext.Media.Probing;

internal static class FfprobeJsonReader
{
    private const int MAX_NUMERIC_CHARACTER_COUNT = 256;

    internal static MediaAssetInfo Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            RequireObject(root, "root");
            var streams = ReadSection(root, "streams", JsonValueKind.Array);
            var format = ReadSection(root, "format", JsonValueKind.Object);
            if (streams is null && format is null)
            {
                throw Invalid("root", "A streams array or format object is required.");
            }

            var result = ImmutableArray.CreateBuilder<MediaStreamInfo>();
            var indices = new HashSet<int>();
            if (streams is { } streamArray)
            {
                foreach (var element in streamArray.EnumerateArray())
                {
                    var stream = ReadStream(element);
                    if (!indices.Add(stream.Index))
                    {
                        throw Invalid("index", "Duplicate stream index.");
                    }

                    result.Add(stream);
                }
            }

            return new()
            {
                FormatName = format is { } f ? ReadText(f, "format_name") : null,
                ReportedStart = format is { } s ? ReadTime(s, "start_time") : null,
                ReportedDuration = format is { } d ? ReadTime(d, "duration", nonNegative: true) : null,
                Tags = format is { } t ? ReadTags(t) : ImmutableDictionary<string, string>.Empty,
                Streams = result.ToImmutable()
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("FFprobe returned invalid JSON.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("FFprobe numbers exceed the representation range of the media contract.", exception);
        }
    }

    private static MediaStreamInfo ReadStream(JsonElement stream)
    {
        RequireObject(stream, "stream");
        var index = ReadInt32(stream, "index") ?? throw Invalid("index", "The stream index is missing.");
        if (index < 0)
        {
            throw Invalid("index", "The stream index cannot be negative.");
        }

        var codecType = ReadText(stream, "codec_type");
        var timeBaseRatio = ReadRatio(stream, "time_base", zeroIsUnknown: true, nonNegative: true);
        var startPts = ReadInt64(stream, "start_pts", timestampSentinel: true);
        var durationTicks = ReadInt64(stream, "duration_ts", timestampSentinel: true);
        if (durationTicks < 0)
        {
            throw Invalid("duration_ts", "The reported duration cannot be negative.");
        }

        return new()
        {
            Index = index,
            CodecType = codecType,
            CodecName = ReadText(stream, "codec_name"),
            Tags = ReadTags(stream),
            Disposition = ReadDisposition(stream),
            Timing = new()
            {
                TimeBase = timeBaseRatio is { } ratio ? new(ratio.Numerator, ratio.Denominator) : null,
                StartPts = startPts,
                DurationTicks = durationTicks,
                ReportedStart = ReadTime(stream, "start_time"),
                ReportedDuration = ReadTime(stream, "duration", nonNegative: true)
            },
            Video = codecType == "video" ? ReadVideo(stream) : null,
            Audio = codecType == "audio" ? ReadAudio(stream) : null
        };
    }

    private static MediaVideoInfo ReadVideo(JsonElement stream)
    {
        var mastering = ImmutableArray.CreateBuilder<MediaMasteringDisplayInfo>();
        var lights = ImmutableArray.CreateBuilder<MediaContentLightInfo>();
        var matrices = ImmutableArray.CreateBuilder<MediaDisplayMatrixInfo>();
        var types = ImmutableArray.CreateBuilder<string>();
        if (ReadSection(stream, "side_data_list", JsonValueKind.Array) is { } sideData)
        {
            foreach (var entry in sideData.EnumerateArray())
            {
                RequireObject(entry, "side_data_list entry");
                var type = ReadText(entry, "side_data_type");
                if (type is null)
                {
                    continue;
                }

                types.Add(type);
                switch (type)
                {
                    case "Mastering display metadata":
                        mastering.Add(ReadMastering(entry));
                        break;
                    case "Content light level metadata":
                        lights.Add(new()
                        {
                            MaxContentLightLevel = ReadUInt32(entry, "max_content", zeroIsUnknown: true),
                            MaxFrameAverageLightLevel = ReadUInt32(entry, "max_average", zeroIsUnknown: true)
                        });
                        break;
                    case "Display Matrix":
                        matrices.Add(new()
                        {
                            MatrixText = ReadText(entry, "displaymatrix"),
                            RotationDegrees = ReadDecimalRatio(entry, "rotation")
                        });
                        break;
                }
            }
        }

        return new()
        {
            Width = ReadPositiveInt32(stream, "width"),
            Height = ReadPositiveInt32(stream, "height"),
            PixelFormat = ReadText(stream, "pix_fmt"),
            BitsPerRawSample = ReadPositiveInt32(stream, "bits_per_raw_sample"),
            SampleAspectRatio = ReadRatio(stream, "sample_aspect_ratio", ':', zeroIsUnknown: true, nonNegative: true),
            FrameRate = ReadRatio(stream, "r_frame_rate", zeroIsUnknown: true, nonNegative: true),
            AverageFrameRate = ReadRatio(stream, "avg_frame_rate", zeroIsUnknown: true, nonNegative: true),
            Color = new()
            {
                Range = ReadText(stream, "color_range"),
                Matrix = ReadText(stream, "color_space"),
                Transfer = ReadText(stream, "color_transfer"),
                Primaries = ReadText(stream, "color_primaries"),
                ChromaLocation = ReadText(stream, "chroma_location")
            },
            MasteringDisplays = mastering.ToImmutable(),
            ContentLightLevels = lights.ToImmutable(),
            DisplayMatrices = matrices.ToImmutable(),
            SideDataTypes = types.ToImmutable()
        };
    }

    private static MediaAudioInfo ReadAudio(JsonElement stream)
    {
        return new()
        {
            SampleRate = ReadPositiveInt32(stream, "sample_rate"),
            Channels = ReadPositiveInt32(stream, "channels"),
            ChannelLayout = ReadText(stream, "channel_layout")
        };
    }

    private static MediaMasteringDisplayInfo ReadMastering(JsonElement entry)
    {
        return new()
        {
            RedX = ReadRatio(entry, "red_x", nonNegative: true),
            RedY = ReadRatio(entry, "red_y", nonNegative: true),
            GreenX = ReadRatio(entry, "green_x", nonNegative: true),
            GreenY = ReadRatio(entry, "green_y", nonNegative: true),
            BlueX = ReadRatio(entry, "blue_x", nonNegative: true),
            BlueY = ReadRatio(entry, "blue_y", nonNegative: true),
            WhitePointX = ReadRatio(entry, "white_point_x", nonNegative: true),
            WhitePointY = ReadRatio(entry, "white_point_y", nonNegative: true),
            MinLuminance = ReadRatio(entry, "min_luminance", nonNegative: true),
            MaxLuminance = ReadRatio(entry, "max_luminance", nonNegative: true)
        };
    }

    private static ImmutableDictionary<string, string> ReadTags(JsonElement parent)
    {
        var result = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        if (ReadSection(parent, "tags", JsonValueKind.Object) is { } tags)
        {
            foreach (var tag in tags.EnumerateObject())
            {
                if (tag.Value.ValueKind != JsonValueKind.String)
                {
                    throw Invalid(tag.Name, "A tag must be a string.");
                }

                result.Add(tag.Name, tag.Value.GetString()!);
            }
        }

        return result.ToImmutable();
    }

    private static ImmutableDictionary<string, int> ReadDisposition(JsonElement parent)
    {
        var result = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        if (ReadSection(parent, "disposition", JsonValueKind.Object) is { } disposition)
        {
            foreach (var flag in disposition.EnumerateObject())
            {
                var value = ReadInt32(disposition, flag.Name);
                if (value is not (0 or 1))
                {
                    throw Invalid(flag.Name, "A stream disposition must be 0 or 1.");
                }

                result.Add(flag.Name, value.Value);
            }
        }

        return result.ToImmutable();
    }

    private static string? ReadText(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid(name, "A string is required.");
        }

        var text = value.GetString();
        return text is "N/A" or "" ? null : text;
    }

    private static string? ReadNumericText(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Number => value.GetRawText(),
            _ => throw Invalid(name, "A number or numeric string is required.")
        };

        if (text.Length > MAX_NUMERIC_CHARACTER_COUNT)
        {
            throw Invalid(name, "The numeric representation exceeds the parsing limit of 256 characters.");
        }

        return text == "N/A" ? null : text;
    }

    private static long? ReadInt64(JsonElement parent, string name, bool timestampSentinel = false)
    {
        if (ReadNumericText(parent, name) is not { } text)
        {
            return null;
        }

        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            throw Invalid(name, "The integer is invalid or exceeds the Int64 range.");
        }

        return timestampSentinel && value == long.MinValue ? null : value;
    }

    private static int? ReadInt32(JsonElement parent, string name)
    {
        var value = ReadInt64(parent, name);
        return value is { } number ? checked((int)number) : null;
    }

    private static int? ReadPositiveInt32(JsonElement parent, string name)
    {
        var value = ReadInt32(parent, name);
        if (value < 0)
        {
            throw Invalid(name, "The number cannot be negative.");
        }

        return value == 0 ? null : value;
    }

    private static uint? ReadUInt32(JsonElement parent, string name, bool zeroIsUnknown)
    {
        var value = ReadInt64(parent, name);
        if (value is null || (zeroIsUnknown && value == 0))
        {
            return null;
        }

        return checked((uint)value.Value);
    }

    private static MediaRatio? ReadRatio(JsonElement parent, string name, char separator = '/',
        bool zeroIsUnknown = false, bool nonNegative = false)
    {
        if (ReadNumericText(parent, name) is not { } text)
        {
            return null;
        }

        var separatorIndex = text.IndexOf(separator);
        if (separatorIndex < 1 || separatorIndex != text.LastIndexOf(separator) ||
            !BigInteger.TryParse(text.AsSpan(0, separatorIndex), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var numerator) ||
            !BigInteger.TryParse(text.AsSpan(separatorIndex + 1), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var denominator))
        {
            throw Invalid(name, "The rational number format is invalid.");
        }

        if (numerator.IsZero && denominator.IsZero)
        {
            return null;
        }

        if (denominator.Sign <= 0 || (nonNegative && numerator.Sign < 0))
        {
            throw Invalid(name, "The rational number sign or denominator is invalid.");
        }

        if (zeroIsUnknown && numerator.IsZero)
        {
            return null;
        }

        return Reduce(numerator, denominator);
    }

    private static MediaTime? ReadTime(JsonElement parent, string name, bool nonNegative = false)
    {
        if (ReadDecimalRatio(parent, name) is not { } ratio)
        {
            return null;
        }

        if (nonNegative && ratio.Numerator < 0)
        {
            throw Invalid(name, "The reported duration cannot be negative.");
        }

        return new(ratio.Numerator, ratio.Denominator);
    }

    private static MediaRatio? ReadDecimalRatio(JsonElement parent, string name)
    {
        if (ReadNumericText(parent, name) is not { } text)
        {
            return null;
        }

        var exponentIndex = text.IndexOfAny(['e', 'E']);
        var exponent = 0;
        var mantissa = text;
        if (exponentIndex >= 0)
        {
            mantissa = text[..exponentIndex];
            if (!int.TryParse(text.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out exponent))
            {
                throw Invalid(name, "The decimal exponent is invalid or overflows.");
            }
        }

        var negative = mantissa.StartsWith('-');
        if (negative || mantissa.StartsWith('+'))
        {
            mantissa = mantissa[1..];
        }

        var decimalIndex = mantissa.IndexOf('.');
        var fractionDigits = decimalIndex < 0 ? 0 : mantissa.Length - decimalIndex - 1;
        if (mantissa.Length == 0 || decimalIndex == 0 || decimalIndex == mantissa.Length - 1 ||
            (decimalIndex >= 0 && decimalIndex != mantissa.LastIndexOf('.')))
        {
            throw Invalid(name, "The decimal number format is invalid.");
        }

        var digits = decimalIndex < 0 ? mantissa : mantissa.Remove(decimalIndex, 1);
        if (digits.Any(static character => !char.IsAsciiDigit(character)))
        {
            throw Invalid(name, "The decimal number contains invalid characters.");
        }

        var numerator = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        if (numerator.IsZero)
        {
            return new(0, 1);
        }

        var scale = (long)fractionDigits - exponent;
        while (numerator % 10 == 0)
        {
            numerator /= 10;
            scale--;
        }

        if (scale < -18 || scale > 63)
        {
            throw Invalid(name, "The decimal number cannot be represented exactly as an Int64 rational number.");
        }

        if (negative)
        {
            numerator = -numerator;
        }

        return scale >= 0
            ? Reduce(numerator, BigInteger.Pow(10, (int)scale))
            : Reduce(numerator * BigInteger.Pow(10, (int)-scale), BigInteger.One);
    }

    private static MediaRatio Reduce(BigInteger numerator, BigInteger denominator)
    {
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(checked((long)(numerator / divisor)), checked((long)(denominator / divisor)));
    }

    private static JsonElement? ReadSection(JsonElement parent, string name, JsonValueKind kind)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != kind)
        {
            throw Invalid(name, $"A value of kind {kind} is required.");
        }

        if (kind == JsonValueKind.Object)
        {
            RequireObject(value, name);
        }

        return value;
    }

    private static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(name, "An object is required.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw Invalid(property.Name, "Duplicate field within the same object.");
            }
        }
    }

    private static InvalidDataException Invalid(string field, string message)
    {
        return new($"FFprobe field {field}: {message}");
    }
}
