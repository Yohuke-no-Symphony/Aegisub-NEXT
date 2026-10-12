using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

internal static class ReferenceSubtitleProject
{
    internal static string Script(string text, bool scaledBorder = true, int playWidth = 640, int playHeight = 360,
        string outline = "0", string shadow = "0")
    {
        return $"""
            [Script Info]
            ScriptType: v4.00+
            PlayResX: {playWidth}
            PlayResY: {playHeight}
            ScaledBorderAndShadow: {(scaledBorder ? "yes" : "no")}
            YCbCr Matrix: None
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Noto Sans,48,&H0000FF00,&H000000FF,&H00FF0000,&H00000000,0,0,0,0,100,100,0,0,1,{outline},{shadow},7,32,32,32,1
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:00.00,0:00:04.00,Default,,0,0,0,,{text}
            """;
    }

    internal static ProjectDocument Import(string source)
    {
        var imported = AssSubtitleFormat.Parse(source, 640, 360);
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var lines = imported.Clips.Select(clip => clip.Line with
            { Style = clip.Line.Style with { FontAssetId = font.Id } }).ToImmutableArray();
        return new()
        {
            Width = 640,
            Height = 360,
            Assets = [font],
            Subtitles = lines,
            Layers = [.. imported.Clips.Select((clip, index) => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = lines[index].Id, Start = lines[index].Start, End = lines[index].End,
                AnimationOffset = clip.ContentOffset, Transform = clip.Transform, Mask = clip.Mask, Tracks = clip.Tracks
            })]
        };
    }

    internal static SubtitleReferenceFrame Render(ProjectSceneRenderer renderer, ProjectDocument document, long milliseconds)
    {
        using var frame = renderer.Render(document, new MediaTime(milliseconds, 1000));
        var pixels = new float[frame.Info.ChannelCount];
        frame.CopyPixels(pixels);
        return new(document.Width, document.Height, pixels);
    }

    internal static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
}
