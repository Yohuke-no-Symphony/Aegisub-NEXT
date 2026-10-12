using System.Globalization;
using AegiNext.Export;
using AegiNext.Media.Decoding;

namespace AegiNext.Cli;

internal static class ExportOptions
{
    internal static ExportConfiguration Resolve(ExportSettings options)
    {
        var codec = (options.Encoding ?? "auto").ToLowerInvariant() switch
        {
            "auto" => VideoCodec.Auto,
            "h264" => VideoCodec.H264,
            "hevc" => VideoCodec.Hevc,
            "vp8" => VideoCodec.Vp8,
            "vp9" => VideoCodec.Vp9,
            "av1" => VideoCodec.Av1,
            _ => throw new CliUsageException("--encoding must be auto/h264/hevc/vp8/vp9/av1.")
        };
        var capability = VideoExportCapabilities.Get(codec);
        if (options.UseGpu && !capability.SupportsHardwareOnCurrentPlatform)
        {
            throw new CliUsageException("This codec has no supported GPU backend on the current platform.");
        }

        var modes = new[] { options.Crf, options.Cq, options.Vbr, options.Cbr };
        if (modes.Count(value => value) > 1)
        {
            throw new CliUsageException("--crf, --cq, --vbr, and --cbr are mutually exclusive; select only one.");
        }
        var mode = options.Crf ? VideoRateControlMode.CRF : options.Cq ? VideoRateControlMode.CQ :
            options.Vbr ? VideoRateControlMode.VBR : options.Cbr ? VideoRateControlMode.CBR :
            options.UseGpu ? VideoRateControlMode.VBR : capability.QualityMode;
        if (options.Quality.HasValue && mode is not (VideoRateControlMode.CRF or VideoRateControlMode.CQ))
        {
            throw new CliUsageException("--quality is only valid for CRF/CQ.");
        }
        if (options.VideoBitrate is not null && mode == VideoRateControlMode.CRF)
        {
            throw new CliUsageException("--video-bitrate is not valid for CRF.");
        }
        var audio = (options.Audio ?? "copy").ToLowerInvariant() switch
        {
            "copy" => AudioExportMode.Copy,
            "aac" => AudioExportMode.Aac,
            "opus" => AudioExportMode.Opus,
            "none" => AudioExportMode.None,
            _ => throw new CliUsageException("--audio must be copy/aac/opus/none.")
        };
        if (options.AudioBitrate.HasValue && audio is not (AudioExportMode.Aac or AudioExportMode.Opus))
        {
            throw new CliUsageException("--audio-bitrate is only valid for AAC/Opus.");
        }
        if (options.AudioBitrate is < 32 or > 512)
        {
            throw new CliUsageException("--audio-bitrate must be between 32 and 512 kbps.");
        }
        var defaults = VideoExportCapabilities.CreateDefaultSettings(codec);
        var settings = defaults with
        {
            EncodingMode = options.UseGpu ? VideoEncodingMode.HARDWARE : VideoEncodingMode.SOFTWARE,
            RateControlMode = mode,
            Quality = options.Quality ?? defaults.Quality,
            VideoBitrate = options.VideoBitrate is { } bitrate ? ParseVideoBitrate(bitrate) : defaults.VideoBitrate,
            Preset = options.Speed?.ToLowerInvariant() ?? defaults.Preset,
            AudioMode = audio,
            AudioBitrate = options.AudioBitrate is { } audioBitrate ? checked(audioBitrate * 1000) : defaults.AudioBitrate
        };
        try
        {
            VideoExportSettingsValidator.Validate(settings);
        }
        catch (ArgumentException error)
        {
            throw new CliUsageException(error.Message);
        }
        var decode = (options.Decode ?? "auto").ToLowerInvariant() switch
        {
            "auto" => VideoDecodeMode.Auto,
            "software" => VideoDecodeMode.Software,
            "hardware" => VideoDecodeMode.Hardware,
            _ => throw new CliUsageException("--decode must be auto/software/hardware.")
        };
        var extension = ResolveExtension(options, codec);
        if (!capability.Extensions.Contains(extension))
        {
            throw new CliUsageException("The output container is incompatible with the selected video codec.");
        }
        if (extension == ".webm" && audio == AudioExportMode.Aac)
        {
            throw new CliUsageException("WebM does not support AAC. Select opus, copy, or none.");
        }
        return new(settings, decode, extension);
    }

    private static int ParseVideoBitrate(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var mbps) ||
            mbps is < 0.1m or > 200m || (decimal.GetBits(mbps)[3] >> 16 & 0xff) > 3)
        {
            throw new CliUsageException("--video-bitrate must be between 0.1 and 200 Mbps, using a decimal point and at most three decimal places.");
        }
        return decimal.ToInt32(mbps * 1_000_000);
    }

    private static string ResolveExtension(ExportSettings options, VideoCodec codec)
    {
        var specified = options.Container?.ToLowerInvariant();
        if (specified is not null and not ("mp4" or "mkv" or "webm"))
        {
            throw new CliUsageException("--container must be mp4/mkv/webm.");
        }
        string? fileExtension = null;
        if (options.Output is { } output && !CliPaths.IsDirectoryOutput(output))
        {
            fileExtension = Path.GetExtension(output).ToLowerInvariant();
        }
        if (specified is not null && fileExtension is not null && fileExtension != "." + specified)
        {
            throw new CliUsageException("--container conflicts with the --output file extension.");
        }
        return fileExtension ?? (specified is not null ? "." + specified :
            codec is VideoCodec.Auto or VideoCodec.H264 or VideoCodec.Hevc ? ".mp4" : ".mkv");
    }
}
