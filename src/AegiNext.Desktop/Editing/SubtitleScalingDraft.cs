using System.ComponentModel;
using System.Globalization;
using AegiNext.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Editing;

/// <summary>保留参考尺寸原文及宽高匹配的纯草稿，不持有界面控件或工程事务。</summary>
public sealed class SubtitleScalingDraft : ObservableObject
{
    private const int MAX_REFERENCE_DIMENSION = 32768;
    private SubtitleScalingSettings source = new();
    private CultureInfo widthCulture = CultureInfo.CurrentCulture;
    private CultureInfo heightCulture = CultureInfo.CurrentCulture;
    private double matchWidthOrHeight = 0.5;
    private bool loading;

    /// <summary>创建两个独立数值草稿，并载入 1920×1080、宽高均衡匹配。</summary>
    public SubtitleScalingDraft()
    {
        ReferenceWidth.PropertyChanged += FieldChanged;
        ReferenceHeight.PropertyChanged += FieldChanged;
        Load(new());
    }

    public NumericValueDraft ReferenceWidth { get; } = new();
    public NumericValueDraft ReferenceHeight { get; } = new();
    public event EventHandler? Changed;

    /// <summary>零按宽度、一按高度，中间值使用对数混合；无效草稿由验证入口报告。</summary>
    public double MatchWidthOrHeight
    {
        get => matchWidthOrHeight;
        set
        {
            if (SetProperty(ref matchWidthOrHeight, value) && !loading)
            {
                RaiseChanged();
            }
        }
    }

    public bool IsDirty => !TryReadDimension(ReferenceWidth, widthCulture, out var width) || width != source.ReferenceWidth ||
                           !TryReadDimension(ReferenceHeight, heightCulture, out var height) || height != source.ReferenceHeight ||
                           !MatchWidthOrHeight.Equals(source.MatchWidthOrHeight);

    /// <summary>载入已提交设置，更新恢复快照而不产生用户修改事件。</summary>
    public void Load(SubtitleScalingSettings settings, CultureInfo? culture = null)
    {
        ProjectValidator.ValidateSubtitleScaling(settings);
        loading = true;
        try
        {
            source = settings;
            widthCulture = heightCulture = culture ?? CultureInfo.CurrentCulture;
            LoadDimension(ReferenceWidth, settings.ReferenceWidth, widthCulture);
            LoadDimension(ReferenceHeight, settings.ReferenceHeight, heightCulture);
            MatchWidthOrHeight = settings.MatchWidthOrHeight;
        }
        finally
        {
            loading = false;
        }
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>返回第一个无效字段；参考宽高必须是 1–32768 的整数。</summary>
    public string? Validate()
    {
        if (!TryReadDimension(ReferenceWidth, widthCulture, out _))
        {
            return "ReferenceWidthInput";
        }
        if (!TryReadDimension(ReferenceHeight, heightCulture, out _))
        {
            return "ReferenceHeightInput";
        }
        return double.IsFinite(MatchWidthOrHeight) && MatchWidthOrHeight is >= 0 and <= 1
            ? null : "MatchWidthOrHeightInput";
    }

    /// <summary>读取已验证原文，创建持久化的参考尺寸与匹配设置。</summary>
    public SubtitleScalingSettings CreateSettings()
    {
        if (Validate() is not null)
        {
            throw new InvalidDataException("字幕参考尺寸草稿无效。");
        }
        _ = TryReadDimension(ReferenceWidth, widthCulture, out var width);
        _ = TryReadDimension(ReferenceHeight, heightCulture, out var height);
        return new()
        {
            ReferenceWidth = width, ReferenceHeight = height, MatchWidthOrHeight = MatchWidthOrHeight
        };
    }

    /// <summary>只恢复指定字段最近一次载入的值，保留其他未完成输入。</summary>
    public bool RestoreField(string fieldKey)
    {
        return fieldKey switch
        {
            "ReferenceWidthInput" => LoadField(fieldKey, source.ReferenceWidth, widthCulture),
            "ReferenceHeightInput" => LoadField(fieldKey, source.ReferenceHeight, heightCulture),
            "MatchWidthOrHeightInput" => LoadField(fieldKey, source.MatchWidthOrHeight),
            _ => false
        };
    }

    /// <summary>替换单个字段及恢复快照，供宿主按当前已提交字幕恢复输入。</summary>
    public bool LoadField(string fieldKey, double number, CultureInfo? culture = null)
    {
        var settings = fieldKey switch
        {
            "ReferenceWidthInput" when double.IsFinite(number) && number.Equals(Math.Truncate(number)) && number is >= 1 and <= MAX_REFERENCE_DIMENSION
                => source with { ReferenceWidth = (int)number },
            "ReferenceHeightInput" when double.IsFinite(number) && number.Equals(Math.Truncate(number)) && number is >= 1 and <= MAX_REFERENCE_DIMENSION
                => source with { ReferenceHeight = (int)number },
            "MatchWidthOrHeightInput" when double.IsFinite(number) && number is >= 0 and <= 1
                => source with { MatchWidthOrHeight = number },
            "ReferenceWidthInput" or "ReferenceHeightInput" or "MatchWidthOrHeightInput"
                => throw new ArgumentOutOfRangeException(nameof(number)),
            _ => null
        };
        if (settings is null)
        {
            return false;
        }
        source = settings;
        loading = true;
        try
        {
            switch (fieldKey)
            {
                case "ReferenceWidthInput":
                    widthCulture = culture ?? CultureInfo.CurrentCulture;
                    LoadDimension(ReferenceWidth, settings.ReferenceWidth, widthCulture);
                    break;
                case "ReferenceHeightInput":
                    heightCulture = culture ?? CultureInfo.CurrentCulture;
                    LoadDimension(ReferenceHeight, settings.ReferenceHeight, heightCulture);
                    break;
                case "MatchWidthOrHeightInput":
                    MatchWidthOrHeight = settings.MatchWidthOrHeight;
                    break;
            }
        }
        finally
        {
            loading = false;
        }
        RaiseChanged();
        return true;
    }

    private static void LoadDimension(NumericValueDraft field, int number, CultureInfo culture)
    {
        field.Value = number;
        field.RawText = number.ToString(culture);
    }

    private static bool TryReadDimension(NumericValueDraft field, CultureInfo culture, out int number)
    {
        number = 0;
        if (!decimal.TryParse(field.RawText, NumberStyles.Float, culture, out var parsed) ||
            parsed != decimal.Truncate(parsed) || parsed is < 1 or > MAX_REFERENCE_DIMENSION)
        {
            return false;
        }
        number = (int)parsed;
        return true;
    }

    private void FieldChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!loading && args.PropertyName == nameof(NumericValueDraft.RawText))
        {
            RaiseChanged();
        }
    }

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
