using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class TimelineEffectRowLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyAndProjectImportRemapEveryRangeNodeAndInstanceIdentity(bool import)
    {
        var document = OrderedDocument();
        var original = document.Layers[0];
        ProjectDocument result;
        ProjectLayer copy;
        if (import)
        {
            var merged = ProjectEditingOperations.MergeProjects(new(), [new(document, "Imported", Path.GetTempPath())]);
            result = merged.Document;
            copy = result.Layers.Single(layer => layer.Id == merged.ImportedLayerIds[0]);
        }
        else
        {
            var clipboard = ProjectEditingOperations.CaptureClips(document, [original.Id], original.Id);
            var pasted = ProjectEditingOperations.PasteClips(document, clipboard, new(5));
            result = pasted.Document;
            copy = result.Layers.Single(layer => layer.Id == pasted.PrimaryId);
        }
        var sourceLine = document.Subtitles[0];
        var line = result.Subtitles.Single(value => value.Id == copy.SubtitleId);
        var ranges = sourceLine.AnimationRanges.Zip(line.AnimationRanges).ToDictionary(pair => pair.First.Id, pair => pair.Second.Id);
        var nodes = ((VectorClipMask)original.Mask!).Contours.SelectMany(contour => contour.Nodes)
            .Zip(((VectorClipMask)copy.Mask!).Contours.SelectMany(contour => contour.Nodes))
            .ToDictionary(pair => pair.First.Id, pair => pair.Second.Id);
        var instances = original.Effects.Zip(copy.Effects).ToDictionary(pair => pair.First.Id, pair => pair.Second.Id);
        var expected = original.EffectRowOrder.Select(identity => identity.InstanceId is { } id
            ? new TimelineEffectRowIdentity(InstanceId: instances[id])
            : new(identity.Target!.Value with
            {
                TextRangeId = identity.Target.Value.TextRangeId is { } range ? ranges[range] : null,
                NodeId = identity.Target.Value.NodeId is { } node ? nodes[node] : null
            })).ToArray();

        Assert.Equal(expected, copy.EffectRowOrder);
        Assert.Equal(expected, TimelineEffectRowOrdering.GetOrder(copy, line));
        Assert.DoesNotContain(copy.EffectRowOrder, identity => identity.InstanceId is { } id && instances.ContainsKey(id));
        Assert.DoesNotContain(copy.EffectRowOrder, identity => identity.Target?.TextRangeId is { } id && ranges.ContainsKey(id));
        Assert.DoesNotContain(copy.EffectRowOrder, identity => identity.Target?.NodeId is { } id && nodes.ContainsKey(id));
    }

    [Fact]
    public void SubtitleSplitRetainsRelativeLeafOrderAndMapsBothNewInstanceSets()
    {
        var document = OrderedDocument();
        var layer = document.Layers[0];

        var result = ProjectEditingOperations.SplitSubtitle(document, document.Subtitles[0].Id, new(2), 2);

        foreach (var child in result.Layers)
        {
            Assert.All(child.EffectRowOrder.Where(identity => identity.InstanceId.HasValue), identity =>
                Assert.Contains(child.Effects, instance => instance.Id == identity.InstanceId));
            Assert.DoesNotContain(child.EffectRowOrder, identity => identity.InstanceId is { } id && layer.Effects.Any(instance => instance.Id == id));
            var line = result.Subtitles.Single(value => value.Id == child.SubtitleId);
            var rows = TimelineEffectRowOrdering.GetOrder(child, line);
            Assert.Equal(child.EffectRowOrder.Where(identity => rows.Contains(identity)), rows);
        }
        Assert.DoesNotContain(result.Layers[1].EffectRowOrder, identity => identity.Target?.TextRangeId.HasValue == true);
        Assert.Contains(result.Layers[0].EffectRowOrder, identity => identity.Target?.TextRangeId == document.Subtitles[0].AnimationRanges[0].Id);
    }

    [Fact]
    public void AddingAndExplicitlyMovingInstancesKeepsTheirDisplayedSubsequenceConsistentWithTheStack()
    {
        var document = OrderedDocument();
        var editor = new ProjectEditor(document);
        var id = document.Layers[0].Id;
        editor.AddEffectInstances(id, "New", document.Layers[0].Effects[0].Source);
        AssertInstanceSubsequence(editor.Snapshot);

        editor.MoveEffectInstance(id, editor.Snapshot.Layers[0].Effects[0].Id, 2);

        AssertInstanceSubsequence(editor.Snapshot);
    }

    private static void AssertInstanceSubsequence(ProjectDocument document)
    {
        var layer = document.Layers[0];
        Assert.Equal(layer.Effects.Reverse().Select(instance => instance.Id),
            TimelineEffectRowOrdering.GetOrder(layer, document.Subtitles[0]).Where(identity => identity.InstanceId.HasValue).Select(identity => identity.InstanceId!.Value));
    }

    private static ProjectDocument OrderedDocument()
    {
        var document = TimelineEffectRowEditingTests.Document();
        var layer = document.Layers[0];
        var rows = TimelineEffectRowOrdering.GetOrder(layer, document.Subtitles[0]);
        return document with { Layers = [layer with { EffectRowOrder = rows.Insert(1, rows[^1]).RemoveAt(rows.Length) }] };
    }
}
