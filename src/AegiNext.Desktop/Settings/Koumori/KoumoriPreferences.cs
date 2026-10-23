using System.Text.Json.Serialization;
using AegiNext.Koumori;

namespace AegiNext.Desktop.Settings.Koumori;

/// <summary>本地字幕时序模型的启用、定位与吸附偏好。</summary>
public sealed record KoumoriPreferences
{
    public bool Enabled { get; init; }
    public bool SnapEnabled { get; init; }
    public int MaximumSnapMilliseconds { get; init; } = 250;
    public KoumoriModelSource ModelSource { get; init; } = KoumoriModelSource.BUNDLED;
    public string ModelDirectory { get; init; } = string.Empty;

    /// <summary>根据来源选择模型目录；内置模式不读取保留的手动路径。</summary>
    [JsonIgnore]
    public string ResolvedModelDirectory => ModelSource == KoumoriModelSource.BUNDLED
        ? KoumoriAnalyzer.DefaultModelDirectory : ModelDirectory;

    /// <summary>验证持久化参数，模型内容在实际加载时验证。</summary>
    public void Validate()
    {
        if (MaximumSnapMilliseconds is < 1 or > 1000 || ModelDirectory is null || !Enum.IsDefined(ModelSource))
        {
            throw new InvalidDataException("Invalid automatic timing preferences.");
        }
    }
}
