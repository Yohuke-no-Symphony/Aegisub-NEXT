using System.Text.Json.Nodes;

namespace AegiNext.Application;

internal static class GraphicAssetJsonMigration
{
    internal static void Upgrade(JsonObject document)
    {
        var items = new JsonArray();
        foreach (var asset in document["assets"]?.AsArray() ?? [])
        {
            if (asset is not JsonObject value)
            {
                continue;
            }
            value.TryAdd("storage", "MANAGED");
            if (value["kind"]?.GetValue<string>() == "IMAGE")
            {
                var path = value["relativePath"]?.GetValue<string>() ?? string.Empty;
                var name = path.Split('/').LastOrDefault();
                items.Add(new JsonObject
                {
                    ["id"] = value["id"]?.DeepClone(),
                    ["assetId"] = value["id"]?.DeepClone(),
                    ["name"] = LegacyName(name),
                    ["folderId"] = null
                });
            }
        }
        document.TryAdd("assetLibrary", new JsonObject
        {
            ["autoCopyToSources"] = true,
            ["folders"] = new JsonArray(),
            ["items"] = items
        });
        UpgradeLayers(document["layers"] as JsonArray);
    }

    private static void UpgradeLayers(JsonArray? layers)
    {
        foreach (var layer in layers ?? [])
        {
            if (layer is not JsonObject value)
            {
                continue;
            }
            if (value["image"] is JsonObject image)
            {
                image.TryAdd("crop", new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = 1, ["height"] = 1 });
                image.TryAdd("playback", null);
            }
            if (value["shape"] is JsonObject shape)
            {
                shape.TryAdd("cornerRadius", 0);
                shape.TryAdd("arrowHeadRatio", 0.3);
                shape.TryAdd("arrowShaftRatio", 0.35);
            }
            UpgradeLayers(value["children"] as JsonArray);
        }
    }

    private static string LegacyName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Image";
        }
        if (name.Length <= 256)
        {
            return name;
        }
        return name[..(char.IsHighSurrogate(name[255]) ? 255 : 256)];
    }
}
