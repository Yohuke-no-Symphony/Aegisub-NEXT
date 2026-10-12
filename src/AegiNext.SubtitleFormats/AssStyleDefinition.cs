using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed record AssStyleDefinition(string Name, SubtitleStyle Style, SceneColor Secondary)
{
    internal ScenePoint Scale { get; init; } = new(1, 1);
    internal double Rotation { get; init; }
}
