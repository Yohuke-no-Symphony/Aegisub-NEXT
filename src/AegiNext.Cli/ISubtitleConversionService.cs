using AegiNext.SubtitleFormats;

namespace AegiNext.Cli;

/// <summary>将字幕或工程完整转换为目标字幕格式，写入由命令负责。</summary>
public interface ISubtitleConversionService
{
    /// <summary>读取并转换输入，同时返回所有需要接受的诊断。</summary>
    Task<SubtitleFormatWriteResult> ConvertAsync(string inputPath, string target, CancellationToken cancellationToken);
}
