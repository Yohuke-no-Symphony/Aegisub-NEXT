using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Rendering;

internal sealed class LayerPlacementResolver : IDisposable
{
    private readonly Lock gate = new();
    private ProjectSceneRenderer? renderer;
    private string? directory;
    private ProjectDocument? cachedDocument;
    private readonly Dictionary<Guid, (double? LetterSpacing, LayerPlacementResolution Result)> cache = [];

    internal LayerPlacementResolution Resolve(ProjectDocument document, string projectDirectory, ProjectLayer? layer, MediaTime? contentTime = null)
    {
        lock (gate)
        {
            return ResolveCore(document, projectDirectory, layer, contentTime);
        }
    }

    private LayerPlacementResolution ResolveCore(ProjectDocument document, string projectDirectory, ProjectLayer? layer, MediaTime? contentTime = null)
    {
        if (layer?.SubtitleId is not { } id)
        {
            var basePosition = layer?.Position is { } position
                ? LayerPositionGeometry.BasePosition(position, new(document.Width, document.Height)) : new ScenePoint();
            return new(basePosition, null);
        }

        if (renderer is null || directory != projectDirectory)
        {
            renderer?.Dispose();
            renderer = new(new DirectoryProjectAssetResolver(projectDirectory));
            directory = projectDirectory;
            cache.Clear();
        }

        if (!ReferenceEquals(cachedDocument, document))
        {
            cachedDocument = document;
            cache.Clear();
        }

        var spacingTrack = contentTime.HasValue
            ? layer.Tracks.FirstOrDefault(track => track.Property == AnimationProperty.LETTER_SPACING) : null;
        var letterSpacing = spacingTrack is not null
            ? SceneEvaluator.EvaluateScalarTrack(spacingTrack, contentTime!.Value) : (double?)null;
        if (cache.TryGetValue(id, out var cached) && cached.LetterSpacing == letterSpacing)
        {
            return cached.Result;
        }

        var subtitle = document.Subtitles.Single(line => line.Id == id);
        LayerPlacementResolution result;
        try
        {
            var measurement = letterSpacing.HasValue
                ? renderer.MeasureSubtitlePlacement(document, SceneEvaluator.EvaluateLayer(layer, subtitle, contentTime!.Value))
                : renderer.MeasureSubtitlePlacement(document, subtitle);
            var position = measurement.Position;
            result = new(new ScenePoint(position.Anchor.X * document.Width + position.Offset.X,
                position.Anchor.Y * document.Height + position.Offset.Y), position, Geometry: new(
                new(document.Width, document.Height), new(measurement.Bounds.Left, measurement.Bounds.Top),
                new(measurement.Bounds.Width, measurement.Bounds.Height), layer.Transform, measurement.HasInk));
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            result = new(null, null, error);
        }

        cache[id] = (letterSpacing, result);
        return result;
    }

    public void Dispose()
    {
        lock (gate)
        {
            renderer?.Dispose();
            renderer = null;
            cachedDocument = null;
            cache.Clear();
        }
    }
}
