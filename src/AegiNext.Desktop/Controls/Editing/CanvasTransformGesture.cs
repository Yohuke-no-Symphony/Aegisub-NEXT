using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record CanvasTransformGesture(ProjectLayer Layer, LayerTransform EvaluatedTransform,
    ProjectLayerGeometry Geometry, Matrix LocalToScreen, Point Start, int HandleIndex)
{
    internal Matrix? WorldToScreen { get; init; }

    internal bool TryMap(Point local, out Point screen)
    {
        if (WorldToScreen is { } fit)
        {
            if (!Geometry.TryMapLocalPoint(new((float)local.X, (float)local.Y), out var world))
            {
                screen = default;
                return false;
            }
            screen = new Point(world.X, world.Y) * fit;
        }
        else
        {
            screen = local * LocalToScreen;
        }
        return double.IsFinite(screen.X) && double.IsFinite(screen.Y);
    }

    internal bool TryUnmap(Point screen, out Point local)
    {
        local = default;
        if (WorldToScreen is { } fit)
        {
            if (!fit.TryInvert(out var inverse))
            {
                return false;
            }
            var world = screen * inverse;
            if (!Geometry.TryUnmapWorldPoint(new((float)world.X, (float)world.Y), out var source))
            {
                return false;
            }
            local = new(source.X, source.Y);
        }
        else if (LocalToScreen.TryInvert(out var inverse))
        {
            local = screen * inverse;
        }
        else
        {
            return false;
        }
        return double.IsFinite(local.X) && double.IsFinite(local.Y);
    }
}
