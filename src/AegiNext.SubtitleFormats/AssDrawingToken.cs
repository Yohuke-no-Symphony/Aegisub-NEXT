using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal readonly record struct AssDrawingToken(AssDrawingCommand Command, int X, int Y)
{
    internal ScenePoint Position => new(X / 64d, Y / 64d);
}
