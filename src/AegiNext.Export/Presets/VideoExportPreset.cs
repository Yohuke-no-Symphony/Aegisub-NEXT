namespace AegiNext.Export.Presets;

/// <summary>命名的便携压制配置，不持有工程、输出或机器工具路径。</summary>
public sealed record VideoExportPreset(Guid Id, string Name, VideoExportSettings Settings);
