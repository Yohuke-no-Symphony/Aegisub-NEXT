using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Editing;

/// <summary>HEX、sRGB 字节文本及精确线性色共用的纯草稿；未编辑 HDR 分量保持原值。</summary>
public sealed class ColorDraft : ObservableObject
{
    private SceneColor source = SceneColor.White;
    private SceneColor value = SceneColor.White;
    private string hexText = string.Empty;
    private string originalHex = string.Empty;
    private string rgbaText = string.Empty;
    private string originalRgba = string.Empty;
    private ColorInputMode inputMode;
    private readonly Dictionary<string, string> originals = new(StringComparer.Ordinal);
    private readonly HashSet<string> dirtyFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Text, double Value)> loadedNumbers = new(StringComparer.Ordinal);
    private bool loading;
    private bool entireColorDraft;
    private bool alphaEnabled = true;
    private string? invalidFieldKey;
    private int revision;

    /// <summary>创建四个原始数值草稿，不持有控件、工程或存储对象。</summary>
    public ColorDraft(SceneColor? initialValue = null)
    {
        Red.PropertyChanged += (_, args) => NumericChanged("Red", Red, args.PropertyName);
        Green.PropertyChanged += (_, args) => NumericChanged("Green", Green, args.PropertyName);
        Blue.PropertyChanged += (_, args) => NumericChanged("Blue", Blue, args.PropertyName);
        Alpha.PropertyChanged += (_, args) => NumericChanged("Alpha", Alpha, args.PropertyName);
        Load(initialValue ?? SceneColor.White);
    }

    public event EventHandler? Changed;
    public event EventHandler<ColorDraftCommittedEventArgs>? Committed;
    public NumericValueDraft Red { get; } = new();
    public NumericValueDraft Green { get; } = new();
    public NumericValueDraft Blue { get; } = new();
    public NumericValueDraft Alpha { get; } = new();
    public SceneColor Value => value;
    public bool IsDirty => dirtyFields.Count > 0;
    public int Revision => revision;
    public string? InvalidFieldKey => invalidFieldKey;
    public bool HasError => invalidFieldKey is not null;
    public string? Error => invalidFieldKey is null ? null : Localization.Get("Settings." + (invalidFieldKey switch
    {
        "Hex" => "HexValidation",
        "Rgba" => "RgbaValidation",
        _ => "LinearColorValidation"
    }));
    public ColorInputMode InputMode => inputMode;
    public string ModeLabel => inputMode == ColorInputMode.HEX ? "HEX" : "RGBA";
    public string InputPlaceholder => inputMode == ColorInputMode.HEX ? IsAlphaEnabled ? "#RRGGBBAA" : "#RRGGBB" : "255,255,255,255";
    public string InputText
    {
        get => inputMode == ColorInputMode.HEX ? HexText : RgbaText;
        set
        {
            if (inputMode == ColorInputMode.HEX)
            {
                HexText = value;
            }
            else
            {
                RgbaText = value;
            }
        }
    }

    public bool IsAlphaEnabled
    {
        get => alphaEnabled;
        set
        {
            if (SetProperty(ref alphaEnabled, value) && !IsDirty)
            {
                Load(source);
            }
        }
    }

    public string HexText
    {
        get => hexText;
        set
        {
            if (SetProperty(ref hexText, value) && !loading)
            {
                if (string.Equals(value.Trim(), originalHex, StringComparison.OrdinalIgnoreCase))
                {
                    dirtyFields.Remove("Hex");
                }
                else
                {
                    dirtyFields.Add("Hex");
                }

                if (ColorHexCodec.TryParse(value, this.value.Alpha, IsAlphaEnabled, out var parsed))
                {
                    this.value = dirtyFields.Contains("Hex") ? parsed : source;
                    loading = true;
                    LoadNumbers(this.value);
                    RgbaText = ColorRgbaCodec.Format(this.value);
                    loading = false;
                    dirtyFields.RemoveWhere(draftKey => draftKey != "Hex");
                    SetInvalidField(null);
                    OnPropertyChanged(nameof(Value));
                }
                else
                {
                    SetInvalidField("Hex");
                }

                RaiseChanged();
            }
        }
    }

    public string RgbaText
    {
        get => rgbaText;
        set
        {
            if (SetProperty(ref rgbaText, value) && !loading)
            {
                if (value == originalRgba)
                {
                    dirtyFields.Remove("Rgba");
                }
                else
                {
                    dirtyFields.Add("Rgba");
                }

                if (ColorRgbaCodec.TryParse(value, source.Alpha, IsAlphaEnabled, out var parsed))
                {
                    this.value = dirtyFields.Contains("Rgba") ? parsed : source;
                    loading = true;
                    LoadNumbers(this.value);
                    HexText = ColorHexCodec.Format(this.value, IsAlphaEnabled);
                    loading = false;
                    dirtyFields.RemoveWhere(draftKey => draftKey != "Rgba");
                    SetInvalidField(null);
                }
                else
                {
                    SetInvalidField("Rgba");
                }

                RaiseChanged();
            }
        }
    }

    /// <summary>验证后仅切换文本表示；不发布颜色编辑或事务提交事件。</summary>
    public bool TryToggleInputMode()
    {
        if (!TryCommit(out var candidate))
        {
            return false;
        }

        loading = true;
        try
        {
            if (inputMode == ColorInputMode.HEX)
            {
                RgbaText = ColorRgbaCodec.Format(candidate);
                inputMode = ColorInputMode.RGBA;
            }
            else
            {
                HexText = ColorHexCodec.Format(candidate, IsAlphaEnabled);
                inputMode = ColorInputMode.HEX;
            }
        }
        finally
        {
            loading = false;
        }

        OnPropertyChanged(nameof(InputMode));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(InputPlaceholder));
        OnPropertyChanged(nameof(InputText));
        return true;
    }

    /// <summary>回填已提交模型；默认替换草稿，保留模式在草稿未提交时拒绝覆盖。</summary>
    public void Load(SceneColor color, bool discardDraft = true)
    {
        if (!discardDraft && IsDirty)
        {
            return;
        }
        ValidateColor(color);

        loading = true;
        try
        {
            source = color;
            value = color;
            dirtyFields.Clear();
            entireColorDraft = false;
            originalHex = ColorHexCodec.Format(color, IsAlphaEnabled);
            HexText = originalHex;
            originalRgba = ColorRgbaCodec.Format(color);
            RgbaText = originalRgba;
            LoadNumbers(color);
            originals["Red"] = Red.RawText;
            originals["Green"] = Green.RawText;
            originals["Blue"] = Blue.RawText;
            originals["Alpha"] = Alpha.RawText;
            SetInvalidField(null);
        }
        finally
        {
            loading = false;
        }

        NotifyValue();
    }

    /// <summary>在所属面板的事务提交前验证全部原始字段；不发布提交事件或清除草稿。</summary>
    public bool TryCommit(out SceneColor color)
    {
        color = value;
        if (dirtyFields.Contains("Rgba"))
        {
            if (!ColorRgbaCodec.TryParse(RgbaText, source.Alpha, IsAlphaEnabled, out color))
            {
                SetInvalidField("Rgba");
                return false;
            }

            SetInvalidField(null);
            return true;
        }
        if (dirtyFields.Contains("Hex"))
        {
            if (!ColorHexCodec.TryParse(HexText, source.Alpha, IsAlphaEnabled, out color))
            {
                SetInvalidField("Hex");
                return false;
            }

            SetInvalidField(null);
            return true;
        }

        foreach (var item in new (string Key, NumericValueDraft Draft, double Minimum, double Maximum)[]
                 { ("Red", Red, -65504, 65504), ("Green", Green, -65504, 65504), ("Blue", Blue, -65504, 65504), ("Alpha", Alpha, 0, 1) })
        {
            if (!dirtyFields.Contains(item.Key) || item.Key == "Alpha" && !IsAlphaEnabled)
            {
                continue;
            }

            if (item.Draft.Parse() is not { } number || number < (decimal)item.Minimum || number > (decimal)item.Maximum)
            {
                SetInvalidField(item.Key);
                return false;
            }
        }

        color = Candidate();
        SetInvalidField(null);
        return true;
    }

    internal SceneColor ApplyEditsTo(SceneColor target)
    {
        if (!IsDirty)
        {
            return target;
        }
        if (!TryCommit(out var candidate))
        {
            throw new InvalidDataException(Error);
        }
        if (entireColorDraft || dirtyFields.Contains("Hex") || dirtyFields.Contains("Rgba"))
        {
            return IsAlphaEnabled ? candidate : candidate with { Alpha = target.Alpha };
        }
        return target with
        {
            Red = dirtyFields.Contains("Red") ? candidate.Red : target.Red,
            Green = dirtyFields.Contains("Green") ? candidate.Green : target.Green,
            Blue = dirtyFields.Contains("Blue") ? candidate.Blue : target.Blue,
            Alpha = IsAlphaEnabled && dirtyFields.Contains("Alpha") ? candidate.Alpha : target.Alpha
        };
    }

    /// <summary>由用户完成 HEX、选择器或 RGBA 操作时发布一次已验证线性色。</summary>
    public bool TryCommit()
    {
        if (!TryCommit(out var color))
        {
            return false;
        }

        value = color;
        OnPropertyChanged(nameof(Value));
        if (IsDirty)
        {
            Committed?.Invoke(this, new(color));
        }

        return true;
    }

    /// <summary>设置显式有效用户颜色草稿，不自动提交工程；未修改时不发布变化。</summary>
    public void SetValue(SceneColor color)
    {
        ValidateColor(color);
        var next = IsAlphaEnabled ? color : color with { Alpha = value.Alpha };
        if (next == value && !IsDirty)
        {
            return;
        }

        loading = true;
        value = next;
        entireColorDraft = true;
        LoadNumbers(next);
        HexText = ColorHexCodec.Format(next, IsAlphaEnabled);
        RgbaText = ColorRgbaCodec.Format(next);
        loading = false;
        dirtyFields.Clear();
        MarkNumericFields();
        SetInvalidField(null);
        RaiseChanged();
    }

    /// <summary>恢复当前颜色文本或单个线性字段，其余原始草稿继续保留。</summary>
    public void Restore(string fieldKey)
    {
        if (fieldKey is "Hex" or "HexText" or "HexInput" or "Rgba" or "RgbaText" or "ColorInput" or "InputText")
        {
            var restored = fieldKey is "ColorInput" or "InputText"
                ? dirtyFields.Remove("Hex") | dirtyFields.Remove("Rgba")
                : dirtyFields.Remove(fieldKey is "Rgba" or "RgbaText" ? "Rgba" : "Hex");
            if (restored)
            {
                loading = true;
                foreach (var component in new[] { "Red", "Green", "Blue", "Alpha" })
                {
                    if (!dirtyFields.Contains(component))
                    {
                        Field(component)!.RawText = originals[component];
                    }
                }
                loading = false;
                if (TryCommit(out var candidate))
                {
                    value = candidate;
                }
                loading = true;
                HexText = ColorHexCodec.Format(value, IsAlphaEnabled);
                RgbaText = ColorRgbaCodec.Format(value);
                loading = false;
                RaiseChanged();
            }
            return;
        }

        var key = fieldKey.Replace("Input", string.Empty, StringComparison.Ordinal);
        var draft = Field(key);
        if (draft is null || !originals.TryGetValue(key, out var text))
        {
            return;
        }

        draft.RawText = text;
        SetInvalidField(null);
        _ = TryCommit(out _);
    }

    /// <summary>仅刷新验证文案，不改变任何源数值和未完成输入。</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Error));
    }

    private void NumericChanged(string key, NumericValueDraft draft, string? property)
    {
        if (loading || property != nameof(NumericValueDraft.RawText))
        {
            return;
        }

        if (draft.RawText == originals.GetValueOrDefault(key))
        {
            dirtyFields.Remove(key);
        }
        else
        {
            dirtyFields.Add(key);
        }

        if (dirtyFields.Contains("Hex"))
        {
            if (!ColorHexCodec.TryParse(HexText, source.Alpha, IsAlphaEnabled, out _))
            {
                SetInvalidField("Hex");
                RaiseChanged();
                return;
            }

            dirtyFields.Remove("Hex");
            MarkNumericFields();
        }
        if (dirtyFields.Contains("Rgba"))
        {
            if (!ColorRgbaCodec.TryParse(RgbaText, source.Alpha, IsAlphaEnabled, out _))
            {
                SetInvalidField("Rgba");
                RaiseChanged();
                return;
            }

            dirtyFields.Remove("Rgba");
            MarkNumericFields();
        }
        if (TryCommit(out var candidate))
        {
            value = candidate;
            loading = true;
            HexText = ColorHexCodec.Format(value, IsAlphaEnabled);
            RgbaText = ColorRgbaCodec.Format(value);
            loading = false;
            OnPropertyChanged(nameof(Value));
        }

        RaiseChanged();
    }

    private SceneColor Candidate()
    {
        double Read(string key, NumericValueDraft draft, double original)
        {
            if (!dirtyFields.Contains(key))
            {
                return original;
            }
            var loaded = loadedNumbers[key];
            return draft.RawText == loaded.Text ? loaded.Value : (double)draft.Parse()!.Value;
        }
        return new(Read("Red", Red, source.Red), Read("Green", Green, source.Green), Read("Blue", Blue, source.Blue),
            IsAlphaEnabled ? Read("Alpha", Alpha, source.Alpha) : source.Alpha);
    }

    private void MarkNumericFields()
    {
        foreach (var key in new[] { "Red", "Green", "Blue", "Alpha" })
        {
            var draft = Field(key)!;
            var original = Component(source, key);
            var current = loadedNumbers[key];
            var changed = draft.RawText == current.Text ? current.Value != original : draft.RawText != originals[key];
            if (changed && (key != "Alpha" || IsAlphaEnabled))
            {
                dirtyFields.Add(key);
            }
            else
            {
                dirtyFields.Remove(key);
            }
        }
    }

    private NumericValueDraft? Field(string key) => key switch { "Red" => Red, "Green" => Green, "Blue" => Blue, "Alpha" => Alpha, _ => null };

    private void LoadNumbers(SceneColor color)
    {
        Red.Load(color.Red);
        Green.Load(color.Green);
        Blue.Load(color.Blue);
        Alpha.Load(color.Alpha);
        foreach (var key in new[] { "Red", "Green", "Blue", "Alpha" })
        {
            loadedNumbers[key] = (Field(key)!.RawText, Component(color, key));
        }
    }

    private static double Component(SceneColor color, string key) => key switch
    {
        "Red" => color.Red,
        "Green" => color.Green,
        "Blue" => color.Blue,
        "Alpha" => color.Alpha,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private static void ValidateColor(SceneColor color)
    {
        if (!double.IsFinite(color.Red) || !double.IsFinite(color.Green) || !double.IsFinite(color.Blue) ||
            !double.IsFinite(color.Alpha) || color.Red is < -65504 or > 65504 || color.Green is < -65504 or > 65504 ||
            color.Blue is < -65504 or > 65504 || color.Alpha is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(color));
        }
    }

    private void SetInvalidField(string? key)
    {
        invalidFieldKey = key;
        OnPropertyChanged(nameof(InvalidFieldKey));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
    }

    private void RaiseChanged()
    {
        revision++;
        NotifyValue();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyValue()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Revision));
        OnPropertyChanged(nameof(InputText));
        OnPropertyChanged(nameof(InputPlaceholder));
    }
}
