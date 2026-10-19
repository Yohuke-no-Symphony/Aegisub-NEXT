using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SubtitleAppearancePersistenceTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task LegacyVersionsUpgradeEveryStylePathWithoutChangingContentOrFlatClipOwnership(int version)
    {
        using var directory = new TemporaryProjectDirectory();
        var original = Document(false);
        var root = LegacyProject(original, version);
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        var expected = ProjectStore.Serialize(original);
        using var parsed = JsonDocument.Parse(bytes);

        Assert.Equal(expected, ProjectStore.Serialize(ProjectStore.Deserialize(bytes)));
        Assert.Equal(expected, ProjectStore.Serialize(ProjectStore.Deserialize(parsed.RootElement)));
        var path = Path.Combine(directory.Path, "appearance.aeginext");
        await File.WriteAllBytesAsync(path, bytes);
        Assert.Equal(expected, ProjectStore.Serialize(await ProjectStore.LoadAsync(path)));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(version, root["version"]!.GetValue<int>());
        Assert.False(root["subtitles"]![0]!["style"]!.AsObject().ContainsKey("letterSpacing"));
        if (version == 9)
        {
            Assert.True(root.ContainsKey("tracks"));
            Assert.False(root["layers"]![0]!.AsObject().ContainsKey("children"));
        }
    }

    [Fact]
    public void CurrentProjectAndStyleLibraryRoundTripIndependentValuesAndAnimationProperties()
    {
        var document = Document(true);
        document = document with
        {
            Layers = [document.Layers[0] with
            {
                Tracks =
                [
                    new(AnimationProperty.LETTER_SPACING, [new(new(0), -8), new(new(2), 8)]),
                    new(AnimationProperty.FILL_BLUR, [new(new(0), 0), new(new(2), 12)]),
                    new(AnimationProperty.STROKE_BLUR, [])
                    {
                        InitialValue = 4, Transforms = [new(Guid.NewGuid(), new(0), new(2), 10)]
                    }
                ]
            }]
        };
        var bytes = ProjectStore.Serialize(document);
        Assert.Equal(bytes, ProjectStore.Serialize(ProjectStore.Deserialize(bytes)));
        var collection = new SubtitleStylePresetCollection
        {
            Presets = [new(Guid.NewGuid(), "Typography", document.Subtitles[0].Style)]
        };
        var stored = SubtitleStylePresetMetadataStore.Serialize(collection);
        Assert.Equal(stored, SubtitleStylePresetMetadataStore.Serialize(SubtitleStylePresetMetadataStore.Deserialize(stored)));
        Assert.Equal(ProjectDocument.CURRENT_VERSION, ProjectStore.Deserialize(bytes).Version);
        Assert.Equal(SubtitleStylePresetCollection.CURRENT_VERSION, SubtitleStylePresetMetadataStore.Deserialize(stored).Version);
    }

    [Theory]
    [InlineData("line")]
    [InlineData("track")]
    [InlineData("inline")]
    [InlineData("highlight")]
    [InlineData("karaokeInactive")]
    [InlineData("karaokeActive")]
    [InlineData("disabledInactive")]
    [InlineData("disabledActive")]
    public void EveryCurrentStylePathRequiresAllOfItsNewFields(string path)
    {
        foreach (var field in Fields(path))
        {
            var root = JsonNode.Parse(ProjectStore.Serialize(Document(true)))!.AsObject();
            Assert.True(StyleAt(root, path).Remove(field));
            Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        }
    }

    [Theory]
    [InlineData("line")]
    [InlineData("track")]
    [InlineData("inline")]
    [InlineData("highlight")]
    [InlineData("karaokeInactive")]
    [InlineData("karaokeActive")]
    [InlineData("disabledInactive")]
    [InlineData("disabledActive")]
    public void LegacyVersionsCannotSmuggleNewFieldsThroughAnyStylePath(string path)
    {
        foreach (var version in new[] { 3, 4, 5, 6, 7, 8, 9 })
        {
            foreach (var field in Fields(path))
            {
                var root = LegacyProject(Document(false), version);
                StyleAt(root, path)[field] = field == "wrapMode" ? JsonValue.Create("GRAPHEME") : JsonValue.Create(0);
                Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void LegacyStyleLibrariesAreRejected(int version)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{{\"version\":{version},\"presets\":[]}}");
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(bytes));
    }

    [Theory]
    [InlineData("letterSpacing")]
    [InlineData("wrapMode")]
    [InlineData("fillBlur")]
    [InlineData("strokeBlur")]
    public void CurrentStyleLibraryRejectsMissingAppearanceFields(string field)
    {
        var root = JsonNode.Parse(SubtitleStylePresetMetadataStore.Serialize(new()
        {
            Presets = [new(Guid.NewGuid(), "Current", new())]
        }))!.AsObject();
        root["presets"]![0]!["style"]!.AsObject().Remove(field);
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Theory]
    [InlineData("LETTER_SPACING")]
    [InlineData("FILL_BLUR")]
    [InlineData("STROKE_BLUR")]
    public void LegacyProjectsRejectUndeclaredAnimationProperties(string property)
    {
        foreach (var version in new[] { 3, 4, 5, 6, 7, 8, 9 })
        {
            var original = Document(false);
            original = original with
            {
                Layers = [original.Layers[0] with { Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5)])] }]
            };
            var root = LegacyProject(original, version);
            var track = root["layers"]![0]!["tracks"]![0]!.AsObject();
            if (version < 5)
            {
                track["property"] = property;
            }
            else
            {
                track["target"]!["property"] = property;
            }
            Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        }
    }

    private static JsonObject LegacyProject(ProjectDocument document, int version)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        if (version < 5)
        {
            LegacyProjectJsonFixture.Downgrade(root, version);
        }
        else
        {
            root["version"] = version;
            LegacySubtitleMarginsJsonFixture.DowngradeProject(root);
        }
        return root;
    }

    private static string[] Fields(string path) => path switch
    {
        "line" or "track" => ["letterSpacing", "wrapMode", "fillBlur", "strokeBlur"],
        "inline" => ["letterSpacing", "fillBlur", "strokeBlur"],
        _ => ["fillBlur", "strokeBlur"]
    };

    private static JsonObject StyleAt(JsonObject root, string path)
    {
        var line = root["subtitles"]![0]!;
        return (path switch
        {
            "line" => line["style"],
            "track" => (root["tracks"] ?? root["subtitleTracks"])![0]!["defaultStyle"],
            "inline" => line["inlineSpans"]![0]!["style"],
            "highlight" => line["karaokeStyle"],
            "karaokeInactive" => line["karaokeStyleSpans"] is null ? line["karaoke"]![0]!["inactiveStyle"] : line["karaokeStyleSpans"]![0]!["inactiveStyle"],
            "karaokeActive" => line["karaokeStyleSpans"] is null ? line["karaoke"]![0]!["activeStyle"] : line["karaokeStyleSpans"]![0]!["activeStyle"],
            "disabledInactive" => line["karaokeStyleSpans"] is null ? line["inactiveKaraoke"]![0]!["inactiveStyle"] : line["karaokeStyleSpans"]![1]!["inactiveStyle"],
            "disabledActive" => line["karaokeStyleSpans"] is null ? line["inactiveKaraoke"]![0]!["activeStyle"] : line["karaokeStyleSpans"]![1]!["activeStyle"],
            _ => throw new ArgumentOutOfRangeException(nameof(path))
        })!.AsObject();
    }

    private static ProjectDocument Document(bool newValues)
    {
        var style = newValues
            ? new SubtitleStyle { LetterSpacing = -2.5, FillBlur = 1.25, StrokeBlur = 4.5, WrapMode = SubtitleWrapMode.NATURAL }
            : new SubtitleStyle();
        var line = new SubtitleLine
        {
            Text = "ab", Start = new(81, 8), End = new(97, 8), Style = style,
            InlineSpans = [new(0, 1, new()
            {
                Italic = true, LetterSpacing = newValues ? 0 : null, FillBlur = newValues ? 3 : null, StrokeBlur = newValues ? 0 : null
            })],
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Glow", style),
            Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)],
            InactiveKaraoke = [new(1, 1, new(1), new(2), SceneColor.White)],
            KaraokeStyleSpans = [Span(0, newValues), Span(1, newValues)]
        };
        return new()
        {
            Tracks = [ProjectTrack.Default with { DefaultStyle = style, StylePresetId = Guid.NewGuid(), StylePresetName = "Typography" }],
            Subtitles = [line], Layers = [new() { SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }

    private static SubtitleKaraokeStyleSpan Span(int start, bool newValues) => new(start, 1,
        new() { ShadowBlur = start == 1 ? 0 : null, Stroke = SceneColor.White, FillBlur = newValues ? 7 : null, StrokeBlur = newValues ? 0 : null },
        new() { Fill = SceneColor.Black, FillBlur = newValues ? 0 : null, StrokeBlur = newValues ? 9 : null });
}
