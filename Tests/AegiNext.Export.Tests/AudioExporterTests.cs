using AegiNext.Media.Probing;
using AegiNext.Media.Processes;

namespace AegiNext.Export.Tests;

public sealed class AudioExporterTests
{
    [ExportTheory]
    [InlineData(AudioFileFormat.Copy, false)]
    [InlineData(AudioFileFormat.Wav, false)]
    [InlineData(AudioFileFormat.Flac, false)]
    [InlineData(AudioFileFormat.Mp3, false)]
    [InlineData(AudioFileFormat.Aac, false)]
    [InlineData(AudioFileFormat.Opus, false)]
    [InlineData(AudioFileFormat.Copy, true)]
    [InlineData(AudioFileFormat.Wav, true)]
    [InlineData(AudioFileFormat.Flac, true)]
    [InlineData(AudioFileFormat.Mp3, true)]
    [InlineData(AudioFileFormat.Aac, true)]
    [InlineData(AudioFileFormat.Opus, true)]
    public async Task SelectedCompleteAudioTrackExportsWithoutVideoOrSubtitles(AudioFileFormat format, bool video)
    {
        using var fixture = await AudioExportFixture.CreateAsync(video);
        var request = fixture.Request(format);
        var progress = new AudioExportProgressCapture();
        var committed = false;

        var result = await new AudioExporter().ExportAsync(request, progress, () =>
        {
            Assert.False(File.Exists(request.OutputPath));
            committed = true;
        }, CancellationToken.None);

        Assert.True(committed);
        var source = fixture.Source.Asset.Streams.Last(stream => stream.CodecType == "audio");
        Assert.Equal(source.Index, result.SourceStreamIndex);
        var output = await new FfprobeMediaProbe(new(fixture.Ffprobe)).ProbeAsync(result.OutputPath);
        var stream = Assert.Single(output.Asset.Streams);
        Assert.Equal("audio", stream.CodecType);
        Assert.Equal(result.Codec, stream.CodecName);
        Assert.Equal(48000, result.SampleRate);
        Assert.Equal(1, result.Channels);
        var duration = stream.Timing.ReportedDuration ?? output.Asset.ReportedDuration;
        Assert.NotNull(duration);
        Assert.InRange((double)duration.Value.Numerator / duration.Value.Denominator, 1.24, 1.35);
        if (format is AudioFileFormat.Copy or AudioFileFormat.Wav or AudioFileFormat.Flac)
        {
            Assert.Equal(await fixture.DecodedHashAsync(fixture.SourcePath, source.Index),
                await fixture.DecodedHashAsync(result.OutputPath, stream.Index));
        }

        Assert.Contains(progress.Values, value => value.Stage == "preflight");
        Assert.Contains(progress.Values, value => value.Stage == "encoding");
        Assert.Contains(progress.Values, value => value.Stage == "verifying");
        Assert.Equal("complete", progress.Values[^1].Stage);
        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(true)]
    public async Task MissingSelectionUsesFirstAudioTrack(bool video)
    {
        using var fixture = await AudioExportFixture.CreateAsync(video);
        var result = await new AudioExporter().ExportAsync(fixture.Request(AudioFileFormat.Flac, false));
        var source = fixture.Source.Asset.Streams.First(stream => stream.CodecType == "audio");
        Assert.Equal(source.Index, result.SourceStreamIndex);
        Assert.Equal(await fixture.DecodedHashAsync(fixture.SourcePath, source.Index),
            await fixture.DecodedHashAsync(result.OutputPath, 0));
    }

