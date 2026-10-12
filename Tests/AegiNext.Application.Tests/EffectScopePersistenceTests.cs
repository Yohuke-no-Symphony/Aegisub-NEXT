using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class EffectScopePersistenceTests
{
    [Fact]
    public void RangeTranslationOriginAndReversedComponentCurvesRoundTripWithoutLoss()
    {
        var parent = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var child = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1)
        {
            Offset = new(3, -12),
            GeneratedOrigin = new("pulse", "letters", parent.Id, "grapheme")
        };
        var first = new Keyframe(new(0), new ScenePoint(0, 0), KeyframeInterpolation.POWER)
        {
            Exponent = 3, Reverse = true,
            ComponentCurves = [new(KeyframeInterpolation.POWER, 0.25, 0.75) { Exponent = 2, Reverse = true }]
        };
        var line = new SubtitleLine { Text = "ab", End = new(2), AnimationRanges = [parent, child] };
        var track = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.POSITION, TextRangeId: child.Id),
            [first, new(new(2), new ScenePoint(0, -12))]);
        var document = new ProjectDocument
        {
            Subtitles = [line], Layers = [new() { SubtitleId = line.Id, End = line.End, Tracks = [track] }]
        };

        var bytes = ProjectStore.Serialize(document);
        var restored = ProjectStore.Deserialize(bytes);

        Assert.Equal(ProjectDocument.CURRENT_VERSION, restored.Version);
        Assert.Equal(bytes, ProjectStore.Serialize(restored));
        Assert.Equal(child, restored.Subtitles[0].AnimationRanges[1]);
        var restoredFrame = restored.Layers[0].Tracks[0].Keyframes[0];
        Assert.True(restoredFrame.Reverse);
        Assert.Equal(first.GetCurve(1), restoredFrame.GetCurve(1));
    }

    [Fact]
    public void VersionTwelveRetainsItsTextRangesAndExistingPositionOffsets()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = new SubtitleLine
        {
            Text = "ab", End = new(2), AnimationRanges = [range],
            Style = new() { Position = new() { Offset = new(7, 9) } }
        };
        var root = JsonNode.Parse(ProjectStore.Serialize(new ProjectDocument
        {
            Subtitles = [line], Layers = [new() { SubtitleId = line.Id, End = line.End,
                Tracks = [new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(1, 2))])] }]
        }))!.AsObject();
        root["version"] = 12;
        LegacySubtitleScalingJsonFixture.DowngradeProject(root);

        var restored = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));

        Assert.Equal(ProjectDocument.CURRENT_VERSION, restored.Version);
        Assert.Equal(range, Assert.Single(restored.Subtitles[0].AnimationRanges));
        Assert.Equal(new ScenePoint(7, 9), restored.Subtitles[0].Style.Position!.Offset);
        Assert.False(restored.Layers[0].Tracks[0].Keyframes[0].Reverse);
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("generatedOrigin")]
    [InlineData("reverse")]
    [InlineData("componentReverse")]
    [InlineData("scopedPosition")]
    public void VersionTwelveRejectsUndeclaredScopeAndCurveFields(string field)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = new SubtitleLine { Text = "ab", End = new(2), AnimationRanges = [range] };
        var document = new ProjectDocument
        {
            Subtitles = [line], Layers = [new() { SubtitleId = line.Id, End = line.End,
                Tracks = [new(AnimationProperty.SCALE, [new(new(0), new ScenePoint(1, 1))])] }]
        };
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        root["version"] = 12;
        LegacySubtitleScalingJsonFixture.DowngradeProject(root);
        var serializedRange = root["subtitles"]![0]!["animationRanges"]![0]!.AsObject();
        var track = root["layers"]![0]!["tracks"]![0]!.AsObject();
        var frame = track["keyframes"]![0]!.AsObject();
        switch (field)
        {
            case "offset":
                serializedRange["offset"] = JsonNode.Parse("{\"x\":0,\"y\":0}");
                break;
            case "generatedOrigin":
                serializedRange["generatedOrigin"] = null;
                break;
            case "reverse":
                frame["reverse"] = false;
                break;
            case "componentReverse":
                frame["componentCurves"] = JsonNode.Parse("[{\"interpolation\":\"LINEAR\",\"curveStart\":0,\"curveEnd\":1,\"reverse\":false}]");
                break;
            case "scopedPosition":
                track["target"]!["property"] = "POSITION";
                track["target"]!["textRangeId"] = range.Id;
                break;
        }

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Fact]
    public void GeneratedOriginsRejectMissingParentsAndCycles()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = new SubtitleAnimationRange(firstId, 0, 1)
        {
            GeneratedOrigin = new("pulse", "letters", secondId, "grapheme")
        };
        var line = new SubtitleLine { Text = "ab", End = new(2), AnimationRanges = [first] };
        var document = new ProjectDocument { Subtitles = [line] };
        Assert.Throws<InvalidDataException>(() => ProjectStore.Serialize(document));

        var second = new SubtitleAnimationRange(secondId, 1, 1)
        {
            GeneratedOrigin = new("pulse", "letters", firstId, "grapheme")
        };
        Assert.Throws<InvalidDataException>(() => ProjectStore.Serialize(document with
        {
            Subtitles = [line with { AnimationRanges = [first, second] }]
        }));
    }

    [Fact]
    public void GeneratedOriginCannotReferenceARangeInAnotherSubtitle()
    {
        var parent = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var child = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1)
        {
            GeneratedOrigin = new("pulse", "letters", parent.Id, "grapheme")
        };
        var first = new SubtitleLine { Text = "a", End = new(2), AnimationRanges = [parent] };
        var second = new SubtitleLine { Text = "b", End = new(2), AnimationRanges = [child] };

        Assert.Throws<InvalidDataException>(() => ProjectStore.Serialize(new() { Subtitles = [first, second] }));
    }

    [Theory]
    [InlineData("Pulse", "letters", "grapheme")]
    [InlineData("pulse", "two words", "grapheme")]
    [InlineData("pulse", "letters", "")]
    [InlineData("pulse", "letters", "\u0000")]
    public void GeneratedOriginsRejectInvalidDefinitions(string effectId, string scopeName, string unit)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1)
        {
            GeneratedOrigin = new(effectId, scopeName, null, unit)
        };
        var line = new SubtitleLine { Text = "a", End = new(2), AnimationRanges = [range] };

        Assert.Throws<InvalidDataException>(() => ProjectStore.Serialize(new() { Subtitles = [line] }));
    }
}
