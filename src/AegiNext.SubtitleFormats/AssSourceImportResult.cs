namespace AegiNext.SubtitleFormats;

/// <summary>ASS 的有效源画布尺寸与完整导入结果；缺失的 PlayRes 轴采用默认尺寸并在结果中报告。</summary>
/// <param name="Width">有效源画布宽度。</param>
/// <param name="Height">有效源画布高度。</param>
/// <param name="Result">保留字幕、蒙版、动画及转换诊断的导入结果。</param>
public sealed record AssSourceImportResult(int Width, int Height, AssImportResult Result);
