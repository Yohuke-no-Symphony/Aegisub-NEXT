using AegiNext.Media.Processes;
using System.Buffers.Binary;
using System.Globalization;
using AegiNext.Core.Timing;
using AegiNext.Media.Probing;

namespace AegiNext.Media.Tests.Audio;

internal sealed class FlacLacingFixture : IDisposable
{
    internal const int SAMPLE_RATE = 48000;
    internal const int FRAME_SAMPLES = 4096;
    internal const int FRAMES_PER_BLOCK = 8;
    private readonly string directory;

    private FlacLacingFixture(string directory, int groupCount, int offsetMilliseconds, int shiftMilliseconds)
    {
        this.directory = directory;
        MediaPath = Path.Combine(directory, "laced.mkv");
        SampleCount = groupCount * FRAMES_PER_BLOCK * FRAME_SAMPLES;
        Origin = new(offsetMilliseconds, 1000);
        Duration = new MediaTime(SampleCount, SAMPLE_RATE) + new MediaTime(shiftMilliseconds, 1000);
    }

    internal string MediaPath { get; }
    internal int SampleCount { get; }
    internal MediaTime Origin { get; }
    internal MediaTime Duration { get; }

    internal static async Task<FlacLacingFixture> CreateAsync(int groupCount = 3, int offsetMilliseconds = 0,
        int shiftMilliseconds = 0, int shiftedGroup = 1)
    {
        var executable = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH");
        Assert.False(string.IsNullOrWhiteSpace(executable));
        Assert.True(Path.IsPathFullyQualified(executable));
        var directory = Path.Combine(Path.GetTempPath(), $"aeginext-flac-lacing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var fixture = new FlacLacingFixture(directory, groupCount, offsetMilliseconds, shiftMilliseconds);
        try
        {
            var wavePath = Path.Combine(directory, "source.wav");
            var flacPath = Path.Combine(directory, "source.flac");
            var packetsPath = Path.Combine(directory, "packets.framecrc");
            WriteWave(wavePath, fixture.SampleCount);
            await RunFfmpegAsync(executable,
                ["-v", "error", "-nostdin", "-i", wavePath, "-c:a", "flac", "-frame_size", FRAME_SAMPLES.ToString(CultureInfo.InvariantCulture),
                    "-y", flacPath]);
            await RunFfmpegAsync(executable,
                ["-v", "error", "-nostdin", "-i", flacPath, "-map", "0:a:0", "-c:a", "copy", "-f", "framecrc", "-y", packetsPath]);
            var flac = await File.ReadAllBytesAsync(flacPath);
            var packetLines = await File.ReadAllLinesAsync(packetsPath);
            var sizes = packetLines.Where(line => !line.StartsWith('#') && !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Split(','))
                .Select(fields =>
                {
                    Assert.Equal(FRAME_SAMPLES, int.Parse(fields[3], CultureInfo.InvariantCulture));
                    return int.Parse(fields[4], CultureInfo.InvariantCulture);
                }).ToArray();
            Assert.Equal(groupCount * FRAMES_PER_BLOCK, sizes.Length);
            WriteMatroska(fixture.MediaPath, flac, sizes, groupCount, offsetMilliseconds, shiftMilliseconds, shiftedGroup);
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(directory, true);
    }

    private static async Task RunFfmpegAsync(string executable, string[] arguments)
    {
        var result = await MediaToolProcess.RunAsync(executable, arguments, TimeSpan.FromSeconds(20),
            1024 * 1024, 1024 * 1024, CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    private static void WriteWave(string path, int count)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + count * 4);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(SAMPLE_RATE);
        writer.Write(SAMPLE_RATE * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(count * 4);
        for (var sample = 0; sample < count; sample++)
        {
            var value = (short)(16000 * Math.Sin(sample * 2 * Math.PI * 1000 / SAMPLE_RATE));
            writer.Write(value);
            writer.Write(value);
        }
    }

    private static void WriteMatroska(string path, byte[] flac, int[] sizes, int groupCount,
        int offsetMilliseconds, int shiftMilliseconds, int shiftedGroup)
    {
        Assert.Equal("fLaC"u8.ToArray(), flac.AsSpan(0, 4).ToArray());
        var frameOffset = 4;
        while (true)
        {
            var last = (flac[frameOffset] & 0x80) != 0;
            var length = flac[frameOffset + 1] << 16 | flac[frameOffset + 2] << 8 | flac[frameOffset + 3];
            frameOffset += 4 + length;
            if (last)
            {
                break;
            }
        }
        var frames = new byte[sizes.Length][];
        for (var index = 0; index < sizes.Length; index++)
        {
            frames[index] = flac.AsSpan(frameOffset, sizes[index]).ToArray();
            frameOffset += sizes[index];
        }
        Assert.Equal(flac.Length, frameOffset);
        var codecPrivate = flac.AsSpan(0, 42).ToArray();
        codecPrivate[4] = 0x80;
        var header = Element(0x1A45DFA3, Combine(
            Unsigned(0x4286, 1), Unsigned(0x42F7, 1), Unsigned(0x42F2, 4), Unsigned(0x42F3, 8),
            Text(0x4282, "matroska"), Unsigned(0x4287, 4), Unsigned(0x4285, 2)));
        var info = Element(0x1549A966, Combine(Unsigned(0x2AD7B1, 1000000),
            Floating(0x4489, groupCount * FRAMES_PER_BLOCK * FRAME_SAMPLES * 1000.0 / SAMPLE_RATE + offsetMilliseconds + shiftMilliseconds),
            Text(0x4D80, "AegiNext tests"), Text(0x5741, "AegiNext tests")));
        var tracks = Element(0x1654AE6B, Element(0xAE, Combine(Unsigned(0xD7, 1), Unsigned(0x73C5, 1),
            Unsigned(0x83, 2), Text(0x86, "A_FLAC"), Element(0x63A2, codecPrivate),
            Unsigned(0x23E383, FRAME_SAMPLES * 1000000000L / SAMPLE_RATE),
            Element(0xE1, Combine(Floating(0xB5, SAMPLE_RATE), Unsigned(0x9F, 2), Unsigned(0x6264, 16))))));
        var clusters = new byte[groupCount][];
        var times = new long[groupCount];
        for (var group = 0; group < groupCount; group++)
        {
            times[group] = new MediaTime((long)group * FRAMES_PER_BLOCK * FRAME_SAMPLES, SAMPLE_RATE)
                .ToTimestamp(new(1, 1000), MediaTimeRounding.TO_EVEN).Value + offsetMilliseconds +
                (group >= shiftedGroup ? shiftMilliseconds : 0);
            clusters[group] = Element(0x1F43B675, Combine(Unsigned(0xE7, times[group]),
                LacedBlock(frames.AsSpan(group * FRAMES_PER_BLOCK, FRAMES_PER_BLOCK))));
        }
        var cues = Array.Empty<byte>();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var clusterPosition = info.Length + tracks.Length + cues.Length;
            var points = new byte[groupCount][];
            for (var group = 0; group < groupCount; group++)
            {
                points[group] = Element(0xBB, Combine(Unsigned(0xB3, times[group]),
                    Element(0xB7, Combine(Unsigned(0xF7, 1), Unsigned(0xF1, clusterPosition)))));
                clusterPosition += clusters[group].Length;
            }
            var next = Element(0x1C53BB6B, Combine(points));
            if (next.AsSpan().SequenceEqual(cues))
            {
                File.WriteAllBytes(path, Combine(header, Element(0x18538067, Combine(info, tracks, cues, Combine(clusters)))));
                return;
            }
            cues = next;
        }
        throw new InvalidDataException("Matroska 测试定位索引未收敛。");
    }

    private static byte[] LacedBlock(ReadOnlySpan<byte[]> frames)
    {
        using var stream = new MemoryStream();
        stream.Write([0x81, 0, 0, 0x86, (byte)(frames.Length - 1)]);
        stream.Write(VariableInteger(frames[0].Length));
        for (var index = 1; index < frames.Length - 1; index++)
        {
            var difference = frames[index].Length - frames[index - 1].Length;
            for (var length = 1; length <= 8; length++)
            {
                var bias = (1L << (length * 7 - 1)) - 1;
                if (difference >= -bias && difference <= bias)
                {
                    stream.Write(VariableInteger(difference + bias, length));
                    break;
                }
            }
        }
        foreach (var frame in frames)
        {
            stream.Write(frame);
        }
        return Element(0xA3, stream.ToArray());
    }

    private static byte[] Element(uint id, byte[] payload)
    {
        return Combine(BigEndian(id), VariableInteger(payload.Length), payload);
    }

    private static byte[] Unsigned(uint id, long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return Element(id, BigEndian((ulong)value));
    }

    private static byte[] Text(uint id, string value)
    {
        return Element(id, System.Text.Encoding.UTF8.GetBytes(value));
    }

    private static byte[] Floating(uint id, double value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
        return Element(id, bytes);
    }

    private static byte[] VariableInteger(long value, int length = 0)
    {
        if (length == 0)
        {
            length = 1;
            while (value >= (1L << (length * 7)) - 1)
            {
                length++;
            }
        }
        var bytes = new byte[length];
        var encoded = (ulong)value | 1UL << (length * 7);
        for (var index = length - 1; index >= 0; index--)
        {
            bytes[index] = (byte)encoded;
            encoded >>= 8;
        }
        return bytes;
    }

    private static byte[] BigEndian(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        var first = 0;
        while (first < bytes.Length - 1 && bytes[first] == 0)
        {
            first++;
        }
        return bytes.AsSpan(first).ToArray();
    }

    private static byte[] Combine(params byte[][] items)
    {
        using var stream = new MemoryStream();
        foreach (var item in items)
        {
            stream.Write(item);
        }
        return stream.ToArray();
    }
}
