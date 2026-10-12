using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AegiNext.Core.Timing;
using AegiNext.Media.Probing;

namespace AegiNext.Media.Tests.Probing;

public sealed class FfprobeVideoTimingProbeTests
{
    [Fact]
    public async Task RestrictsProtocolsUsesAbsoluteStreamAndPreservesLiteralFilePath()
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-video-probe-").FullName;
        try
        {
            var source = Path.Combine(directory, "字幕 ' $() [测试].json");
            await File.WriteAllTextAsync(source, "{\"mode\":\"success\"}");
            var probe = CreateProbe();
            var index = await probe.ProbeAsync(source, 2, new(5));
            var arguments = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(source + ".arguments"))!;

            Assert.Equal(new MediaTime[] { new(0), new(1, 10) }, index.FrameTimes);
            Assert.Equal<int>([0, 1], index.Keyframes);
            Assert.Equal("file", arguments[Array.IndexOf(arguments, "-protocol_whitelist") + 1]);
            Assert.Equal("2", arguments[Array.IndexOf(arguments, "-select_streams") + 1]);
            Assert.Equal(source, arguments[Array.IndexOf(arguments, "-i") + 1]);
            Assert.DoesNotContain("-r", arguments);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("overflow")]
    [InlineData("version")]
    [InlineData("decode-error")]
    public async Task RejectsOutputOverflowAndChangedToolIdentity(string mode)
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-video-probe-").FullName;
        try
        {
            var source = Path.Combine(directory, "fixture.json");
            await File.WriteAllTextAsync(source, JsonSerializer.Serialize(new { mode }));

            await Assert.ThrowsAsync<InvalidDataException>(() => CreateProbe(outputLimit: 2048).ProbeAsync(source, 2, new(5)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("change-source")]
    [InlineData("replace-source")]
    public async Task RejectsSourceMutationOrSamePathReplacementDuringScan(string mode)
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-video-probe-").FullName;
        try
        {
            var source = Path.Combine(directory, "fixture.json");
            await File.WriteAllTextAsync(source, JsonSerializer.Serialize(new { mode }));

            var error = await Assert.ThrowsAsync<InvalidDataException>(() => CreateProbe().ProbeAsync(source, 2, new(5)));

            Assert.Contains("media file changed", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ScanTimeoutKillsAndReapsChild()
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-video-probe-").FullName;
        try
        {
            var source = Path.Combine(directory, "wait.json");
            var pidFile = Path.Combine(directory, "pid");
            await File.WriteAllTextAsync(source, JsonSerializer.Serialize(new { mode = "wait", pidFile }));

            await Assert.ThrowsAsync<TimeoutException>(() => CreateProbe(timeout: TimeSpan.FromSeconds(2)).ProbeAsync(source, 2, default));

            AssertStopped(pidFile);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ScanCancellationPreservesCallerTokenAndReapsChild()
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-video-probe-").FullName;
        using var cancellation = new CancellationTokenSource();
        Task? scan = null;
        try
        {
            var source = Path.Combine(directory, "wait.json");
            var pidFile = Path.Combine(directory, "pid");
            await File.WriteAllTextAsync(source, JsonSerializer.Serialize(new { mode = "wait", pidFile }));
            scan = CreateProbe().ProbeAsync(source, 2, default, cancellation.Token);
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(pidFile) && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(20);
            }

            Assert.True(File.Exists(pidFile));
            await cancellation.CancelAsync();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);

            Assert.Equal(cancellation.Token, error.CancellationToken);
            AssertStopped(pidFile);
        }
        finally
        {
            await cancellation.CancelAsync();
            if (scan is not null)
            {
                await scan.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AlreadyCancelledNeverInvokesTool()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var probe = new FfprobeVideoTimingProbe(new(Path.Combine(Path.GetTempPath(), "missing-tool")));

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            probe.ProbeAsync(Path.Combine(Path.GetTempPath(), "missing-video"), 0, default, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    private static FfprobeVideoTimingProbe CreateProbe(int outputLimit = 65536, TimeSpan? timeout = null)
    {
        var host = Path.Combine(AppContext.BaseDirectory, "ProbeTestHost",
            "AegiNext.Media.TestHost" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        return new(new(host, timeout ?? TimeSpan.FromSeconds(10), outputLimit));
    }

    private static void AssertStopped(string pidFile)
    {
        var pid = int.Parse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture);
        try
        {
            using var process = Process.GetProcessById(pid);
            Assert.True(process.HasExited);
        }
        catch (ArgumentException)
        {
        }
    }
}
