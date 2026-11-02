using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class AudioOnlyProjectPersistenceTests
{
    [Fact]
    public void AudioOnlyBindingRoundTripsWithNullVideoStream()
    {
        var original = Document(null, 0);

        var json = JsonNode.Parse(ProjectStore.Serialize(original))!.AsObject();
        Assert.Null(json["media"]!["videoStreamIndex"]);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(original));

        Assert.Equal(ProjectDocument.CURRENT_VERSION, restored.Version);
        Assert.Equal(original.Media, restored.Media);
        Assert.Null(restored.Media!.VideoStreamIndex);
        Assert.Equal(0, restored.Media.AudioStreamIndex);
    }

    [Fact]
    public void VersionNineteenVideoProjectUpgradesWithoutChangingTheBinding()
    {
        var original = Document(0, 1);
        var json = JsonNode.Parse(ProjectStore.Serialize(original))!.AsObject();
        json["version"] = 19;

        var restored = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Equal(ProjectDocument.CURRENT_VERSION, restored.Version);
        Assert.Equal(original.Media, restored.Media);
    }

    [Fact]
    public void BindingWithoutAnyStreamIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(null, null)));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(-1, 0)));
    }

    private static ProjectDocument Document(int? video, int? audio)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: "/media/song.flac");
        return new() { Assets = [asset], Media = new(asset.Id, video, audio, new(0)) { PlaybackOrigin = new(0) } };
    }
}
