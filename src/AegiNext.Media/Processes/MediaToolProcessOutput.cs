namespace AegiNext.Media.Processes;

/// <summary>媒体工具退出状态与经过容量限制的标准输出和诊断。</summary>
public sealed record MediaToolProcessOutput(int ExitCode, string StandardOutput, string StandardError);
