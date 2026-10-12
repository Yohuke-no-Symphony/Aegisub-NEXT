namespace AegiNext.Cli;

/// <summary>标识用户可通过调整命令参数解决的配置错误。</summary>
public sealed class CliUsageException(string message) : Exception(message);
