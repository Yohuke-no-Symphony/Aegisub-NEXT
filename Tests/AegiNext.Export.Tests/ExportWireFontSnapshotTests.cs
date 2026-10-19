using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Export.Worker;

namespace AegiNext.Export.Tests;

public sealed class ExportWireFontSnapshotTests
{
    [Fact]
    public void BurnInProtocolCarriesFrozenFontReferencesWithoutRawBytes()
    {
        var job = Job() with
        {
            FontFamilies = [new("Private Family", [new("regular.ttf", new('a', 64)), new("bold.ttf", new('b', 64))])]
        };

        var json = JsonSerializer.Serialize(job, ExportWire.Options);
        var restored = JsonSerializer.Deserialize<ExportWorkerJob>(json, ExportWire.Options)!;
        ExportWire.ValidateJob(restored);

        var family = Assert.Single(restored.FontFamilies);
        Assert.Equal("Private Family", family.FamilyName);
        Assert.Equal(["regular.ttf", "bold.ttf"], family.Files.Select(file => file.FileName));
        Assert.Equal(new('b', 64), family.Files[1].Sha256);
        Assert.DoesNotContain("\"data\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SubtitleMuxDoesNotAcceptFontFiles()
    {
        var job = Job() with
        {
            Operation = ExportWorkerOperation.SUBTITLE_MUX,
            Video = null,
            FontFamilies = [new("Private Family", [new("regular.ttf", new('a', 64))])]
        };

        Assert.Throws<InvalidDataException>(() => ExportWire.ValidateJob(job));
    }

    [Fact]
    public void UninitializedFontIndexIsRejectedAsAProtocolError()
    {
        Assert.Throws<InvalidDataException>(() => ExportWire.ValidateJob(Job() with { FontFamilies = default }));
    }

    private static ExportWorkerJob Job()
    {
        return new(JsonSerializer.SerializeToElement(new ProjectDocument(), ExportWire.Options), Path.GetTempPath(),
            Path.GetTempPath(), ".mkv", "ffmpeg", ExportWorkerOperation.BURN_IN,
            new(VideoCodec.H264, "medium", 20, AudioExportMode.None, 192000, RateControlMode: VideoRateControlMode.CRF), ExportWire.VERSION);
    }
}
