using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

internal static class LegacyProjectJsonFixture
{
    internal static JsonObject Create(int version)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "legacy");
        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.5));
        editor.Apply("Preset", document => document with
        {
            Presets = [new(Guid.NewGuid(), "legacy preset", [new(AnimationProperty.OPACITY, [new(new(0), 0.4)])])]
        });
        var root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        Downgrade(root, version);
        return root;
    }

    internal static void Downgrade(JsonObject root, int version)
    {
        root["version"] = version;
        LegacySubtitleMarginsJsonFixture.DowngradeProject(root);
        DowngradeAnimationTracks(root);
        foreach (var preset in root["presets"]!.AsArray().OfType<JsonObject>())
        {
            preset["mask"] = null;
        }
    }

    internal static void AddAnimationRows(JsonObject root, IEnumerable<TimelineAnimationRowId> rows)
    {
        root["timelineViewState"]!["collapsedAnimationRows"] = new JsonArray(rows.Select(row => (JsonNode)new JsonObject
        {
            ["scope"] = row.Scope.ToString(),
            ["ownerId"] = row.OwnerId,
            ["property"] = row.Property.ToString()
        }).ToArray());
    }

    private static void DowngradeAnimationTracks(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            if (value.ContainsKey("keyframes") && value["target"] is { } target)
            {
                value["property"] = target["property"]!.DeepClone();
                value.Remove("target");
            }
            foreach (var property in value)
            {
                DowngradeAnimationTracks(property.Value);
            }
        }
        else if (node is JsonArray values)
        {
            foreach (var item in values)
            {
                DowngradeAnimationTracks(item);
            }
        }
    }
}