    [ExportTheory]
    [InlineData("preflight")]
    [InlineData("encoding")]
    [InlineData("verifying")]
    public async Task CancellationNeverPublishesOrLeavesTemporaryFiles(string stage)
    {
        using var fixture = await AudioExportFixture.CreateAsync(false);
        var request = fixture.Request(AudioFileFormat.Flac);
        using var cancellation = new CancellationTokenSource();
        var requestCancellation = new Action(cancellation.Cancel);
        var progress = new AudioExportProgressCapture(value =>
        {
            if (value.Stage == stage)
            {
                requestCancellation();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AudioExporter().ExportAsync(request, progress, cancellation.Token));

        Assert.Contains(progress.Values, value => value.Stage == stage);
        Assert.False(File.Exists(request.OutputPath));
        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingOutputAndCommitRejectionDoNotPublishPartialFiles(bool existing)
    {
        using var fixture = await AudioExportFixture.CreateAsync(false);
        var request = fixture.Request(AudioFileFormat.Flac);
        if (existing)
        {
            await File.WriteAllTextAsync(request.OutputPath, "previous audio");
            await Assert.ThrowsAsync<IOException>(() => new AudioExporter().ExportAsync(request));
            Assert.Equal("previous audio", await File.ReadAllTextAsync(request.OutputPath));
        }
        else
        {
            var called = false;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AudioExporter().ExportAsync(request, null, () =>
            {
                called = true;
                throw new OperationCanceledException("Commit declined");
            }, CancellationToken.None));
            Assert.True(called);
            Assert.False(File.Exists(request.OutputPath));
        }

        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrNonAudioSelectionFailsWithoutPublishing(bool selectVideo)
    {
        using var fixture = await AudioExportFixture.CreateAsync(true);
        var request = fixture.Request(AudioFileFormat.Flac);
        request = request with
        {
            Project = request.Project with
            {
                Media = request.Project.Media! with { AudioStreamIndex = selectVideo ? 0 : 99 }
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new AudioExporter().ExportAsync(request));

        Assert.False(File.Exists(request.OutputPath));
        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(AudioFileFormat.Copy)]
    [InlineData(AudioFileFormat.Flac)]
    public async Task VideoWithoutAnyAudioCannotCreateAnEmptyAudioFile(AudioFileFormat format)
    {
        using var fixture = await SubtitleMuxFixture.CreateVideoOnlyAsync(".mkv", null);
        var mux = fixture.Request(".mkv");
        var request = new AudioExportRequest(mux.Project, mux.ProjectDirectory,
            Path.Combine(fixture.DirectoryPath, "audio" + AudioExportCapabilities.GetExtension(format)))
        {
            Format = format, FfmpegPath = fixture.Ffmpeg, WorkerPath = mux.WorkerPath
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new AudioExporter().ExportAsync(request));

        Assert.False(File.Exists(request.OutputPath));
        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(AudioFileFormat.Flac, false)]
    [InlineData(AudioFileFormat.Mp3, true)]
    public async Task MultichannelAudioIsPreservedOrExplicitlyRejected(AudioFileFormat format, bool rejected)
    {
        using var fixture = await AudioExportFixture.CreateAsync(false, true);
        var request = fixture.Request(format);
        if (rejected)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new AudioExporter().ExportAsync(request));
            Assert.Contains("声道", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(request.OutputPath));
        }
        else
        {
            var result = await new AudioExporter().ExportAsync(request);
            Assert.Equal(6, result.Channels);
        }

        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(AudioFileFormat.Flac)]
    [InlineData(AudioFileFormat.Aac)]
    public async Task RecoverableSourceDecodingErrorsCannotPublishTruncatedAudio(AudioFileFormat format)
    {
        using var fixture = await AudioExportFixture.CreateAsync(false);
        var damagedPath = Path.Combine(fixture.DirectoryPath, "damaged.aac");
        var encoded = await MediaToolProcess.RunAsync(fixture.Ffmpeg,
            ["-nostdin", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2",
                "-c:a", "aac", "-f", "adts", "-n", damagedPath],
            TimeSpan.FromSeconds(30), 4096, 65536, CancellationToken.None);
        Assert.True(encoded.ExitCode == 0, encoded.StandardError);
        var bytes = await File.ReadAllBytesAsync(damagedPath);
        Array.Fill<byte>(bytes, 0, bytes.Length / 2, 50);
        await File.WriteAllBytesAsync(damagedPath, bytes);
        var request = fixture.Request(format);
        var asset = request.Project.Assets[0] with { ExternalPath = damagedPath };
        request = request with
        {
            Project = request.Project with
            {
                Assets = [asset],
                Media = request.Project.Media! with { AudioStreamIndex = 0 }
            }
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new AudioExporter().ExportAsync(request));

        Assert.Contains("未提交输出", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(request.OutputPath));
        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(".mp4")]
    public async Task CopyPreservesDecodedSamplesAndTrimmingOrRejectsBeforeCommit(string sourceExtension)
    {
        using var fixture = await SubtitleMuxFixture.CreateAsync(sourceExtension, nonzeroOrigin: false);
        var mux = fixture.Request(".mkv");
        var request = new AudioExportRequest(mux.Project, mux.ProjectDirectory, Path.Combine(fixture.DirectoryPath, "audio.mka"))
        {
            FfmpegPath = fixture.Ffmpeg, WorkerPath = mux.WorkerPath
        };
        try
        {
            var result = await new AudioExporter().ExportAsync(request);
            Assert.Equal(await DecodedHashAsync(fixture.Ffmpeg, fixture.SourcePath, result.SourceStreamIndex),
                await DecodedHashAsync(fixture.Ffmpeg, result.OutputPath, 0));
        }
        catch (InvalidOperationException error)
        {
            Assert.Contains("首尾采样裁剪", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(request.OutputPath));
        }

        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    private static async Task<string> DecodedHashAsync(string ffmpeg, string path, int index)
    {
        var output = await MediaToolProcess.RunAsync(ffmpeg,
            ["-nostdin", "-v", "error", "-i", path, "-map", $"0:{index}", "-c:a", "pcm_s32le",
                "-f", "hash", "-hash", "sha256", "pipe:1"],
            TimeSpan.FromSeconds(30), 4096, 65536, CancellationToken.None);
        Assert.True(output.ExitCode == 0, output.StandardError);
        return output.StandardOutput.Trim();
    }
}
