using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class SubtitleColorTagEditingTests
{
    [Fact]
    public void PersonalTemplateImportsIndependentSnapshotAndBatchAssignmentUsesOneTransaction()
    {
        var editor = CreateEditor();
        var before = editor.Snapshot;
        var ids = before.Subtitles.Select(line => line.Id).ToArray();
        var template = Tag("Reviewed", "#aaBBcc");
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ApplySubtitleColorTag(ids, template);

        var after = editor.Snapshot;
        var tag = Assert.Single(after.ColorTags);
        Assert.NotEqual(template.Id, tag.Id);
        Assert.Equal("#AABBCC", tag.ColorHex);
        Assert.All(after.Subtitles, line => Assert.Equal(tag.Id, line.ColorTagId));
        Assert.Equal(before.Layers, after.Layers);
        Assert.Same(before.Layers[0], after.Layers[0]);
        Assert.Equal(1, changes);
        editor.ApplySubtitleColorTag(ids, template with { Id = Guid.NewGuid(), ColorHex = "#AABBCC" });
        Assert.Same(after, editor.Snapshot);
        Assert.Equal(1, changes);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);
        editor.SetSubtitleColorTag(ids, null);
        Assert.All(editor.Snapshot.Subtitles, line => Assert.Null(line.ColorTagId));
        Assert.Single(editor.Snapshot.ColorTags);
    }

    [Fact]
    public void UnknownTargetsAndUnknownTagRejectAtomicAssignmentAndPreserveHistory()
    {
        var editor = CreateEditor();
        var before = editor.Snapshot;
        var id = before.Subtitles[0].Id;
        Assert.Throws<KeyNotFoundException>(() => editor.ApplySubtitleColorTag([id, Guid.NewGuid()], Tag("Tag", "#112233")));
        Assert.Throws<KeyNotFoundException>(() => editor.SetSubtitleColorTag([id], Guid.NewGuid()));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        editor.ApplySubtitleColorTag([], Tag("Tag", "#112233"));
        Assert.Same(before, editor.Snapshot);
    }

    [Fact]
    public void SameNameWithDifferentColorAndSameColorWithDifferentNameRemainSeparateSnapshots()
    {
        var editor = CreateEditor();
        var id = editor.Snapshot.Subtitles[0].Id;
        var template = Tag("Review", "#112233");
        editor.ApplySubtitleColorTag([id], template);
        editor.ApplySubtitleColorTag([id], template with { ColorHex = "#223344" });
        editor.ApplySubtitleColorTag([id], template with { Name = "Approved" });
        Assert.Equal(3, editor.Snapshot.ColorTags.Length);
        Assert.Equal("Approved", editor.Snapshot.ColorTags.Single(tag => tag.Id == editor.Snapshot.Subtitles[0].ColorTagId).Name);
    }

    [Fact]
    public void EmptyMetadataIsOmittedAndRoundTripUsesCurrentProjectVersion()
    {
        var editor = CreateEditor();
        var before = editor.Snapshot;
        var untagged = JsonNode.Parse(ProjectStore.Serialize(before))!;
        Assert.Null(untagged["colorTags"]);
        Assert.False(untagged["subtitles"]![0]!.AsObject().ContainsKey("colorTagId"));
        var id = editor.Snapshot.Subtitles[0].Id;
        editor.ApplySubtitleColorTag([id], Tag("Review", "#112233"));
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot));
        Assert.Equal(ProjectDocument.CURRENT_VERSION, restored.Version);
        Assert.Equal<SubtitleColorTag>(editor.Snapshot.ColorTags, restored.ColorTags);
        Assert.Equal(editor.Snapshot.Subtitles[0].ColorTagId, restored.Subtitles[0].ColorTagId);
        Assert.NotEqual(ProjectStore.ComputeFingerprint(before), ProjectStore.ComputeFingerprint(restored));
    }

    [Fact]
    public void ClipboardRestoresDefinitionsAfterFirstAssignmentIsUndoneAndFailedPasteAddsNothing()
    {
        var editor = CreateEditor();
        var original = editor.Snapshot;
        var id = original.Subtitles[0].Id;
        editor.ApplySubtitleColorTag([id], Tag("Review", "#112233"));
        var content = ProjectEditingOperations.CaptureClips(editor.Snapshot, [id], id);
        Assert.Single(content.ColorTags);
        Assert.True(editor.Undo());
        Assert.Throws<InvalidDataException>(() => editor.PasteClips(content, new(0)));
        Assert.Same(original, editor.Snapshot);
        var result = editor.PasteClips(content, new(10));
        var pasted = result.Document.Subtitles.Single(line => line.Id == result.PrimaryId);
        Assert.Equal(Assert.Single(result.Document.ColorTags).Id, pasted.ColorTagId);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
    }

    [Fact]
    public void SplittingAndMergingRetainTheFirstSubtitleTag()
    {
        var editor = CreateEditor();
        var first = editor.Snapshot.Subtitles[0].Id;
        var second = editor.Snapshot.Subtitles[1].Id;
        editor.ApplySubtitleColorTag([first], Tag("First", "#112233"));
        editor.ApplySubtitleColorTag([second], Tag("Second", "#223344"));
        var colorId = editor.Snapshot.Subtitles[0].ColorTagId;
        var split = ProjectEditingOperations.SplitSubtitle(editor.Snapshot, first, new(1), 1);
        Assert.All(split.Subtitles.Take(2), line => Assert.Equal(colorId, line.ColorTagId));
        var merged = ProjectEditingOperations.MergeSubtitles(editor.Snapshot, [second, first]);
        Assert.Equal(colorId, Assert.Single(merged.Subtitles).ColorTagId);
    }

    [Fact]
    public void ProjectMergeReusesCompleteDefinitionsAndRemapsConflictingIdentities()
    {
        var editor = CreateEditor();
        var firstId = editor.Snapshot.Subtitles[0].Id;
        editor.ApplySubtitleColorTag([firstId], Tag("Review", "#112233"));
        var target = editor.Snapshot;
        var original = target.ColorTags[0];
        var source = target with
        {
            ColorTags = [original with { Name = "Other" }, original with { Id = Guid.NewGuid(), ColorHex = "#112233" }, Tag("Unused", "#AABBCC")],
            Subtitles = target.Subtitles.SetItem(0, target.Subtitles[0] with { ColorTagId = original.Id })
        };
        var result = ProjectEditingOperations.MergeProjects(target, [new(source, "Source", "/source")]);
        Assert.Equal(3, result.Document.ColorTags.Length);
        var imported = result.Document.Subtitles.Single(line => line.Id == result.ImportedSubtitleIds[0]);
        Assert.NotEqual(original.Id, imported.ColorTagId);
        Assert.Equal("Other", result.Document.ColorTags.Single(tag => tag.Id == imported.ColorTagId).Name);
        Assert.Equal(original, result.Document.ColorTags[0]);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void ProjectsWithoutColorMetadataRemainReadableAtEverySupportedVersion(int version)
    {
        var editor = CreateEditor();
        var node = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        node["version"] = version;
        LegacySubtitleMarginsJsonFixture.DowngradeProject(node);
        var restored = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(node.ToJsonString()));
        Assert.Empty(restored.ColorTags);
        Assert.All(restored.Subtitles, line => Assert.Null(line.ColorTagId));
        Assert.Equal(editor.Snapshot.Subtitles[0].Start, restored.Subtitles[0].Start);
        Assert.Equal(editor.Snapshot.Subtitles[0].End, restored.Subtitles[0].End);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("colorHex")]
    public void StoredTagDefinitionsStillRequireEveryField(string missingField)
    {
        var editor = CreateEditor();
        editor.ApplySubtitleColorTag([editor.Snapshot.Subtitles[0].Id], Tag("Review", "#112233"));
        var node = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        node["colorTags"]![0]!.AsObject().Remove(missingField);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Fact]
    public void StoredNullCollectionAndDanglingIdentityAreRejected()
    {
        var editor = CreateEditor();
        var node = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        node["colorTags"] = null;
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(node.ToJsonString())));
        node.AsObject().Remove("colorTags");
        node["subtitles"]![0]!["colorTagId"] = Guid.NewGuid().ToString();
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Fact]
    public void FrozenClipboardRetainsOriginalMeaningWhenItsProjectDefinitionChanges()
    {
        var editor = CreateEditor();
        var id = editor.Snapshot.Subtitles[0].Id;
        editor.ApplySubtitleColorTag([id], Tag("Review", "#112233"));
        var copiedTag = editor.Snapshot.ColorTags[0];
        var content = ProjectEditingOperations.CaptureClips(editor.Snapshot, [id], id);
        editor.Apply("Change project definition", document => document with
        {
            ColorTags = [copiedTag with { Name = "Approved", ColorHex = "#223344" }]
        });
        var result = editor.PasteClips(content, new(10));
        var copiedLine = result.Document.Subtitles.Single(line => line.Id == result.PrimaryId);
        var restored = result.Document.ColorTags.Single(tag => tag.Id == copiedLine.ColorTagId);
        Assert.NotEqual(copiedTag.Id, restored.Id);
        Assert.Equal(copiedTag.Name, restored.Name);
        Assert.Equal(copiedTag.ColorHex, restored.ColorHex);
        editor.PasteClips(content, new(20));
        Assert.Equal(2, editor.Snapshot.ColorTags.Length);
    }

    [Fact]
    public void ColorTagsLeaveAssSrtTextExportsAndSubtitleFillUnchanged()
    {
        var editor = CreateEditor();
        var id = editor.Snapshot.Subtitles[0].Id;
        editor.UpdateSubtitle(id, line => line with
        {
            Style = line.Style with { Fill = new(1, 0, 0) },
            InlineSpans = [new(1, 1, new() { Fill = new(0, 1, 0) })]
        });
        var before = editor.Snapshot;
        var expectedAss = AssSubtitleFormat.Write(before);
        var expectedSrt = SubtitleTextFormat.WriteSrt(before.Subtitles);
        var expectedText = SubtitleTextFormat.WriteText(before.Subtitles);

        editor.ApplySubtitleColorTag(before.Subtitles.Select(line => line.Id).ToArray(), Tag("Review", "#0000FF"));

        var after = editor.Snapshot;
        var actualAss = AssSubtitleFormat.Write(after);
        Assert.Equal(expectedAss.Text, actualAss.Text);
        Assert.Equal<SubtitleFormatDiagnostic>(expectedAss.Diagnostics, actualAss.Diagnostics);
        Assert.Equal(expectedSrt, SubtitleTextFormat.WriteSrt(after.Subtitles));
        Assert.Equal(expectedText, SubtitleTextFormat.WriteText(after.Subtitles));
        Assert.Same(before.Subtitles[0].Style, after.Subtitles[0].Style);
        Assert.Equal(new SceneColor(1, 0, 0), after.Subtitles[0].Style.Fill);
        Assert.Equal(new SceneColor(0, 1, 0), after.Subtitles[0].InlineSpans[0].Style.Fill);
        Assert.Equal(before.Layers, after.Layers);
    }

    private static ProjectEditor CreateEditor()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(0), new(2), "AB");
        editor.AddSubtitle(new(3), new(5), "CD");
        return new(editor.Snapshot);
    }

    private static SubtitleColorTag Tag(string name, string color) => new() { Name = name, ColorHex = color };
}
