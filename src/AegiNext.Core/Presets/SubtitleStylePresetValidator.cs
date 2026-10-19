using System.Diagnostics.CodeAnalysis;
using System.Text;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Presets;

/// <summary>验证便携样式元数据，不访问文件系统或持有字体字节。</summary>
public static class SubtitleStylePresetValidator
{
    /// <summary>验证一个不携带工程字体标识的自包含样式。</summary>
    public static void Validate(SubtitleStylePreset preset)
    {
        Require(preset is not null && preset.Id != Guid.Empty, "预设标识无效。");
        ValidateName(preset.Name);
        ProjectValidator.ValidateSubtitleStyle(preset.Style);
        Require(preset.Style.FontAssetId is null, "便携样式不能持有项目字体标识；请捕获完整字体族。");
        try
        {
            preset.TimingPostProcessor?.Validate();
        }
        catch (ArgumentOutOfRangeException error)
        {
            throw new InvalidDataException("字幕样式关联的时间后续处理器参数无效。", error);
        }
    }

    /// <summary>验证当前版本、集合预算、重复标识和名称。</summary>
    public static void Validate(SubtitleStylePresetCollection collection)
    {
        Require(collection is not null && collection.Version == SubtitleStylePresetCollection.CURRENT_VERSION,
            "不支持的字幕样式库版本。");
        Require(!collection.Presets.IsDefault && collection.Presets.Length <= 256, "样式库集合无效或超过 256 项。");
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in collection.Presets)
        {
            Require(preset is not null, "预设不能为 null。");
            Validate(preset);
            Require(ids.Add(preset.Id) && names.Add(preset.Name), "预设标识或名称重复；名称不区分大小写。");
        }
    }

    private static void ValidateName(string name)
    {
        Require(name is { Length: > 0 and <= 128 }, "预设名称长度必须为 1 至 128。");
        ProjectValidator.ValidateText(name);
        Require(!name.Any(char.IsControl) && name == name.Trim() && name.IsNormalized(NormalizationForm.FormC),
            "预设名称必须采用 NFC Unicode，且不能包含控制字符或首尾空白。");
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
