using System.Globalization;

namespace AegiNext.Cli;

/// <summary>命令共用的路径验证和无覆盖输出命名。</summary>
public static class CliPaths
{
    /// <summary>解析当前目录下的输入并验证允许的扩展名。</summary>
    public static string ResolveInput(string path, params string[] extensions)
    {
        var fullPath = FullPath(path);
        if (!extensions.Any(extension => string.Equals(Path.GetExtension(fullPath), NormalizeExtension(extension), StringComparison.OrdinalIgnoreCase)))
        {
            throw new CliUsageException($"输入格式不受支持；允许的扩展名：{string.Join("、", extensions)}。");
        }
        if (!File.Exists(fullPath))
        {
            throw new CliUsageException($"输入文件不存在：{fullPath}");
        }
        return fullPath;
    }

    /// <summary>确定文件或目录输出，自动名称避让冲突，显式文件永不覆盖。</summary>
    public static string ResolveOutput(string input, string? output, string extension, bool video = false, TimeProvider? clock = null)
    {
        var inputPath = FullPath(input);
        var suffix = NormalizeExtension(extension);
        var directoryOutput = output is null || IsDirectoryOutput(output);
        var fullOutput = output is null
            ? video ? Path.Combine(Path.GetDirectoryName(inputPath)!, "output") : Path.GetDirectoryName(inputPath)!
            : FullPath(output);
        if (!directoryOutput)
        {
            if (!string.Equals(Path.GetExtension(fullOutput), suffix, StringComparison.OrdinalIgnoreCase))
            {
                throw new CliUsageException($"输出扩展名必须为 {suffix}：{fullOutput}");
            }
            ValidateOutputFile(inputPath, fullOutput);
            return fullOutput;
        }
        if (File.Exists(fullOutput))
        {
            throw new CliUsageException($"输出目录路径已被文件占用：{fullOutput}");
        }
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        if (video)
        {
            stem += "-" + (clock ?? TimeProvider.System).GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        }
        else if (SamePath(Path.Combine(fullOutput, stem + suffix), inputPath))
        {
            stem += ".converted";
        }
        var candidate = Path.Combine(fullOutput, stem + suffix);
        for (var number = 1; File.Exists(candidate) || Directory.Exists(candidate); number++)
        {
            candidate = Path.Combine(fullOutput, $"{stem}-{number.ToString(CultureInfo.InvariantCulture)}{suffix}");
        }
        ValidateOutputFile(inputPath, candidate);
        return candidate;
    }

    /// <summary>在询问缺少参数前，独立验证显式输出的格式和占用情况。</summary>
    public static string ValidateExplicitOutput(string output, IEnumerable<string> allowedExtensions, string? expectedExtension = null)
    {
        ArgumentNullException.ThrowIfNull(allowedExtensions);
        var fullPath = FullPath(output);
        if (IsDirectoryOutput(output))
        {
            if (File.Exists(fullPath))
            {
                throw new CliUsageException($"输出目录路径已被文件占用：{fullPath}");
            }
            return fullPath;
        }
        var extension = Path.GetExtension(fullPath);
        var extensions = allowedExtensions.Select(NormalizeExtension).ToArray();
        if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new CliUsageException($"输出文件的扩展名必须为 {string.Join("、", extensions)}。");
        }
        if (expectedExtension is not null && !extension.Equals(NormalizeExtension(expectedExtension), StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException($"输出扩展名必须为 {NormalizeExtension(expectedExtension)}：{fullPath}");
        }
        if (File.Exists(fullPath))
        {
            throw new CliUsageException($"输出已存在，不能覆盖：{fullPath}");
        }
        return fullPath;
    }

    /// <summary>判断用户路径是否按目录解释。</summary>
    public static bool IsDirectoryOutput(string output)
    {
        var fullPath = FullPath(output);
        return Directory.Exists(fullPath) || Path.EndsInDirectorySeparator(output) || !Path.HasExtension(output);
    }

    /// <summary>将用户路径转为绝对路径，并统一路径语法错误。</summary>
    public static string FullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CliUsageException("文件路径不能为空。");
        }
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new CliUsageException($"文件路径无效：{path}");
        }
    }

    private static void ValidateOutputFile(string input, string output)
    {
        if (SamePath(input, output))
        {
            throw new CliUsageException("输入与输出不能为同一路径。");
        }
        if (File.Exists(output) || Directory.Exists(output))
        {
            throw new CliUsageException($"输出已存在，不能覆盖：{output}");
        }
    }

    private static bool SamePath(string first, string second)
    {
        return string.Equals(first, second, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        return extension.StartsWith('.') ? extension : "." + extension;
    }
}
