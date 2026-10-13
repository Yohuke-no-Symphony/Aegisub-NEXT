using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed class AssBorderSourceState(double initial)
{
    private ScenePoint widths = new(initial, initial);
    private int horizontalAnimation;
    private int verticalAnimation;
    private int nextAnimation;

    internal bool Dirty { get; private set; }
    internal bool IsShared => widths.X.Equals(widths.Y) && horizontalAnimation == verticalAnimation;
    internal bool IsStatic => horizontalAnimation == 0 && verticalAnimation == 0;
    internal double Width => widths.X;

    internal void Set(string name, double width)
    {
        if (name is "bord" or "xbord")
        {
            widths = widths with { X = width };
            horizontalAnimation = 0;
        }
        if (name is "bord" or "ybord")
        {
            widths = widths with { Y = width };
            verticalAnimation = 0;
        }
        Dirty = true;
    }

    internal void Animate(string name)
    {
        var animation = ++nextAnimation;
        if (name is "bord" or "xbord")
        {
            horizontalAnimation = animation;
        }
        if (name is "bord" or "ybord")
        {
            verticalAnimation = animation;
        }
        Dirty = true;
    }

    internal void Observe()
    {
        Dirty = false;
    }
}
