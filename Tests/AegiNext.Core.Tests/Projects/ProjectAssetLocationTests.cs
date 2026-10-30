using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class ProjectAssetLocationTests
{
    [Theory]
    [InlineData("./media/../media/字幕 01.mkv", "media/字幕 01.mkv")]
    [InlineData(@".\media\..\media\字幕 01.mkv", "media/字幕 01.mkv")]
    [InlineData("../outside.mkv", "../outside.mkv")]
    [InlineData(@"..\outside.mkv", "../outside.mkv")]
    public void InputPathsResolveAgainstProjectDirectory(string input, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-path-tests", "project");

        Assert.Equal(Path.GetFullPath(Path.Combine(directory, expected)),
            ProjectAssetLocation.ResolveInputPath(input, directory));
    }

    [Fact]
    public void StoredRelativeMediaUsesTheCurrentProjectDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-path-tests", "moved project");
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "media/字幕 01.mkv");

        Assert.Equal(Path.Combine(directory, "media", "字幕 01.mkv"),
            ProjectAssetLocation.Resolve(asset, directory));
    }

    [Theory]
    [InlineData("project/media/video.mkv", true, "media/video.mkv")]
    [InlineData("project-other/video.mkv", false, "")]
    [InlineData("project", false, "")]
    public void RelativeReferencesRespectDirectoryBoundariesAndCanonicalFormat(string source, bool expected,
        string expectedRelative)
    {
        var root = Path.Combine(Path.GetTempPath(), "AegiNext-path-tests");

        Assert.Equal(expected, ProjectAssetLocation.TryGetRelativePath(Path.Combine(root, source),
            Path.Combine(root, "project"), out var relative));
        Assert.Equal(expectedRelative, relative);
    }

    [Fact]
    public void ForeignAbsoluteInputIsRejectedBeforeItCanBeInterpretedAsRelative()
    {
        var input = OperatingSystem.IsWindows() ? "/Volumes/Media/video.mkv" : "C:\\Media\\video.mkv";

        Assert.Throws<NotSupportedException>(() => ProjectAssetLocation.ResolveInputPath(input, Path.GetTempPath()));
    }
}
