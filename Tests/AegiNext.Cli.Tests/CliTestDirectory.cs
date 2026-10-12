namespace AegiNext.Cli.Tests;

internal sealed class CliTestDirectory : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aeginext-cli-tests", Guid.NewGuid().ToString("N"));

    internal CliTestDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    internal string File(string name)
    {
        return System.IO.Path.Combine(Path, name);
    }

    internal async Task<string> InputAsync(string name = "input.srt", string text = "1\n00:00:00,000 --> 00:00:02,000\n你好 😀\n")
    {
        var file = File(name);
        await System.IO.File.WriteAllTextAsync(file, text);
        return file;
    }

    public void Dispose()
    {
        Directory.Delete(Path, true);
    }
}
