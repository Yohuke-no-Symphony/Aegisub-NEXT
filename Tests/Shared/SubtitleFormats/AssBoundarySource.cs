namespace AegiNext.Testing.SubtitleFormats;

internal static class AssBoundarySource
{
    internal static string File(string text, string? scaledBorder = "yes", int? layoutWidth = 640, int? layoutHeight = 360)
    {
        var scaling = scaledBorder is null ? string.Empty : "ScaledBorderAndShadow: " + scaledBorder + "\n";
        var layout = (layoutWidth is null ? string.Empty : "LayoutResX: " + layoutWidth + "\n") +
            (layoutHeight is null ? string.Empty : "LayoutResY: " + layoutHeight + "\n");
        return "[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\nWrapStyle: 1\n" + scaling + layout +
            "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
            "Style: Default,Noto Sans,20,&H80FFFFFF,&H400000FF,&H2000FF00,&H60FF0000,0,0,0,0,100,100,0,0,1,2,2,2,10,10,10,1\n" +
            "Style: Alternate,Noto Sans,30,&H800000FF,&H4000FF00,&H20FF0000,&H60FFFFFF,0,0,0,0,100,100,0,0,1,4,3,7,20,20,20,1\n" +
            "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            "Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,," + text;
    }
}
