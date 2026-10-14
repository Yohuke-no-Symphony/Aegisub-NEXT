using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class ProjectMergeTests
{
    [Fact]
    public void MergeCopiesTemplateProjectsWithoutOverwritingExistingIdentities()
    {
        var template = CreateDocument();
        var source = new ProjectMergeSource(template, "Part", "/source");

        var result = ProjectEditingOperations.MergeProjects(template, [source, source]);

        Assert.Equal(template.Id, result.Document.Id);
        Assert.Equal(6, result.ImportedTrackIds.Length);
        Assert.Equal(6, result.ImportedLayerIds.Length);
        Assert.Equal(2, result.ImportedSubtitleIds.Length);
        Assert.Same(template.Subtitles[0], result.Document.Subtitles[0]);
        Assert.Same(template.Layers[0], result.Document.Layers[0]);
        Assert.Equal(9, result.Document.Tracks.Length);
        Assert.Equal(result.ImportedTrackIds, result.Document.Tracks.Take(6).Skip(3).Concat(result.Document.Tracks.Take(3)).Select(track => track.Id));
        Assert.Equal(result.ImportedSubtitleIds, result.Document.Subtitles.Skip(1).Select(line => line.Id));
        var allLayers = Flatten(result.Document.Layers);
        Assert.Equal(allLayers.Length, allLayers.Select(layer => layer.Id).Distinct().Count());
        Assert.Equal(result.ImportedLayerIds, allLayers.Skip(3).Select(layer => layer.Id));
        Assert.Equal("Part／Subtitles", result.Document.Tracks[3].Name);
        Assert.Equal("Part／Subtitles (2)", result.Document.Tracks[0].Name);
        for (var index = 1; index < result.Document.Subtitles.Length; index++)
        {
            var line = result.Document.Subtitles[index];
            var layer = allLayers.Single(value => value.SubtitleId == line.Id);
            Assert.NotEqual(template.Subtitles[0].Id, line.Id);
            Assert.Equal(line.Id, layer.Id);
            Assert.Equal(result.ImportedTrackIds[(index - 1) * 3], layer.TrackId);
        }
        ProjectValidator.Validate(result.Document);
    }

    [Fact]
    public void MergePreservesAbsoluteTimeVisualsAndEveryRelativeAnimationClock()
    {
        var source = CreateDocument();
        var target = new ProjectDocument { FrameRate = new(60, 1) };

        var result = ProjectEditingOperations.MergeProjects(target, [new(source, "Segment", "/source")]);

        var original = source.Layers[1];
        var copy = result.Document.Layers[1];
        Assert.Equal(original.Start, copy.Start);
        Assert.Equal(original.End, copy.End);
        Assert.Equal(original.AnimationOffset, copy.AnimationOffset);
        Assert.Equal(original.Transform, copy.Transform);
        Assert.Equal(original.MotionPath, copy.MotionPath);
        Assert.Equal(original.Blend, copy.Blend);
        Assert.Equal(original.Opacity, copy.Opacity);
        Assert.Equal(original.Tracks[0].Keyframes, copy.Tracks[0].Keyframes);
        Assert.Equal(original.Tracks[1].InitialValue, copy.Tracks[1].InitialValue);
        var operation = Assert.Single(copy.Tracks[1].Transforms);
        Assert.NotEqual(original.Tracks[1].Transforms[0].Id, operation.Id);
        Assert.Equal(original.Tracks[1].Transforms[0] with { Id = operation.Id }, operation);
        var originalMask = Assert.IsType<VectorClipMask>(original.Mask);
        var copiedMask = Assert.IsType<VectorClipMask>(copy.Mask);
        Assert.Equal(originalMask.Transform, copiedMask.Transform);
        Assert.Equal(originalMask.Inverted, copiedMask.Inverted);
        Assert.NotEqual(originalMask.Contours[0].Id, copiedMask.Contours[0].Id);
        Assert.Equal(copiedMask.Contours[0].Nodes[0].Id, copy.Tracks[0].Target.NodeId);
        for (var index = 0; index < originalMask.Contours[0].Nodes.Length; index++)
        {
            var node = copiedMask.Contours[0].Nodes[index];
            Assert.NotEqual(originalMask.Contours[0].Nodes[index].Id, node.Id);
            Assert.Equal(originalMask.Contours[0].Nodes[index] with { Id = node.Id }, node);
        }
        var sourceLine = source.Subtitles[0];
        var copiedLine = result.Document.Subtitles[0];
        Assert.Equal(sourceLine.Text, copiedLine.Text);
        Assert.Equal(sourceLine.StyleName, copiedLine.StyleName);
        Assert.Same(sourceLine.KaraokeStyle, copiedLine.KaraokeStyle);
        Assert.Equal(sourceLine.Karaoke[0] with { Id = copiedLine.Karaoke[0].Id }, copiedLine.Karaoke[0]);
        Assert.NotEqual(sourceLine.Karaoke[0].Id, copiedLine.Karaoke[0].Id);
        Assert.Equal(sourceLine.InactiveKaraoke[0] with { Id = copiedLine.InactiveKaraoke[0].Id }, copiedLine.InactiveKaraoke[0]);
        Assert.NotEqual(sourceLine.InactiveKaraoke[0].Id, copiedLine.InactiveKaraoke[0].Id);
        Assert.Equal(target.FrameRate, result.Document.FrameRate);
    }

    [Fact]
    public void MergeImportsOnlyUsedFontsAndImagesAndRemapsEveryReference()
    {
        var source = CreateDocument();
        var dependencies = ProjectEditingOperations.GetMergeAssetIds(source);

        Assert.Equal(source.Assets.Take(4).Select(asset => asset.Id).ToHashSet(), dependencies.ToHashSet());
        var result = ProjectEditingOperations.MergeProjects(new(), [new(source, "Part", "/unused")]);

        Assert.Equal(4, result.Document.Assets.Length);
        Assert.DoesNotContain(result.Document.Assets, asset => asset.Kind == ProjectAssetKind.MEDIA);
        for (var index = 0; index < 4; index++)
        {
            var copied = result.Document.Assets[index];
            Assert.NotEqual(source.Assets[index].Id, copied.Id);
            Assert.Equal(source.Assets[index] with { Id = copied.Id }, copied);
        }
        var line = Assert.Single(result.Document.Subtitles);
        Assert.Equal(result.Document.Assets[0].Id, line.Style.FontAssetId);
        Assert.Equal(result.Document.Assets[1].Id, line.InlineSpans[0].Style.FontAssetId);
        Assert.Equal(result.Document.Assets[2].Id, result.Document.Layers[2].Image!.AssetId);
        var track = result.Document.Tracks[0];
        Assert.Equal(result.Document.Assets[3].Id, track.DefaultStyle!.FontAssetId);
        Assert.Equal(source.Tracks[0].StylePresetId, track.StylePresetId);
        Assert.Equal(source.Tracks[0].StylePresetName, track.StylePresetName);
        Assert.Equal(source.Tracks[0].AutoApplyStyle, track.AutoApplyStyle);
        Assert.Equal(source.Subtitles[0].Style with { FontAssetId = line.Style.FontAssetId }, line.Style);
        Assert.Equal(source.Subtitles[0].InlineSpans[0].Style with { FontAssetId = line.InlineSpans[0].Style.FontAssetId },
            line.InlineSpans[0].Style);
        Assert.Null(result.Document.Media);
    }

    [Fact]
    public void MergeRetainsTargetSettingsMediaAndViewStateAndCopiesEffectPresets()
    {
        var source = CreateDocument();
        var target = source with
        {
            Name = "Master", FrameRate = new(24000, 1001),
            TimelineViewState = new()
            {
                CollapsedAnimationRows = [new(TimelineRowScope.TRACK, source.Tracks[0].Id, AnimationProperty.OPACITY)]
            }
        };

        var result = ProjectEditingOperations.MergeProjects(target, [new(source, "Part", "/source")]);

        Assert.Equal(target.Id, result.Document.Id);
        Assert.Equal(target.Name, result.Document.Name);
        Assert.Equal(target.Version, result.Document.Version);
        Assert.Equal(target.Width, result.Document.Width);
        Assert.Equal(target.Height, result.Document.Height);
        Assert.Equal(target.ReferenceWhiteNits, result.Document.ReferenceWhiteNits);
        Assert.Same(target.FrameRate, result.Document.FrameRate);
        Assert.Same(target.Media, result.Document.Media);
        Assert.Same(target.TimelineViewState, result.Document.TimelineViewState);
        var importedPreset = result.Document.Presets[1];
        Assert.NotEqual(source.Presets[0].Id, importedPreset.Id);
        Assert.Equal(source.Presets[0].Name, importedPreset.Name);
        Assert.Equal(source.Presets[0].MotionPath, importedPreset.MotionPath);
        Assert.Equal(source.Presets[0].Blend, importedPreset.Blend);
        Assert.NotEqual(source.Presets[0].Tracks[0].Transforms[0].Id, importedPreset.Tracks[0].Transforms[0].Id);
    }

    [Fact]
    public void MergeUsesOneUndoTransactionAndRoundTripsAllImportedReferences()
    {
        var original = CreateDocument();
        var editor = new ProjectEditor(original);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        var result = editor.MergeProjects([new(original, "Part A", "/a"), new(original, "Part B", "/b")]);

        Assert.Same(editor.Snapshot, result.Document);
        Assert.Equal(1, changes);
        Assert.True(editor.HasUnsavedChanges);
        var merged = editor.Snapshot;
        var reopened = ProjectStore.Deserialize(ProjectStore.Serialize(merged));
        Assert.Equal(result.ImportedTrackIds, reopened.Tracks.Take(6).Skip(3).Concat(reopened.Tracks.Take(3)).Select(track => track.Id));
        Assert.Equal(result.ImportedLayerIds, Flatten(reopened.Layers).Skip(3).Select(layer => layer.Id));
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(editor.HasUnsavedChanges);
        Assert.True(editor.Redo());
        Assert.Same(merged, editor.Snapshot);
    }

    [Theory]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("white")]
    public void IncompatibleLaterSourceRejectsEntireBatchWithoutChangingHistory(string field)
    {
        var original = CreateDocument();
        var incompatible = field switch
        {
            "width" => original with { Width = 1280 },
            "height" => original with { Height = 720 },
            _ => original with { ReferenceWhiteNits = 100 }
        };
        var editor = new ProjectEditor(original);
        editor.Apply("rename", document => document with { Name = "Changed" });
        Assert.True(editor.Undo());
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        Assert.Throws<InvalidDataException>(() => editor.MergeProjects([
            new(original, "Valid", "/valid"), new(incompatible, "Invalid", "/invalid")]));

        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void InvalidSourceRejectsEntireBatchAndDependencyCollection()
    {
        var original = CreateDocument();
        var invalid = original with { Assets = original.Assets.RemoveAt(0) };
        var editor = new ProjectEditor(original);

        Assert.Throws<InvalidDataException>(() => ProjectEditingOperations.GetMergeAssetIds(invalid));
        Assert.Throws<InvalidDataException>(() => editor.MergeProjects([new(original, "Valid", "/v"), new(invalid, "Invalid", "/i")]));

        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void LongUnicodeSourceLabelsProduceValidUniqueTrackNames()
    {
        var source = CreateDocument();
        var label = string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦e\u0301", 50));

        var result = ProjectEditingOperations.MergeProjects(new(), [new(source, label, "/a"), new(source, label, "/b")]);

        var names = result.Document.Tracks.Take(6).Select(track => track.Name).ToArray();
        Assert.Equal(6, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name =>
        {
            Assert.InRange(name.Length, 1, 128);
            ProjectValidator.ValidateText(name);
            Assert.False(char.IsHighSurrogate(name[^1]));
            Assert.False(name.EndsWith('‍'));
        });
        ProjectValidator.Validate(result.Document);
    }

    [Fact]
    public void EmptyBatchKeepsTheOriginalSnapshotAndDoesNotCreateHistory()
    {
        var original = CreateDocument();
        var editor = new ProjectEditor(original);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        var result = editor.MergeProjects([]);

        Assert.Same(original, result.Document);
        Assert.Empty(result.ImportedTrackIds);
        Assert.Empty(result.ImportedSubtitleIds);
        Assert.Empty(result.ImportedLayerIds);
        Assert.False(editor.CanUndo);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void LayerIdentityDistinctFromItsSubtitleStaysDistinctAfterMerge()
    {
        var source = CreateDocument();
        source = source with
        {
            Layers = source.Layers.SetItem(1, source.Layers[1] with { Id = Guid.NewGuid() })
        };

        var result = ProjectEditingOperations.MergeProjects(new(), [new(source, "Distinct", "/source")]);

        var layer = result.Document.Layers[1];
        var line = Assert.Single(result.Document.Subtitles);
        Assert.Equal(line.Id, layer.SubtitleId);
        Assert.NotEqual(line.Id, layer.Id);
        Assert.NotEqual(source.Layers[1].Id, layer.Id);
    }

    [Fact]
    public void RepeatedLocalMaskAndOperationIdsAreRemappedSeparatelyForEachLayer()
    {
        var source = CreateDocument();
        var originalLayer = source.Layers[1];
        var secondLine = source.Subtitles[0] with { Id = Guid.NewGuid(), Start = new(40), End = new(42) };
        var secondLayer = originalLayer with
        {
            Id = secondLine.Id, SubtitleId = secondLine.Id, Start = secondLine.Start, End = secondLine.End
        };
        source = source with
        {
            Subtitles = source.Subtitles.Add(secondLine),
            Layers = source.Layers.Add(secondLayer)
        };

        var result = ProjectEditingOperations.MergeProjects(new(), [new(source, "Repeated local", "/source")]);

        var first = result.Document.Layers[1];
        var second = result.Document.Layers[3];
        var firstNode = Assert.IsType<VectorClipMask>(first.Mask).Contours[0].Nodes[0].Id;
        var secondNode = Assert.IsType<VectorClipMask>(second.Mask).Contours[0].Nodes[0].Id;
        Assert.NotEqual(firstNode, secondNode);
        Assert.Equal(firstNode, first.Tracks[0].Target.NodeId);
        Assert.Equal(secondNode, second.Tracks[0].Target.NodeId);
        Assert.NotEqual(first.Tracks[1].Transforms[0].Id, second.Tracks[1].Transforms[0].Id);
        Assert.NotEqual(result.Document.Subtitles[0].Karaoke[0].Id, result.Document.Subtitles[1].Karaoke[0].Id);
    }

    [Fact]
    public void EffectPresetNodeTargetsGetConsistentIndependentIdentities()
    {
        var source = CreateDocument();
        var nodeId = Guid.NewGuid();
        var nodePreset = new EffectPreset(Guid.NewGuid(), "Node effect",
        [
            new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, nodeId), [new(MediaTime.Zero, new ScenePoint(1, 2))]),
            new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_IN_HANDLE, nodeId), [new(MediaTime.Zero, new ScenePoint(3, 4))])
        ]);
        source = source with { Presets = [nodePreset] };

        var result = ProjectEditingOperations.MergeProjects(new(), [new(source, "Preset", "/source")]);

        var preset = Assert.Single(result.Document.Presets);
        Assert.NotEqual(nodeId, preset.Tracks[0].Target.NodeId);
        Assert.Equal(preset.Tracks[0].Target.NodeId, preset.Tracks[1].Target.NodeId);
    }

    [Fact]
    public void PreparedMergeSnapshotPreservesMultiCharacterKaraokeBeforeItsSingleApply()
    {
        var source = CreateDocument();
        source = source with
        {
            Subtitles = [source.Subtitles[0] with
            {
                Karaoke = [new(0, 2, new(1, 3), new(7, 3), SceneColor.White)], InactiveKaraoke = []
            }]
        };
        var editor = new ProjectEditor();

        var result = ProjectEditingOperations.MergeProjects(editor.Snapshot, [new(source, "Normalize", "/source")]);
        editor.Apply("Prepared merge", _ => result.Document);

        Assert.Same(result.Document, editor.Snapshot);
        Assert.Single(Assert.Single(result.Document.Subtitles).Karaoke);
        Assert.Single(source.Subtitles[0].Karaoke);
        Assert.Equal(2, source.Subtitles[0].Karaoke[0].Utf16Length);
    }

    [Fact]
    public void MatchingPreparedResourcesAreReusedAcrossTargetAndSourcesWithAllReferencesMapped()
    {
        var source = CreateDocument();
        source = source with
        {
            Assets = source.Assets.Select((asset, index) => index < 4
                ? asset with { Sha256 = (index + 10).ToString("x64", System.Globalization.CultureInfo.InvariantCulture) } : asset).ToImmutableArray()
        };
        var targetAssets = source.Assets.Take(4).Select(asset => asset with
        {
            Id = Guid.NewGuid(), Sha256 = asset.Sha256!.ToUpperInvariant()
        }).ToImmutableArray();
        var target = new ProjectDocument { Assets = targetAssets };

        var result = ProjectEditingOperations.MergeProjects(target, [new(source, "A", "/a"), new(source, "B", "/b")]);

        Assert.Equal<ProjectAsset>(targetAssets, result.Document.Assets);
        Assert.All(result.Document.Subtitles, line =>
        {
            Assert.Equal(targetAssets[0].Id, line.Style.FontAssetId);
            Assert.Equal(targetAssets[1].Id, line.InlineSpans[0].Style.FontAssetId);
        });
        Assert.All(result.Document.Tracks.Where(track => track.DefaultStyle is not null), track => Assert.Equal(targetAssets[3].Id, track.DefaultStyle!.FontAssetId));
        Assert.All(result.Document.Layers.Where(clip => clip.Kind == LayerKind.IMAGE), clip => Assert.Equal(targetAssets[2].Id, clip.Image!.AssetId));
        var intoEmpty = ProjectEditingOperations.MergeProjects(new(), [new(source, "A", "/a"), new(source, "B", "/b")]);
        Assert.Equal(4, intoEmpty.Document.Assets.Length);
        Assert.Equal(intoEmpty.Document.Subtitles[0].Style.FontAssetId, intoEmpty.Document.Subtitles[1].Style.FontAssetId);
        Assert.Equal(intoEmpty.Document.Layers[2].Image!.AssetId, intoEmpty.Document.Layers[5].Image!.AssetId);
    }

    [Fact]
    public void EqualHashesWithDifferentResourcePathsOrKindsDoNotReuseTargetRecords()
    {
        var source = CreateDocument();
        var hash = new string('a', 64);
        source = source with { Assets = source.Assets.SetItem(0, source.Assets[0] with { Sha256 = hash }) };
        var wrongPath = source.Assets[0] with { Id = Guid.NewGuid(), RelativePath = "assets/missing.ttf" };
        var wrongKind = source.Assets[0] with { Id = Guid.NewGuid(), Kind = ProjectAssetKind.IMAGE };
        var target = new ProjectDocument { Assets = [wrongPath, wrongKind] };

        var result = ProjectEditingOperations.MergeProjects(target, [new(source, "Paths", "/source")]);

        var fontId = Assert.Single(result.Document.Subtitles).Style.FontAssetId;
        Assert.NotEqual(wrongPath.Id, fontId);
        Assert.NotEqual(wrongKind.Id, fontId);
        Assert.Equal("assets/font.ttf", result.Document.Assets.Single(asset => asset.Id == fontId).RelativePath);
        Assert.Equal(6, result.Document.Assets.Length);
        Assert.Same(wrongPath, result.Document.Assets[0]);
        Assert.Same(wrongKind, result.Document.Assets[1]);
    }

    [Fact]
    public void ResourceBudgetCountsDeduplicatedRecordsAndRejectsARealOverflowAtomically()
    {
        var source = CreateDocument();
        source = source with
        {
            Assets = source.Assets.Select((asset, index) => index < 4
                ? asset with { Sha256 = (index + 10).ToString("x64", System.Globalization.CultureInfo.InvariantCulture) } : asset).ToImmutableArray()
        };
        var targetAssets = source.Assets.Take(4).Select(asset => asset with { Id = Guid.NewGuid() })
            .Concat(Enumerable.Range(0, 9996).Select(index => new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, $"assets/unused{index}.ttf")))
            .ToImmutableArray();
        var target = new ProjectDocument { Assets = targetAssets };

        var result = ProjectEditingOperations.MergeProjects(target, [new(source, "Within budget", "/source")]);

        Assert.Equal(10000, result.Document.Assets.Length);
        var incompatibleAssets = source with
        {
            Assets = source.Assets.SetItem(0, source.Assets[0] with { RelativePath = "assets/different.ttf" })
        };
        var editor = new ProjectEditor(target);
        Assert.Throws<InvalidDataException>(() => editor.MergeProjects([new(incompatibleAssets, "Overflow", "/source")]));
        Assert.Same(target, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    private static ProjectDocument CreateDocument()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "assets/font.ttf");
        var inlineFont = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "assets/inline.ttf");
        var image = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "assets/image.png");
        var trackFont = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "assets/track.ttf");
        var unused = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "assets/unused.ttf");
        var media = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "media/video.mkv");
        var track = ProjectTrack.Default with
        {
            Name = "Subtitles",
            DefaultStyle = new() { FontAssetId = trackFont.Id },
            StylePresetId = Guid.NewGuid(), StylePresetName = "Track style", AutoApplyStyle = false
        };
        var line = new SubtitleLine
        {
            Start = new(1001, 30), End = new(1061, 30), Text = "AB", StyleName = "Source style",
            Style = new() { FontAssetId = font.Id, Fill = new(2, 1, 1) },
            InlineSpans = [new(0, 1, new() { FontAssetId = inlineFont.Id })],
            Karaoke = [new(0, 1, new(1, 3), new(4, 3), SceneColor.White)],
            InactiveKaraoke = [new(1, 1, new(4, 3), new(7, 3), SceneColor.White)],
            KaraokeStyle = new() { PresetId = Guid.NewGuid(), PresetName = "Highlight" }
        };
        var node = new MaskNode { Position = new(5, 6), InHandle = new(-1, 2), OutHandle = new(3, 4) };
        var subtitle = new ProjectLayer
        {
            Id = line.Id, SubtitleId = line.Id, Kind = LayerKind.SUBTITLE,
            Start = line.Start, End = line.End, AnimationOffset = new(1, 3),
            Transform = new(10, 20, 2, 3, 15), Opacity = 0.75, Blend = BlendMode.MULTIPLY,
            Mask = new VectorClipMask
            {
                Inverted = true, Contours = [new() { Nodes = [node, new() { Position = new(15, 16) }] }],
                Placement = new() { Pivot = new(0.5, 0.5), Offset = new(10, 11) },
                ReferenceFrame = new() { Origin = new(5, 6), Size = new(10, 10) },
                Transform = new() { Position = new(2, 3) }
            },
            Tracks =
            [
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id), [new(new(1, 3), new ScenePoint(20, 30))]),
                new(AnimationProperty.OPACITY, [])
                {
                    InitialValue = 1,
                    Transforms = [new(Guid.NewGuid(), new(1, 3), new(7, 3), 0.25, 1.5)]
                }
            ],
            MotionPath = new(new(new(0, 0), [new(new(1, 2), new(3, 4), new(5, 6))]), new(2))
        };
        var shapeTrack = new ProjectTrack { Name = "Shape" };
        var imageTrack = new ProjectTrack { Name = "Image" };
        var shape = new ProjectLayer { TrackId = shapeTrack.Id, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.ELLIPSE, 30, 40) };
        var imageLayer = new ProjectLayer { TrackId = imageTrack.Id, Kind = LayerKind.IMAGE, Start = new(4), End = new(6), Image = new(image.Id, 50, 60) };
        var preset = new EffectPreset(Guid.NewGuid(), "Effect", [subtitle.Tracks[1]], subtitle.MotionPath, BlendMode.MULTIPLY);
        return new()
        {
            Assets = [font, inlineFont, image, trackFont, unused, media], Tracks = [track, shapeTrack, imageTrack],
            Subtitles = [line], Layers = [shape, subtitle, imageLayer], Presets = [preset],
            Media = new(media.Id, 0, 1, new(100)) { PlaybackOrigin = new(120) }
        };
    }

    private static ImmutableArray<ProjectLayer> Flatten(ImmutableArray<ProjectLayer> layers)
    {
        return layers;
    }
}
