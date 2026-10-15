using System.Globalization;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Editing;

/// <summary>保留模板参数的原始输入和强类型候选，不持有工程或控件。</summary>
public sealed class EffectScriptParameterDraft : ObservableObject
{
    private EffectScriptParameterValue original;
    private string rawText = string.Empty;
    private string? error;
    private bool loading;

    /// <summary>以声明及已绑定参数创建一个独立字段草稿。</summary>
    public EffectScriptParameterDraft(EffectScriptParameter definition, EffectScriptParameterValue value)
    {
        Definition = definition;
        original = value;
        foreach (var component in new[] { X, Y, Z })
        {
            component.PropertyChanged += (_, args) =>
            {
                if (!loading && args.PropertyName == nameof(NumericValueDraft.RawText))
                {
                    var parts = HasZ ? new[] { X.RawText, Y.RawText, Z.RawText } : new[] { X.RawText, Y.RawText };
                    RawText = (HasZ ? "vec3(" : "vec2(") + string.Join(", ", parts.Select(NormalizeNumber)) + ")";
                }
            };
        }
        Color.Changed += (_, _) =>
        {
            if (!loading && !Color.HasError)
            {
                RawText = EffectScriptParameterValue.FromAnimation(AnimationValue.FromColor(Color.Value)).ToSource();
            }
        };
        Load(value);
    }

    public event EventHandler? Changed;
    public EffectScriptParameter Definition { get; }
    public string Name => Definition.Name;
    public string Label => Definition.Label ?? Name;
    public bool IsStrictChoice => !Definition.Options.IsEmpty;
    public bool HasZ => Definition.Kind == EffectScriptParameterKind.VECTOR3;
    public bool BooleanValue => RawText == "true";
    public bool IsDirty => RawText != Format(original) || Color.IsDirty;
    public string InvalidFieldKey => Definition.Kind is EffectScriptParameterKind.VECTOR2 or EffectScriptParameterKind.VECTOR3
        ? Name + (!HasValidComponent(X) ? "_x" : !HasValidComponent(Y) ? "_y" : HasZ && !HasValidComponent(Z) ? "_z" : string.Empty) : Name;
    public NumericValueDraft X { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft Y { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft Z { get; } = new() { PreserveDoublePrecision = true };
    public ColorDraft Color { get; } = new();
    public IReadOnlyList<EffectScriptParameterChoice> Choices => IsStrictChoice ? Definition.Options : Definition.Suggestions;
    public EffectScriptParameterChoice? SelectedChoice
    {
        get
        {
            try
            {
                var value = Definition.Kind == EffectScriptParameterKind.STRING
                    ? EffectScriptParameterValue.FromString(RawText) : EffectScriptParameterValue.Parse(
                        Definition.Kind == EffectScriptParameterKind.NUMBER ? NormalizeNumber(RawText) : RawText);
                return Choices.FirstOrDefault(choice => choice.Value == value);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }

    public string RawText
    {
        get => rawText;
        set
        {
            if (SetProperty(ref rawText, value) && !loading)
            {
                OnPropertyChanged(nameof(SelectedChoice));
                OnPropertyChanged(nameof(BooleanValue));
                Error = null;
                OnPropertyChanged(nameof(IsDirty));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string? Error
    {
        get => error;
        private set => SetProperty(ref error, value);
    }

    /// <summary>读取原文并检查参数类型及严格选项约束。</summary>
    public bool TryGetValue(out EffectScriptParameterValue value)
    {
        value = original;
        try
        {
            if (Definition.Kind == EffectScriptParameterKind.COLOR && !Color.TryCommit(out _))
            {
                Error = Color.Error;
                return false;
            }
            value = Definition.Kind == EffectScriptParameterKind.STRING
                ? EffectScriptParameterValue.FromString(RawText)
                : EffectScriptParameterValue.Parse(Definition.Kind == EffectScriptParameterKind.NUMBER ? NormalizeNumber(RawText) : RawText);
            var candidate = value;
            if (value.Kind != Definition.Kind || IsStrictChoice && !Definition.Options.Any(choice => choice.Value == candidate))
            {
                Error = I18n.Localization.Get("Workbench.EffectParameterInvalid");
                return false;
            }
            Error = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException or InvalidDataException or OverflowException)
        {
            Error = exception.Message;
            return false;
        }
    }

    /// <summary>恢复此字段的上次已接受值。</summary>
    public void Restore() => Load(original);

    /// <summary>只恢复向量的一个坐标，保留其他坐标尚未提交的输入。</summary>
    public void RestoreComponent(int component)
    {
        var draft = component switch { 0 => X, 1 => Y, _ => Z };
        draft.Load(original.Animation!.Value.GetComponent(component));
    }

    /// <summary>载入一个明确选择，不改变其他字段。</summary>
    public void Choose(EffectScriptParameterChoice choice)
    {
        LoadProjection(choice.Value);
        RawText = Format(choice.Value);
    }

    /// <summary>接受已验证的事务值。</summary>
    public void Accept(EffectScriptParameterValue value)
    {
        original = value;
        Load(value);
    }

    private void Load(EffectScriptParameterValue value)
    {
        loading = true;
        try
        {
            RawText = Format(value);
            LoadProjection(value);
            Error = null;
        }
        finally
        {
            loading = false;
        }
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(SelectedChoice));
        OnPropertyChanged(nameof(BooleanValue));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void LoadProjection(EffectScriptParameterValue value)
    {
        var wasLoading = loading;
        loading = true;
        try
        {
            if (value.Animation is { } animation)
            {
                if (animation.IsColor)
                {
                    Color.Load(animation.Color);
                }
                else
                {
                    X.Load(animation.GetComponent(0));
                    Y.Load(animation.GetComponent(1));
                    if (animation.IsVector3)
                    {
                        Z.Load(animation.GetComponent(2));
                    }
                }
            }
        }
        finally
        {
            loading = wasLoading;
        }
    }

    private static string Format(EffectScriptParameterValue value) => value.Kind switch
    {
        EffectScriptParameterKind.STRING => value.Text ?? string.Empty,
        EffectScriptParameterKind.NUMBER => value.Number.ToString(CultureInfo.CurrentCulture),
        _ => value.ToSource()
    };

    private static string NormalizeNumber(string value)
    {
        return value.Replace(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, ".", StringComparison.Ordinal);
    }

    private static bool HasValidComponent(NumericValueDraft draft) => double.TryParse(draft.RawText,
        NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && double.IsFinite(value);
}
