using System.Diagnostics.CodeAnalysis;
using System.Text;
using AegiNext.Core.Projects;

namespace AegiNext.Export.Presets;

/// <summary>验证便携压制预设的名称、身份、显式参数、版本和集合预算，不访问文件系统。</summary>
public static class VideoExportPresetValidator
{
    public const int MAX_NAME_LENGTH = 128;
    private const int MAXIMUM_PRESETS = 256;

    /// <summary>验证一个命名预设及其完整的显式压制设置。</summary>
    public static void Validate(VideoExportPreset? preset)
    {
        Require(preset is not null && preset.Id != Guid.Empty, "压制预设标识无效。");
        var name = preset.Name;
        Require(name is { Length: > 0 and <= MAX_NAME_LENGTH }, "压制预设名称长度必须为 1 至 128。");
        ProjectValidator.ValidateText(name);
        Require(!name.Any(char.IsControl) && name == name.Trim() && name.IsNormalized(NormalizationForm.FormC),
            "压制预设名称必须采用 NFC Unicode，且不能包含控制字符或首尾空白。");
        try
        {
            VideoExportSettingsValidator.ValidatePreset(preset.Settings);
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException("压制预设参数无效。", error);
        }
    }

    /// <summary>验证当前版本、集合预算和全部预设；身份与不区分大小写的名称必须唯一。</summary>
    public static void Validate(VideoExportPresetCollection? collection)
    {
        Require(collection is not null && collection.Version == VideoExportPresetCollection.CURRENT_VERSION,
            "不支持的压制预设库版本。");
        Require(!collection.Presets.IsDefault && collection.Presets.Length <= MAXIMUM_PRESETS,
            "压制预设集合无效或超过 256 项。");
        var identities = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in collection.Presets)
        {
            Validate(preset);
            Require(identities.Add(preset.Id) && names.Add(preset.Name), "压制预设标识或名称重复；名称不区分大小写。");
        }
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
