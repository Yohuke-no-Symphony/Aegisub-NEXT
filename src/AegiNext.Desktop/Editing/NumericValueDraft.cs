using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Editing;

/// <summary>保留未完成数值输入的纯草稿，不持有界面控件。</summary>
public sealed class NumericValueDraft : ObservableObject
{
    private string rawText = string.Empty;
    private decimal? value;

    /// <summary>在 decimal 无法保留非零双精度值时，保留上次控件投影和权威原文。</summary>
    public bool PreserveDoublePrecision { get; init; }

    public string RawText
    {
        get => rawText;
        set
        {
            if (SetProperty(ref rawText, value))
            {
                if (Parse() is { } number)
                {
                    Value = number;
                }
                else if (rawText.Length == 0)
                {
                    Value = null;
                }
            }
        }
    }

    /// <summary>控件的最后有效数值投影；无效原始文本保留旧投影，显式空文本清空投影。</summary>
    public decimal? Value
    {
        get => value;
        set
        {
            if (value is not null || rawText.Length == 0)
            {
                SetProperty(ref this.value, value);
            }
        }
    }

    /// <summary>以当前界面文化读取原始文本，不将无效内容替换为零。</summary>
    public decimal? Parse()
    {
        if (!decimal.TryParse(rawText, NumberStyles.Float, CultureInfo.CurrentCulture, out var number))
        {
            return null;
        }
        if (PreserveDoublePrecision && number == 0 &&
            double.TryParse(rawText, NumberStyles.Float, CultureInfo.CurrentCulture, out var doubleValue) && doubleValue != 0)
        {
            return null;
        }
        return number;
    }

    /// <summary>从已验证模型载入数值，显式替换上次草稿。</summary>
    public void Load(double number)
    {
        if (PreserveDoublePrecision)
        {
            RawText = number.ToString("R", CultureInfo.CurrentCulture);
            return;
        }
        var loaded = (decimal)number;
        Value = loaded;
        RawText = loaded.ToString(CultureInfo.CurrentCulture);
    }
}
