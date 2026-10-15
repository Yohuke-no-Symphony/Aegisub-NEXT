using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Rendering.Projects;
using System.Collections.Immutable;

namespace AegiNext.Desktop.Rendering;

internal sealed class LayerPlacementResolver : IDisposable
{
    private readonly Lock gate = new();
    private ProjectSceneRenderer? renderer;
    private string? directory;
    private ProjectDocument? cachedDocument;
    private readonly EffectCompilationCache effectPlans = new();
    private readonly Dictionary<Guid, (ImmutableDictionary<AnimationTrackTarget, AnimationValue> Typography,
        ImmutableArray<EffectStyleOperation> Effects, LayerPlacementResolution Result)> cache = [];

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

        var subtitle = document.Subtitles.Single(line => line.Id == id);
        var evaluated = contentTime.HasValue ? SceneEvaluator.EvaluateLayer(layer, subtitle, contentTime.Value, effectPlans) : null;
        var typography = evaluated?.AnimationValues.Where(pair => pair.Key.Property is AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING)
            .ToImmutableDictionary() ?? ImmutableDictionary<AnimationTrackTarget, AnimationValue>.Empty;
        var typographyEffects = evaluated?.EffectStyleOperations.Where(operation =>
            operation.Target.Property is AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING).ToImmutableArray() ?? [];
        if (cache.TryGetValue(id, out var cached) && typography.Count == cached.Typography.Count &&
            typography.All(pair => cached.Typography.TryGetValue(pair.Key, out var previous) && previous == pair.Value) &&
            typographyEffects.SequenceEqual(cached.Effects))
        {
            return cached.Result;
        }

        LayerPlacementResolution result;
        try
        {
            var measurement = evaluated is not null
                ? renderer.MeasureSubtitlePlacement(document, evaluated)
                : renderer.MeasureSubtitlePlacement(document, subtitle);
            var position = measurement.Position;
            result = new(new ScenePoint(position.Anchor.X * document.Width + position.Offset.X,
                position.Anchor.Y * document.Height + position.Offset.Y),
                SubtitleStyleScaling.ToReferencePosition(position, subtitle.Style.Scaling, document.Width, document.Height), Geometry: new(
                new(document.Width, document.Height), new(measurement.Bounds.Left, measurement.Bounds.Top),
                new(measurement.Bounds.Width, measurement.Bounds.Height), layer.Transform, measurement.HasInk)
                {
                    OffsetScale = SubtitleStyleScaling.GetScaleFactor(subtitle.Style.Scaling, document.Width, document.Height)
                });
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            result = new(null, null, error);
        }

        cache[id] = (typography, typographyEffects, result);
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
