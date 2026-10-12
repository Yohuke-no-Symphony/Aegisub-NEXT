using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Export.Tests;

public sealed class SubtitleMuxRequestTests
{
    [Theory]
    [InlineData(".mkv", "subrip")]
    [InlineData(".mp4", "mov_text")]
    [InlineData(".MKV", "subrip")]
    public void ContainersSelectTheirNativeTextSubtitleCodec(string extension, string expectedCodec)
    {
        Assert.Equal(expectedCodec, SubtitleMuxCapabilities.GetSubtitleCodec(extension));
    }

    [Theory]
    [InlineData(".mkv")]
    [InlineData(".mp4")]
    [InlineData(".MKV")]
    public void ValidRequestsDoNotRequireEvenCanvasDimensionsOrKnownPlaybackOrigin(string extension)
    {
        using var directory = new ExportTestDirectory();
        var request = CreateRequest(directory.Path, extension);

        SubtitleMuxRequestValidator.Validate(request);

        Assert.Equal(65, request.Project.Width);
        Assert.Equal(49, request.Project.Height);
        Assert.Null(request.Project.Media!.PlaybackOrigin);
    }

    [Theory]
    [InlineData(".webm")]
    [InlineData(".srt")]
    [InlineData(".avi")]
    public void UnsupportedContainersAreRejected(string extension)
    {
        using var directory = new ExportTestDirectory();

        Assert.Throws<ArgumentException>(() => SubtitleMuxRequestValidator.Validate(CreateRequest(directory.Path, extension)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RelativeProjectOrOutputPathsAreRejected(bool relativeProject)
    {
        using var directory = new ExportTestDirectory();
        var request = CreateRequest(directory.Path, ".mkv");
        request = relativeProject
            ? request with { ProjectDirectory = "relative-project" }
            : request with { OutputPath = "relative-output.mkv" };

        Assert.Throws<ArgumentException>(() => SubtitleMuxRequestValidator.Validate(request));
    }

    [Fact]
    public void MissingMediaBindingIsRejected()
    {
        using var directory = new ExportTestDirectory();
        var request = CreateRequest(directory.Path, ".mkv");
        request = request with { Project = request.Project with { Media = null } };

        Assert.Throws<ArgumentException>(() => SubtitleMuxRequestValidator.Validate(request));
    }

    [Fact]
    public void MissingProjectSubtitlesIsRejected()
    {
        using var directory = new ExportTestDirectory();
        var request = CreateRequest(directory.Path, ".mkv");
        request = request with { Project = request.Project with { Subtitles = [], Layers = [] } };

        Assert.Throws<ArgumentException>(() => SubtitleMuxRequestValidator.Validate(request));
    }

    private static SubtitleMuxRequest CreateRequest(string directory, string extension)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
            ExternalPath: Path.Combine(directory, "source.mkv"));
        var subtitle = new SubtitleLine { Text = "内挂字幕" };
        var project = new ProjectDocument
        {
            Width = 65,
            Height = 49,
            Assets = [asset],
            Media = new(asset.Id, 0, null, MediaTime.Zero),
            Subtitles = [subtitle],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, Start = subtitle.Start, End = subtitle.End }]
        };
        return new(project, directory, Path.Combine(directory, "output" + extension));
    }
}
