using System.Text.Json;
using System.Text.Json.Nodes;

namespace AegiNext.Application;

internal static class EffectInstanceJsonMigration
{
    internal static void Upgrade(JsonObject document)
    {
        if (document["layers"] is not JsonArray layers)
        {
            return;
        }

        UpgradeLayers(layers);
    }

    private static void UpgradeLayers(JsonArray layers)
    {
        foreach (var value in layers)
        {
            if (value is not JsonObject layer)
            {
                continue;
            }
            if (layer.TryGetPropertyValue("effects", out var effects) && effects is not JsonArray { Count: 0 })
            {
                throw new JsonException("旧版本工程不能声明特效实例。");
            }
            layer["effects"] = new JsonArray();
            if (layer["children"] is JsonArray children)
            {
                UpgradeLayers(children);
            }
        }
    }
}
