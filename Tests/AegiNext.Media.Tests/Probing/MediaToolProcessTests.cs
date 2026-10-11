using AegiNext.Media.Processes;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AegiNext.Media.Probing;

namespace AegiNext.Media.Tests.Probing;

public sealed class MediaToolProcessTests
{
    [Fact]
    public void FixtureAppHostCarriesItsOwnRuntime()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ProbeTestHost");
        var executable = Path.Combine(directory, "AegiNext.Media.TestHost" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        Assert.True(File.Exists(executable), $"Fixture apphost missing: {executable}");
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "AegiNext.Media.TestHost.runtimeconfig.json")));
        var runtime = configuration.RootElement.GetProperty("runtimeOptions");
        Assert.False(runtime.TryGetProperty("framework", out _));
        Assert.Contains(runtime.GetProperty("includedFrameworks").EnumerateArray(),
            framework => framework.GetProperty("name").GetString() == "Microsoft.NETCore.App");
        var hostPolicy = OperatingSystem.IsWindows() ? "hostpolicy.dll" : OperatingSystem.IsMacOS() ? "libhostpolicy.dylib" : "libhostpolicy.so";
        Assert.True(File.Exists(Path.Combine(directory, hostPolicy)));
    }

    [Fact]
    public async Task PassesPathsAndMetacharactersAsLiteralArguments()
    {
        var values = new[] { "字幕 文件.mkv", "a\"b'c", "$(echo injected)", "-leading", "a;b&c", "" };
        var result = await RunAsync(["arguments", .. values]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(values, JsonSerializer.Deserialize<string[]>(result.StandardOutput));
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task PreservesNonzeroExitEvenWhenJsonLooksValid()
    {
        var result = await RunAsync(["exit"]);

        Assert.Equal(7, result.ExitCode);
        Assert.Equal("{\"streams\":[]}", result.StandardOutput);
        Assert.Equal("fixture failure", result.StandardError);
    }

    [Fact]
    public async Task DrainsBothPipesWithoutDeadlock()
    {
        var result = await RunAsync(["pipes"], limit: 1024 * 1024);

        Assert.Equal(409600, result.StandardOutput.Length);
        Assert.Equal(409600, result.StandardError.Length);
    }

    [Theory]
    [InlineData("stdout")]
    [InlineData("stderr")]
    public async Task RejectsUnboundedOutputAndStopsTheProcess(string stream)
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => RunAsync(["overflow", stream], limit: 1000));

        Assert.Contains("1000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeoutKillsAndReapsTheChild()
    {
        var pidFile = Path.Combine(Path.GetTempPath(), $"aeginext-pid-{Guid.NewGuid():N}");
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => RunAsync(["wait", pidFile], timeout: TimeSpan.FromSeconds(2)));
            AssertStopped(pidFile);
        }
        finally
        {
            File.Delete(pidFile);
        }
    }

    [Fact]
    public async Task CancellationKeepsCallerTokenAndReapsTheChild()
    {
        var pidFile = Path.Combine(Path.GetTempPath(), $"aeginext-pid-{Guid.NewGuid():N}");
        using var cancellation = new CancellationTokenSource();
        Task<MediaToolProcessOutput>? task = null;
        try
        {
            task = RunAsync(["wait", pidFile], cancellationToken: cancellation.Token);
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(pidFile) && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(20);
            }

            Assert.True(File.Exists(pidFile));
            await cancellation.CancelAsync();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Equal(cancellation.Token, error.CancellationToken);
            AssertStopped(pidFile);
        }
        finally
        {
            await cancellation.CancelAsync();
            if (task is not null)
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                }
            }

            File.Delete(pidFile);
            File.Delete(pidFile + ".writing");
        }
    }

    [Fact]
    public async Task AlreadyCancelledDoesNotStartAProcess()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MediaToolProcess.RunAsync("this-executable-does-not-exist", [], TimeSpan.FromSeconds(1), 1, 1, cancellation.Token));
    }

    private static Task<MediaToolProcessOutput> RunAsync(string[] args, int limit = 65536, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var host = Path.Combine(AppContext.BaseDirectory, "ProbeTestHost",
            "AegiNext.Media.TestHost" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        return MediaToolProcess.RunAsync(host, args,
            timeout ?? TimeSpan.FromSeconds(10), limit, limit, cancellationToken);
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
