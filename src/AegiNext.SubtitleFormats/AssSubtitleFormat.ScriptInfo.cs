using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

public static partial class AssSubtitleFormat
{
    private static AssScriptInfoContext ReadScriptInfo(Dictionary<string, string> info, int? requestedWidth,
        int? requestedHeight, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        var hasWidth = info.TryGetValue("PlayResX", out var x);
        var hasHeight = info.TryGetValue("PlayResY", out var y);
        var width = hasWidth ? AssFormatValues.Integer(x!) : requestedWidth ?? 1920;
        var height = hasHeight ? AssFormatValues.Integer(y!) : requestedHeight ?? 1080;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("ASS PlayRes values must be positive.");
        }
        var targetWidth = requestedWidth ?? width;
        var targetHeight = requestedHeight ?? height;
        var colorMatrix = Get(info, "YCbCr Matrix", string.Empty).Trim().ToUpperInvariant();
        if (colorMatrix is "TV.601" or "PC.601" or "TV.709" or "PC.709" or "TV.240M" or "PC.240M" or "TV.FCC" or "PC.FCC")
        {
            diagnostics.Add(new("Ass.YCbCrMatrix", $"ASS YCbCr Matrix ({colorMatrix}) color-matching information was not imported. The project preserves RGB values; colors may differ if the source player matches them to the video color space."));
        }
        var wrapStyle = AssFormatValues.Integer(Get(info, "WrapStyle", "0"));
        if (wrapStyle is < 0 or > 3)
        {
            diagnostics.Add(new("Ass.WrapStyle", "ASS WrapStyle must be between 0 and 3. Default smart wrapping mode 0 was used."));
            wrapStyle = 0;
        }
        var layoutWidth = AssFormatValues.Integer(Get(info, "LayoutResX", "0"));
        var layoutHeight = AssFormatValues.Integer(Get(info, "LayoutResY", "0"));
        var blurUsesPlayRes = layoutWidth <= 0 || layoutHeight <= 0;
        var scaledBorder = !Get(info, "ScaledBorderAndShadow", "yes").Equals("no", StringComparison.OrdinalIgnoreCase);
        if (!scaledBorder && blurUsesPlayRes)
        {
            diagnostics.Add(new("Ass.BorderLayoutResolution", "ASS does not provide complete, valid LayoutRes values. Unscaled stroke and shadow are interpreted in target canvas pixels; their appearance may differ if the original video dimensions differ."));
        }
        return new()
        {
            Width = width,
            Height = height,
            TargetWidth = targetWidth,
            TargetHeight = targetHeight,
            HasWidth = hasWidth,
            HasHeight = hasHeight,
            WrapStyle = wrapStyle,
            BlurUsesPlayRes = blurUsesPlayRes,
            BlurScaleX = (double)targetWidth / (blurUsesPlayRes ? width : layoutWidth),
            BlurScaleY = (double)targetHeight / (blurUsesPlayRes ? height : layoutHeight),
            PerspectiveDistance = SceneProjection.DEFAULT_DISTANCE * (blurUsesPlayRes ? 1 : (double)targetHeight / layoutHeight),
            Resolution = scaledBorder ? new((double)targetWidth / width, (double)targetHeight / height) :
                blurUsesPlayRes ? new(1, 1) : new((double)targetWidth / layoutWidth, (double)targetHeight / layoutHeight)
        };
    }

    private static void AddMissingResolutionDiagnostic(AssScriptInfoContext script, bool atSourceResolution,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        if (script.HasWidth && script.HasHeight)
        {
            return;
        }
        if (!atSourceResolution)
        {
            diagnostics.Add(new("Ass.PlayRes", "Missing ASS PlayRes axes use the target canvas dimensions; declared axes are still resampled from their source dimensions."));
            return;
        }
        var missingAxes = new List<string>();
        if (!script.HasWidth)
        {
            missingAxes.Add("PlayResX=1920");
        }
        if (!script.HasHeight)
        {
            missingAxes.Add("PlayResY=1080");
        }
        diagnostics.Add(new("Ass.PlayRes", $"Missing ASS PlayRes axes use default dimensions ({string.Join(", ", missingAxes)}); declared axes retain their source dimensions."));
    }
}
