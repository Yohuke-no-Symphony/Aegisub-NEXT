using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SubtitleAnimationPersistenceTests
{
    [Fact]
    public void CurrentProjectRoundTripsRangeStateColorSpaceAndOrderedComponents()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        var subtitle = new SubtitleLine { Text = "abc", End = new(2), AnimationRanges = [range] };
        var track = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id,
            State: SubtitleAnimationState.ACTIVE), [])
        {
            ColorSpace = AnimationColorSpace.SRGB, InitialValue = SceneColor.White,
            Transforms = [new(Guid.NewGuid(), new(0), new(2), new SceneColor(1, 1, 1, 0.5))
            {
                ComponentMask = 8, Mode = AnimationTransformMode.MULTIPLY_BY
            }]
        };
        var document = new ProjectDocument
        {
            Subtitles = [subtitle], Layers = [new() { SubtitleId = subtitle.Id, End = subtitle.End, Tracks = [track] }]
        };
        var bytes = ProjectStore.Serialize(document);

        Assert.Equal(bytes, ProjectStore.Serialize(ProjectStore.Deserialize(bytes)));
        var restored = ProjectStore.Deserialize(bytes);
        Assert.Equal(range, restored.Subtitles[0].AnimationRanges[0]);
        Assert.Equal(track.Target, restored.Layers[0].Tracks[0].Target);
        Assert.Equal(AnimationColorSpace.SRGB, restored.Layers[0].Tracks[0].ColorSpace);
    }

    [Fact]
    public void DefaultRangePivotAndRotationRemainOptionalButScaleIsRequired()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = new SubtitleLine { Text = "a", End = new(2), AnimationRanges = [range] };
        var document = new ProjectDocument
        {
            Subtitles = [line], Layers = [new() { SubtitleId = line.Id, End = line.End }]
        };
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        var serializedRange = root["subtitles"]![0]!["animationRanges"]![0]!.AsObject();

        Assert.False(serializedRange.ContainsKey("pivot"));
        Assert.False(serializedRange.ContainsKey("rotation"));
        Assert.Equal(range, ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())).Subtitles[0].AnimationRanges[0]);
        serializedRange.Remove("scale");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void LegacyProjectsUpgradeToTextAnimationVersion(int version)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(new ProjectDocument()))!.AsObject();
        root["version"] = version;
        LegacySubtitleScalingJsonFixture.DowngradeProject(root);

        var document = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));

        Assert.Equal(ProjectDocument.CURRENT_VERSION, document.Version);
        Assert.Equal(version, root["version"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("FONT_SIZE")]
    [InlineData("SHADOW_OFFSET")]
    [InlineData("SHADOW_BLUR")]
    [InlineData("SHADOW_COLOR")]
    public void LegacyProjectsRejectUndeclaredAnimationProperties(string property)
    {
        var subtitle = new SubtitleLine { Text = "abc", Start = new(0), End = new(2) };
        var document = new ProjectDocument
        {
            Subtitles = [subtitle],
            Layers = [new() { SubtitleId = subtitle.Id, Start = subtitle.Start, End = subtitle.End,
                Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 1)])] }]
        };
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        root["version"] = 11;
        LegacySubtitleScalingJsonFixture.DowngradeProject(root);
        root["layers"]![0]!["tracks"]![0]!["target"]!["property"] = property;

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Theory]
    [InlineData("textRangeId", "\"d3c64634-657b-477d-b077-3586ad8518b1\"")]
    [InlineData("state", "\"ACTIVE\"")]
    public void LegacyProjectsRejectUndeclaredTargetFields(string name, string json)
    {
        var subtitle = new SubtitleLine { Text = "abc", Start = new(0), End = new(2) };
        var document = new ProjectDocument
        {
            Subtitles = [subtitle],
            Layers = [new() { SubtitleId = subtitle.Id, Start = subtitle.Start, End = subtitle.End,
                Tracks = [new(AnimationProperty.FILL, [new(new(0), SceneColor.White)])] }]
        };
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        root["version"] = 11;
        LegacySubtitleScalingJsonFixture.DowngradeProject(root);
        root["layers"]![0]!["tracks"]![0]!["target"]![name] = JsonNode.Parse(json);

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }
}
