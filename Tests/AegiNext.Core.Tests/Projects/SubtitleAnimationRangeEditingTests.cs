using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitleAnimationRangeEditingTests
{
    [Fact]
    public void TextRemappingPreservesRangeOrderIdentityAndNativeGeometry()
    {
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2) { Rotation = 15 };
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2) { Scale = new(2, 3) };
        var map = new SubtitleTextEditMap("abcd", 1, 1, "😀");

        var ranges = SubtitleAnimationRangeEditing.Remap([first, second], map);

        Assert.Equal(new[] { first with { Utf16Length = 3 }, second with { Utf16Length = 3 } }, ranges.ToArray());
    }

    [Fact]
    public void RemovingAParentRangePrunesAllGeneratedDescendants()
    {
        var parent = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var child = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1)
        {
            GeneratedOrigin = new("effect", "child", parent.Id, "grapheme")
        };
        var grandchild = new SubtitleAnimationRange(Guid.NewGuid(), 2, 1)
        {
            GeneratedOrigin = new("effect", "grandchild", child.Id, "grapheme")
        };
        var independent = new SubtitleAnimationRange(Guid.NewGuid(), 2, 1);
        var map = new SubtitleTextEditMap("abc", 0, 1, "");

        var ranges = SubtitleAnimationRangeEditing.Remap([grandchild, child, parent, independent], map);

        Assert.Equal(independent with { Utf16Start = 1 }, Assert.Single(ranges));
    }

    [Fact]
    public void SplittingGeneratedRangesRemapsTheirParentIdentity()
    {
        var parent = new SubtitleAnimationRange(Guid.NewGuid(), 0, 4);
        var child = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2)
        {
            GeneratedOrigin = new("effect", "child", parent.Id, "grapheme")
        };
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var identities = new Dictionary<Guid, Guid> { [parent.Id] = parentId, [child.Id] = childId };

        var ranges = SubtitleAnimationRangeEditing.Split([parent, child], 2, true, identities);

        Assert.Equal(parent with { Id = parentId, Utf16Start = 0, Utf16Length = 2 }, ranges[0]);
        Assert.Equal(child with
        {
            Id = childId, Utf16Start = 0, Utf16Length = 1,
            GeneratedOrigin = child.GeneratedOrigin with { ParentRangeId = parentId }
        }, ranges[1]);
    }
}
