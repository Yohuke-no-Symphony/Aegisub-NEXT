using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class ClipMaskPlacementJsonMigration
{
    internal static void Upgrade(JsonObject document, JsonSerializerOptions options)
    {
        var width = document["width"]?.Deserialize<int>(options) ?? throw new JsonException("The project canvas width is missing.");
        var height = document["height"]?.Deserialize<int>(options) ?? throw new JsonException("The project canvas height is missing.");
        if (width <= 0 || height <= 0)
        {
            throw new JsonException("The project canvas dimensions must be positive.");
        }

        if (document["layers"] is JsonArray layers)
        {
            UpgradeLayers(layers, new(width, height), options);
        }
    }

    private static void UpgradeLayers(JsonArray layers, ScenePoint canvasSize, JsonSerializerOptions options)
    {
        foreach (var item in layers)
        {
            if (item is not JsonObject layer)
            {
                continue;
            }

            if (layer["mask"] is JsonObject mask)
            {
                UpgradeMask(mask, canvasSize, options);
            }
            if (layer["children"] is JsonArray children)
            {
                UpgradeLayers(children, canvasSize, options);
            }
        }
    }

    private static void UpgradeMask(JsonObject mask, ScenePoint canvasSize, JsonSerializerOptions options)
    {
        if (mask.ContainsKey("placement") || mask.ContainsKey("referenceFrame"))
        {
            throw new JsonException("Projects older than version 18 cannot declare native mask placement or reference frames.");
        }
        if (mask["transform"] is not JsonObject transform || transform["pivot"] is not { } pivotNode)
        {
            throw new JsonException("A legacy mask must contain its complete pixel pivot.");
        }

        var pivot = pivotNode.Deserialize<ScenePoint>(options);
        if (!double.IsFinite(pivot.X) || !double.IsFinite(pivot.Y) ||
            pivot.X is < -1e9 or > 1e9 || pivot.Y is < -1e9 or > 1e9)
        {
            throw new JsonException("The legacy mask pixel pivot exceeds its finite coordinate budget.");
        }

        var prepared = mask.DeepClone().AsObject();
        prepared["transform"]!.AsObject().Remove("pivot");
        prepared["placement"] = JsonSerializer.SerializeToNode(new LayerPosition(), options);
        prepared["referenceFrame"] = JsonSerializer.SerializeToNode(new MaskReferenceFrame(), options);
        var geometry = prepared.Deserialize<ClipMask>(options) ?? throw new JsonException("A legacy mask cannot be null.");
        var line = new SubtitleLine();
        ProjectValidator.Validate(new()
        {
            Subtitles = [line], Layers = [new() { SubtitleId = line.Id, Start = line.Start, End = line.End, Mask = geometry }]
        });
        var frame = ClipMaskPlacementGeometry.CaptureReferenceFrame(geometry);
        var anchor = new ScenePoint(Math.Clamp(pivot.X / canvasSize.X, 0, 1), Math.Clamp(pivot.Y / canvasSize.Y, 0, 1));
        var placement = new LayerPosition
        {
            Anchor = anchor,
            Pivot = new((pivot.X - frame.Origin.X) / frame.Size.X, (pivot.Y - frame.Origin.Y) / frame.Size.Y),
            Offset = new(pivot.X - anchor.X * canvasSize.X, pivot.Y - anchor.Y * canvasSize.Y)
        };
        transform.Remove("pivot");
        mask["placement"] = JsonSerializer.SerializeToNode(placement, options);
        mask["referenceFrame"] = JsonSerializer.SerializeToNode(frame, options);
    }
}
