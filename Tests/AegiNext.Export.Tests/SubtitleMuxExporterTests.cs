using AegiNext.Export.Muxing;
using AegiNext.Export.Worker;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Probing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Export.Tests;

public sealed class SubtitleMuxExporterTests
{
    [ExportTheory]
    [InlineData(".mkv", false)]
    [InlineData(".mp4", false)]
    [InlineData(".mkv", true)]
    [InlineData(".mp4", true)]
    public async Task AllOriginalVideoAudioAndSubtitlePacketsSurviveWithNonzeroOrigins(string extension, bool chapters)
    {
        using var fixture = await SubtitleMuxFixture.CreateAsync(extension, chapters);
        var request = fixture.Request(extension);
        var progress = new SubtitleMuxProgressCapture();
        var result = await new SubtitleMuxExporter().ExportAsync(request, progress);
        var output = await new FfprobeMediaProbe(new(fixture.Ffprobe)).ProbeAsync(result.OutputPath);
        Assert.Equal(2, result.VideoStreamCount);
        Assert.Equal(2, result.AudioStreamCount);
        Assert.Equal(1, result.PreservedSubtitleStreamCount);
        Assert.Equal(SubtitleMuxCapabilities.GetSubtitleCodec(extension), result.SubtitleCodec);
        Assert.Null(request.Project.Media!.PlaybackOrigin);
        Assert.True(request.Project.Media.MediaOrigin >= new MediaTime(3));
        var oldStreams = fixture.Source.Asset.Streams.Where(stream => stream.CodecType is "video" or "audio" or "subtitle").ToArray();
        var newStreams = output.Asset.Streams.Where(stream => stream.CodecType is "video" or "audio" or "subtitle").ToArray();
        Assert.Equal(oldStreams.Length + 1, newStreams.Length);
        for (var index = 0; index < oldStreams.Length; index++)
        {
            Assert.Equal(oldStreams[index].CodecName, newStreams[index].CodecName);
            Assert.Equal(await fixture.PacketEvidenceAsync(fixture.SourcePath, oldStreams[index].Index),
                await fixture.PacketEvidenceAsync(result.OutputPath, newStreams[index].Index));
        }

        var added = newStreams[^1];
        Assert.Equal(result.AddedSubtitleStreamIndex, added.Index);
        Assert.All(added.Disposition, pair => Assert.Equal(0, pair.Value));
        var extracted = await fixture.SubtitleTextAsync(result.OutputPath, added.Index);
        var expected = SubtitleTextFormat.WriteSrt(request.Project.Subtitles, request.Project.Media.MediaOrigin);
        SubtitleMuxVerification.ValidateSubtitles(expected, extracted);
        Assert.Equal("Original title", output.Asset.Tags.First(pair => pair.Key.Equals("title", StringComparison.OrdinalIgnoreCase)).Value);
        Assert.Contains(progress.Values, value => value.Stage == "preflight");
        Assert.Contains(progress.Values, value => value.Stage == "muxing");
        Assert.Contains(progress.Values, value => value.Stage == "verifying");
        Assert.Equal("complete", progress.Values[^1].Stage);
        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CommitRejectionAndExistingOutputsNeverPublishPartialResults(bool existing)
    {
        using var fixture = await SubtitleMuxFixture.CreateAsync(".mkv");
        var request = fixture.Request(".mkv");
        if (existing)
        {
            await File.WriteAllTextAsync(request.OutputPath, "previous output");
            await Assert.ThrowsAsync<IOException>(() => new SubtitleMuxExporter().ExportAsync(request));
            Assert.Equal("previous output", await File.ReadAllTextAsync(request.OutputPath));
        }
        else
        {
            var called = false;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SubtitleMuxExporter().ExportAsync(request, null, () =>
            {
                called = true;
                Assert.False(File.Exists(request.OutputPath));
                throw new OperationCanceledException("Commit rejected");
            }, CancellationToken.None));
            Assert.True(called);
            Assert.False(File.Exists(request.OutputPath));
        }

        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData("preflight")]
    [InlineData("muxing")]
    public async Task CancelledWorkerNeverPublishesOrLeavesTemporaryFiles(string stage)
    {
        using var fixture = await SubtitleMuxFixture.CreateAsync(".mkv");
        var request = fixture.Request(".mkv");
        using var cancellation = new CancellationTokenSource();
        var progress = new SubtitleMuxProgressCapture(value =>
        {
            if (value.Stage == stage)
            {
                cancellation.Cancel();
            }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SubtitleMuxExporter().ExportAsync(request, progress, cancellation.Token));
        Assert.Contains(progress.Values, value => value.Stage == stage);
        Assert.False(File.Exists(request.OutputPath));
        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(true)]
    public async Task ActiveMediaProcessCancellationDrainsAndStopsTheRunningTool(bool cancel)
    {
        using var fixture = await SubtitleMuxFixture.CreateAsync(".mkv");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var position = MediaTime.Zero;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SubtitleMuxProcess.RunAsync(fixture.Ffmpeg,
            ["-nostdin", "-v", "error", "-nostats", "-progress", "pipe:1", "-re", "-f", "lavfi", "-i",
                "sine=frequency=440:sample_rate=48000:duration=30", "-f", "null", "-"], value =>
            {
                position = value;
                if (cancel && value > MediaTime.Zero)
                {
                    cancellation.Cancel();
                }
            }, cancellation.Token));
        Assert.True(position > MediaTime.Zero);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [ExportTheory]
    [InlineData(".mp4", ".mkv", false, false)]
    [InlineData(".mkv", ".mp4", true, true)]
    public async Task CrossContainerAudioTrimIsPreservedOrRejectedBeforeCommit(string sourceExtension,
        string outputExtension, bool nonzeroOrigin, bool rejected)
    {
        using var fixture = await SubtitleMuxFixture.CreateAsync(sourceExtension, originalSubtitle: false, nonzeroOrigin: nonzeroOrigin);
        var request = fixture.Request(outputExtension);
        var audio = fixture.Source.Asset.Streams.First(stream => stream.CodecType == "audio");
        var before = await SubtitleMuxAudioPackets.ReadAsync(fixture.Ffprobe, fixture.SourcePath, audio.Index,
            audio.Timing.TimeBase, CancellationToken.None);
        Assert.True(before.SkipSamples > 0);
        if (rejected)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SubtitleMuxExporter().ExportAsync(request));
            Assert.Contains("首尾采样裁剪", error.Message, StringComparison.Ordinal);
            Assert.Contains("未提交", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(request.OutputPath));
        }
        else
        {
            Assert.True(before.DiscardPadding > 0);
            var result = await new SubtitleMuxExporter().ExportAsync(request);
            Assert.Equal(0, result.PreservedSubtitleStreamCount);
            var output = await new FfprobeMediaProbe(new(fixture.Ffprobe)).ProbeAsync(result.OutputPath);
            var copied = output.Asset.Streams.First(stream => stream.CodecType == "audio");
            var after = await SubtitleMuxAudioPackets.ReadAsync(fixture.Ffprobe, result.OutputPath, copied.Index,
                copied.Timing.TimeBase, CancellationToken.None);
            Assert.Equal(before.PacketCount, after.PacketCount);
            Assert.Equal(before.InitialPadding, after.InitialPadding);
            Assert.Equal(before.SkipSamples, after.SkipSamples);
            Assert.Equal(before.DiscardPadding, after.DiscardPadding);
            Assert.Equal(2, result.VideoStreamCount);
            Assert.Equal(2, result.AudioStreamCount);
        }

        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(".mkv", "tv")]
    [InlineData(".mkv", "pc")]
    [InlineData(".mp4", null)]
    public async Task Mp4RetainsOnlyTheVideoColorRangeAlreadyDeclaredByTheSource(string sourceExtension, string? range)
    {
        using var fixture = await SubtitleMuxFixture.CreateVideoOnlyAsync(sourceExtension, range);
        var original = fixture.Source.Asset.Streams[0].Video!;
        Assert.Equal(range, original.Color.Range);
        Assert.Null(original.Color.Primaries);
        Assert.Null(original.Color.Transfer);
        Assert.Null(original.Color.Matrix);
        var result = await new SubtitleMuxExporter().ExportAsync(fixture.Request(".mp4"));
        var output = await new FfprobeMediaProbe(new(fixture.Ffprobe)).ProbeAsync(result.OutputPath);
        Assert.Equal(original.Color, output.Asset.Streams[0].Video!.Color);
        var subtitle = output.Asset.Streams.Single(stream => stream.Index == result.AddedSubtitleStreamIndex);
        Assert.Equal(1, subtitle.Disposition["default"]);
        Assert.All(subtitle.Disposition.Where(pair => pair.Key != "default"), pair => Assert.Equal(0, pair.Value));
        Assert.Equal(1, result.VideoStreamCount);
        Assert.Equal(0, result.AudioStreamCount);
        Assert.Equal(0, result.PreservedSubtitleStreamCount);
    }

    [ExportTheory]
    [InlineData(".mkv")]
    [InlineData(".mp4")]
    public async Task OverlappingCuesArePreservedOrExplicitlyRejectedByTheirContainer(string extension)
    {
        using var fixture = await SubtitleMuxFixture.CreateAsync(extension);
        var request = fixture.Request(extension);
        var first = request.Project.Subtitles[0];
        var second = new SubtitleLine { Start = new(3, 10), End = new(7, 10), Text = "第二条重叠字幕" };
        var secondTrack = new ProjectTrack { Name = "Second subtitle track" };
        request = request with
        {
            Project = request.Project with
            {
                Subtitles = [first, second],
                Tracks = request.Project.Tracks.Add(secondTrack),
                Layers = request.Project.Layers.Add(new() { Kind = LayerKind.SUBTITLE, SubtitleId = second.Id,
                    TrackId = secondTrack.Id, Start = second.Start, End = second.End })
            }
        };
        if (extension == ".mp4")
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SubtitleMuxExporter().ExportAsync(request));
            Assert.Contains("字幕", error.Message, StringComparison.Ordinal);
            Assert.Contains("未提交", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(request.OutputPath));
        }
        else
        {
            var result = await new SubtitleMuxExporter().ExportAsync(request);
            SubtitleMuxVerification.ValidateSubtitles(SubtitleTextFormat.WriteSrt(request.Project.Subtitles, request.Project.Media!.MediaOrigin),
                await fixture.SubtitleTextAsync(result.OutputPath, result.AddedSubtitleStreamIndex));
        }

        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(".mp4")]
    public async Task AnIncompatibleOriginalSubtitleCannotBeDroppedOrImplicitlyConverted(string extension)
    {
        using var fixture = await SubtitleMuxFixture.CreateAsync(".mkv");
        var request = fixture.Request(extension);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SubtitleMuxExporter().ExportAsync(request));
        Assert.Contains("不能原样保存", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(request.OutputPath));
        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrEncodingReceiptsCannotClaimAValidatedMux(bool encoding)
    {
        using var directory = new ExportTestDirectory();
        var request = new SubtitleMuxRequest(new(), directory.Path, Path.Combine(directory.Path, "output.mkv"));
        var message = encoding
            ? new ExportWorkerMessage("complete", Frames: 10, Operation: ExportWorkerOperation.BURN_IN)
            : new ExportWorkerMessage("complete", Operation: ExportWorkerOperation.SUBTITLE_MUX);
        Assert.Throws<InvalidDataException>(() => SubtitleMuxExporter.ValidateCompleted(request, message));
    }
}
