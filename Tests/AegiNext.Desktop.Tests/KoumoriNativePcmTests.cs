using System.Globalization;
using System.Text;
using System.Text.Json;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Koumori;
using AegiNext.Koumori;
using AegiNext.Media.Audio;
using Xunit.Abstractions;

namespace AegiNext.Desktop.Tests;

public sealed class KoumoriNativePcmTests(ITestOutputHelper output)
{
    [KoumoriNativePcmFact]
    public async Task NativeMonoPcmAndCachedWindowsMatchTheTrainingExtraction()
    {
        var path = Environment.GetEnvironmentVariable("KOUMORI_NATIVE_MEDIA")!;
        var trainingPath = Environment.GetEnvironmentVariable("KOUMORI_TRAINING_PCM")!;
        var streamIndex = int.Parse(Environment.GetEnvironmentVariable("KOUMORI_NATIVE_STREAM") ?? "1", CultureInfo.InvariantCulture);
        var originSeconds = decimal.Parse(Environment.GetEnvironmentVariable("KOUMORI_NATIVE_ORIGIN") ?? "0", CultureInfo.InvariantCulture);
        var origin = new MediaTime(checked((long)(originSeconds * 1000000)), 1000000);
        var expected = ReadTrainingWave(trainingPath);
        var sequential = new float[expected.Length];
        using (var continuous = FfmpegAudioDecoder.Open(path, streamIndex, new(16000, 1), timestampPolicy: AudioTimestampPolicy.FFMPEG_ASYNC_ONE))
        {
            while (continuous.Read() is { } block)
            {
                var start = checked((int)(block.Start - origin).ToTimestamp(new(1, 16000), MediaTimeRounding.TO_EVEN).Value);
                var first = Math.Max(0, start);
                var last = Math.Min(expected.Length, start + block.FrameCount);
                if (last > first)
                {
                    block.Samples.Span.Slice(first - start, last - first).CopyTo(sequential.AsSpan(first));
                }
            }
        }
        var sequentialMaximum = 0f;
        for (var index = 0; index < expected.Length; index++)
        {
            sequentialMaximum = Math.Max(sequentialMaximum, Math.Abs(expected[index] - sequential[index]));
        }
        output.WriteLine($"Sequential full-media PCM maximum absolute error: {sequentialMaximum:R}.");
        Assert.InRange(sequentialMaximum, 0, 0.000001f);
        using var source = new KoumoriMediaSource(FfmpegAudioDecoder.Open(path, streamIndex, new(16000, 1), timestampPolicy: AudioTimestampPolicy.FFMPEG_ASYNC_ONE), expected.Length, origin, CachePath());
        var records = new List<object>();
        var failures = new List<string>();
        var starts = new[] { Math.Max(0, expected.Length - 32000L), 0L, 1600160L, 165920L };
        foreach (var start in starts.Where(value => value < expected.Length).Distinct())
        {
            var actual = new float[Math.Min(32000, expected.Length - start)];
            var read = await source.ReadAsync(start, actual, default);
            var reference = expected.AsSpan((int)start, actual.Length).ToArray();
            var squaredError = 0d;
            var referenceEnergy = 0d;
            var cross = 0d;
            var maximum = 0f;
            for (var index = 0; index < actual.Length; index++)
            {
                var error = actual[index] - reference[index];
                squaredError += (double)error * error;
                referenceEnergy += (double)reference[index] * reference[index];
                cross += (double)actual[index] * reference[index];
                maximum = Math.Max(maximum, Math.Abs(error));
            }
            var rmse = Math.Sqrt(squaredError / actual.Length);
            var normalizedError = referenceEnergy == 0 ? 0 : Math.Sqrt(squaredError / referenceEnergy);
            var gain = referenceEnergy == 0 ? 1 : cross / referenceEnergy;
            var bestLag = BestLag(reference, actual);
            records.Add(new { startSample = start, requestedSamples = actual.Length, returnedSamples = read, rmse, maximum, normalizedError, gain, bestLag });
            output.WriteLine($"PCM start={start}, returned={read}/{actual.Length}, rmse={rmse:R}, max={maximum:R}, normalized={normalizedError:R}, gain={gain:R}, bestLag={bestLag}.");
            if (maximum > 0.000001 || Math.Abs(gain - 1) > 0.00001 || bestLag != 0)
            {
                failures.Add($"PCM window {start}: rmse={rmse:R}, normalizedError={normalizedError:R}, gain={gain:R}, bestLag={bestLag}.");
            }
            if (read != actual.Length)
            {
                failures.Add($"Native source failed to preserve the declared media duration at {start + read}.");
            }
        }
        var reportPath = Environment.GetEnvironmentVariable("KOUMORI_NATIVE_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { media = path, trainingPcm = trainingPath, streamIndex, originSeconds, sequentialMaximum, windows = records, failures }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [KoumoriNativeModelFact]
    public async Task TrainedModelBoundariesRemainStableAcrossCachedAudioWindows()
    {
        var path = Environment.GetEnvironmentVariable("KOUMORI_NATIVE_MEDIA")!;
        var trainingPath = Environment.GetEnvironmentVariable("KOUMORI_TRAINING_PCM")!;
        var modelPath = Environment.GetEnvironmentVariable("KOUMORI_NATIVE_MODEL")!;
        var streamIndex = int.Parse(Environment.GetEnvironmentVariable("KOUMORI_NATIVE_STREAM") ?? "1", CultureInfo.InvariantCulture);
        var originSeconds = decimal.Parse(Environment.GetEnvironmentVariable("KOUMORI_NATIVE_ORIGIN") ?? "0", CultureInfo.InvariantCulture);
        var origin = new MediaTime(checked((long)(originSeconds * 1000000)), 1000000);
        var expected = ReadTrainingWave(trainingPath);
        var reference = new KoumoriTestArrayAudioSource(expected);
        using var source = new KoumoriMediaSource(FfmpegAudioDecoder.Open(path, streamIndex, new(16000, 1), timestampPolicy: AudioTimestampPolicy.FFMPEG_ASYNC_ONE), expected.Length, origin, CachePath());
        await using var analyzer = await KoumoriAnalyzer.LoadAsync(modelPath);
        var records = new List<object>();
        var ranges = new List<KoumoriAnalysisRange> { new(0, expected.Length) };
        ranges.AddRange(new[] { Math.Max(0, expected.Length - 32000L), 0L, 1600160L, 165920L }
            .Where(value => value < expected.Length).Distinct().Select(start => new KoumoriAnalysisRange(start, Math.Min(expected.Length, start + 32000))));
        foreach (var range in ranges)
        {
            var gold = await analyzer.AnalyzeAsync(reference, range);
            var actual = await analyzer.AnalyzeAsync(source, range);
            var displacements = new List<double>();
            var scoreMaximum = 0f;
            var unmatched = actual.Boundaries.ToList();
            var missing = 0;
            foreach (var boundary in gold.Boundaries)
            {
                var candidate = unmatched.Where(value => value.Kind == boundary.Kind && value.Slot == boundary.Slot &&
                        Math.Abs(value.Sample - boundary.Sample) <= 800)
                    .MinBy(value => Math.Abs(value.Sample - boundary.Sample));
                if (candidate is null)
                {
                    missing++;
                    continue;
                }
                unmatched.Remove(candidate);
                displacements.Add(Math.Abs(candidate.Sample - boundary.Sample) / 16d);
                scoreMaximum = Math.Max(scoreMaximum, Math.Abs(candidate.Score - boundary.Score));
            }
            var maximumMilliseconds = displacements.Count == 0 ? 0 : displacements.Max();
            var within10 = displacements.Count(value => value <= 10);
            var within50 = displacements.Count(value => value <= 50);
            var segmentsEqual = gold.Segments.Select(value => (value.StartSample, value.EndSample, value.Slot))
                .SequenceEqual(actual.Segments.Select(value => (value.StartSample, value.EndSample, value.Slot)));
            records.Add(new { startSample = range.StartSample, endSample = range.EndSample, referenceBoundaries = gold.Boundaries.Count, actualBoundaries = actual.Boundaries.Count,
                missing, extra = unmatched.Count, maximumMilliseconds, within10, within50, scoreMaximum, referenceSegments = gold.Segments.Count, actualSegments = actual.Segments.Count, segmentsEqual });
            output.WriteLine($"Model {analyzer.ModelId} at {range.StartSample}..{range.EndSample}: edges={gold.Boundaries.Count}/{actual.Boundaries.Count}, missing/extra={missing}/{unmatched.Count}, max shift={maximumMilliseconds} ms, within 10/50 ms={within10}/{within50}, max score difference={scoreMaximum:R}, segments equal={segmentsEqual}.");
            Assert.Equal(gold.Boundaries.Count, actual.Boundaries.Count);
            Assert.Equal(0, missing);
            Assert.Empty(unmatched);
            Assert.Equal(0, maximumMilliseconds);
            Assert.Equal(0, scoreMaximum);
            Assert.True(segmentsEqual);
        }
        var reportPath = Environment.GetEnvironmentVariable("KOUMORI_NATIVE_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            await File.WriteAllTextAsync(Path.ChangeExtension(reportPath, ".model.json"), JsonSerializer.Serialize(new
            {
                media = path, trainingPcm = trainingPath, analyzer.ModelId, analyzer.QualityStatus,
                note = "PCM frontend and cached window stability only; this does not establish subtitle accuracy.", windows = records
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static string CachePath() => Path.Combine(Path.GetTempPath(), "aeginext-koumori-tests", Guid.NewGuid().ToString("N") + ".f32");

    private static int BestLag(float[] reference, float[] actual)
    {
        var bestLag = 0;
        var bestError = double.PositiveInfinity;
        foreach (var lag in new[] { 0, -1, 1, -2, 2, -3, 3, -4, 4 })
        {
            var squared = 0d;
            for (var index = 4; index < reference.Length - 4; index++)
            {
                var difference = reference[index] - actual[index + lag];
                squared += (double)difference * difference;
            }
            if (squared < bestError)
            {
                bestLag = lag;
                bestError = squared;
            }
        }
        return bestLag;
    }

    private static float[] ReadTrainingWave(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        reader.ReadUInt32();
        Assert.Equal("WAVE", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        var formatVerified = false;
        while (stream.Position + 8 <= stream.Length)
        {
            var kind = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadUInt32();
            var next = stream.Position + size + (size & 1);
            if (kind == "fmt ")
            {
                Assert.Equal(3, reader.ReadUInt16());
                Assert.Equal(1, reader.ReadUInt16());
                Assert.Equal(16000, reader.ReadInt32());
                reader.ReadInt32();
                reader.ReadUInt16();
                Assert.Equal(32, reader.ReadUInt16());
                formatVerified = true;
            }
            else if (kind == "data")
            {
                Assert.True(formatVerified);
                var samples = new float[size / 4];
                for (var index = 0; index < samples.Length; index++)
                {
                    samples[index] = reader.ReadSingle();
                }
                return samples;
            }
            stream.Position = next;
        }
        throw new InvalidDataException("The training PCM WAV has no data chunk.");
    }
}
