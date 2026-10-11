using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class VersionThirteenAnimationMigrationTests
{
    [Fact]
    public async Task VersionThirteenGeneratedRangesAndReversedTranslationSurviveEveryLoadEntry()
    {
        using var directory = new TemporaryProjectDirectory();
        var source = CreateDocument();
        var root = CreateLegacyRoot(source, 13);
        LegacyProjectJsonFixture.AddAnimationRows(root,
            [new(TimelineRowScope.TRACK, ProjectTrack.DEFAULT_TRACK_ID, AnimationProperty.OPACITY)]);
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        using var parsed = JsonDocument.Parse(bytes);
        var path = Path.Combine(directory.Path, "version-thirteen.aeginext");
        await File.WriteAllBytesAsync(path, bytes);

        var loaded = new[]
        {
            ProjectStore.Deserialize(bytes),
            ProjectStore.Deserialize(parsed.RootElement),
            await ProjectStore.LoadAsync(path)
        };

        foreach (var document in loaded)
        {
            Assert.Equal(ProjectDocument.CURRENT_VERSION, document.Version);
            Assert.Equal(source.Subtitles[0].AnimationRanges.ToArray(), document.Subtitles[0].AnimationRanges.ToArray());
            var track = Assert.Single(Assert.Single(document.Layers).Tracks);
            Assert.Equal(source.Layers[0].Tracks[0].Target, track.Target);
            Assert.True(track.Keyframes[0].Reverse);
            Assert.True(Assert.Single(track.Keyframes[0].ComponentCurves)!.Reverse);
            Assert.Empty(document.TimelineViewState.ExpandedAnimationClips);
            Assert.Single(document.TimelineViewState.CollapsedAnimationRows);
            Assert.Equal(ProjectStore.Serialize(source), ProjectStore.Serialize(document));
        }
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("generatedOrigin")]
    [InlineData("reverse")]
    [InlineData("componentReverse")]
    [InlineData("rangePosition")]
    public void VersionTwelveStillRejectsFieldsAndTargetsIntroducedInVersionThirteen(string field)
    {
        var root = CreateLegacyRoot(CreateDocument(), 12);
        var range = root["subtitles"]![0]!["animationRanges"]![1]!.AsObject();
        var origin = range["generatedOrigin"]!.DeepClone();
        range.Remove("offset");
        range.Remove("generatedOrigin");
        var track = root["layers"]![0]!["tracks"]![0]!;
        var frame = track["keyframes"]![0]!.AsObject();
        var componentCurve = frame["componentCurves"]![0]!.AsObject();
        frame.Remove("reverse");
        componentCurve.Remove("reverse");
        track["target"]!["property"] = "SCALE";
        switch (field)
        {
            case "offset":
                range["offset"] = new JsonObject { ["x"] = 7, ["y"] = -3 };
                break;
            case "generatedOrigin":
                range["generatedOrigin"] = origin;
                break;
            case "reverse":
                frame["reverse"] = true;
                break;
            case "componentReverse":
                componentCurve["reverse"] = true;
                break;
            case "rangePosition":
                track["target"]!["property"] = "POSITION";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    private static JsonObject CreateLegacyRoot(ProjectDocument document, int version)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        root["version"] = version;
        root.Remove("assetLibrary");
        root["timelineViewState"]!.AsObject().Remove("expandedAnimationClips");
        return root;
    }

    private static ProjectDocument CreateDocument()
    {
        var parent = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var generated = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1)
        {
            Offset = new(7, -3),
            GeneratedOrigin = new("legacy-pulse", "letters", parent.Id, "grapheme")
        };
        var line = new SubtitleLine
        {
            Text = "AB", Start = new(0), End = new(2), AnimationRanges = [parent, generated]
        };
        return new()
        {
            Subtitles = [line],
            Layers =
            [
                new()
                {
                    Id = line.Id, SubtitleId = line.Id, Start = line.Start, End = line.End,
                    Tracks =
                    [
                        new(new AnimationTrackTarget(AnimationProperty.POSITION, TextRangeId: generated.Id),
                        [
                            new(new(0), new ScenePoint(1, 1), KeyframeInterpolation.EASE_IN)
                            {
                                Reverse = true,
                                ComponentCurves = [new(KeyframeInterpolation.POWER) { Exponent = 2, Reverse = true }]
                            },
                            new(new(2), new ScenePoint(2, 3))
                        ])
                    ]
                }
            ]
        };
    }
}
