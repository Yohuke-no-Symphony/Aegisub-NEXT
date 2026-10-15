using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class EffectInstancePersistenceTests
{
    private static readonly JsonSerializerOptions wireOptions = CreateWireOptions();

    [Fact]
    public void VersionNineteenRoundTripsSourceAndAllParameterKindsWithoutDerivedContent()
    {
        var source = EffectInstanceTestSources.SOURCE.Replace("scope clip current", """
            var flag = true
            var label = "文字"
            var direction = vec2(1, -2)
            var axis = vec3(1, 2, 3)
            var tint = rgba(2, 0.5, 0, 1)
            scope clip current
            """, StringComparison.Ordinal);
        var editor = new ProjectEditor(EffectInstanceTestSources.Document());
        editor.AddEffectInstances(editor.Snapshot.Layers[0].Id, "All types", source);
        var before = editor.Snapshot;

        var bytes = ProjectStore.Serialize(before);
        var restored = ProjectStore.Deserialize(bytes);

        Assert.Equal(19, restored.Version);
        Assert.Equal(bytes, ProjectStore.Serialize(restored));
        var original = Assert.Single(before.Layers[0].Effects);
        var instance = Assert.Single(restored.Layers[0].Effects);
        Assert.Equal(original.Id, instance.Id);
        Assert.Equal(original.Source, instance.Source);
        Assert.Equal(original.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            instance.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(new SceneVector3(1, 2, 3), instance.Parameters["axis"].Animation!.Value.Vector3);
        Assert.Equal(new SceneColor(2, 0.5, 0, 1), instance.Parameters["tint"].Animation!.Value.Color);
        Assert.Empty(restored.Layers[0].Tracks);
        Assert.Empty(restored.Subtitles[0].AnimationRanges);
        var workerProject = ProjectStore.Deserialize(JsonSerializer.SerializeToUtf8Bytes(before, wireOptions));
        Assert.Equal(bytes, ProjectStore.Serialize(workerProject));
    }

    [Fact]
    public void VersionEighteenMigratesMissingInstancesToAnEmptyStack()
    {
        var document = EffectInstanceTestSources.Document();
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        root["version"] = 18;
        root["layers"]![0]!.AsObject().Remove("effects");

        var restored = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));

        Assert.Equal(ProjectDocument.CURRENT_VERSION, restored.Version);
        Assert.Empty(restored.Layers[0].Effects);
        Assert.Equal(document.Layers[0].Id, restored.Layers[0].Id);
        Assert.Equal(document.Subtitles[0], restored.Subtitles[0]);
    }

    [Theory]
    [InlineData("legacy-instance")]
    [InlineData("missing-stack")]
    [InlineData("unknown-payload")]
    [InlineData("wrong-payload")]
    [InlineData("unknown-instance-field")]
    public void UndeclaredLegacyFeaturesAndInvalidInstancePayloadsAreRejected(string failure)
    {
        var editor = new ProjectEditor(EffectInstanceTestSources.Document());
        editor.AddEffectInstances(editor.Snapshot.Layers[0].Id, "Test", EffectInstanceTestSources.SOURCE);
        var root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        var layer = root["layers"]![0]!.AsObject();
        var instance = layer["effects"]![0]!.AsObject();
        var amount = instance["parameters"]!["amount"]!.AsObject();
        switch (failure)
        {
            case "legacy-instance":
                root["version"] = 18;
                break;
            case "missing-stack":
                layer.Remove("effects");
                break;
            case "unknown-payload":
                amount["text"] = "unexpected";
                break;
            case "wrong-payload":
                amount.Remove("number");
                amount["boolean"] = true;
                break;
            default:
                instance["tracks"] = new JsonArray();
                break;
        }

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("name")]
    [InlineData("source")]
    [InlineData("parameters")]
    [InlineData("time")]
    [InlineData("range")]
    public void ProjectValidationRejectsInvalidInstancesBeforeStorage(string failure)
    {
        var editor = new ProjectEditor(EffectInstanceTestSources.Document());
        editor.AddEffectInstances(editor.Snapshot.Layers[0].Id, "Test", EffectInstanceTestSources.SOURCE);
        var document = editor.Snapshot;
        var original = document.Layers[0].Effects[0];
        var invalid = failure switch
        {
            "identity" => original with { Id = Guid.Empty },
            "name" => original with { Name = "\n" },
            "source" => original with { Source = "invalid" },
            "parameters" => original with { Parameters = original.Parameters.SetItem("undeclared", EffectScriptParameterValue.FromBoolean(true)) },
            "time" => original with { End = new(5) },
            _ => original with { TargetContext = new(AnimationProperty.SCALE, TextRangeId: Guid.NewGuid()) }
        };
        document = document with { Layers = [document.Layers[0] with { Effects = [invalid] }] };

        Assert.Throws<InvalidDataException>(() => ProjectStore.Serialize(document));
    }

    [Fact]
    public void InstanceIdentityCanPersistInTimelineSelectionWithoutRequiringAnExpansion()
    {
        var document = EffectInstanceTestSources.Document();
        var id = new TimelineEffectClipId(document.Layers[0].Id, new(AnimationProperty.POSITION), Guid.NewGuid());
        document = document with { TimelineViewState = new() { ExpandedAnimationClips = [id] } };

        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(document));

        Assert.Equal(id, Assert.Single(restored.TimelineViewState.ExpandedAnimationClips));
    }

    private static JsonSerializerOptions CreateWireOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 128 };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
