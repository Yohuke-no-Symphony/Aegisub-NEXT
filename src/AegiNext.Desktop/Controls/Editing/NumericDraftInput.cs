using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

/// <summary>保留未解析数值文本的通用输入控件；验证与事务由功能面板承担。</summary>
public sealed class NumericDraftInput : NumericUpDown
{
    public static readonly StyledProperty<string> RawTextProperty =
        AvaloniaProperty.Register<NumericDraftInput, string>(nameof(RawText), string.Empty, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> PreserveDoublePrecisionProperty =
        AvaloniaProperty.Register<NumericDraftInput, bool>(nameof(PreserveDoublePrecision));
    private bool synchronizing;
    private bool preservingDraft;
    private bool initializingDraft;

    /// <summary>取得标题拖动是否正在更新草稿，供消费者抑制即时事务。</summary>
    public bool IsTitleDragging { get; private set; }

    /// <summary>在基础数值控件初始化格式化期间保持原文为权威输入。</summary>
    public NumericDraftInput()
    {
        SetCurrentValue(ShowButtonSpinnerProperty, false);
        Initialized += (_, _) =>
        {
            try
            {
                if (initializingDraft)
                {
                    SetCurrentValue(TextProperty, RawText);
                }
            }
            finally
            {
                initializingDraft = false;
            }
        };
    }

    /// <summary>取得或设置包含无效和未完成输入的原始草稿。</summary>
    public string RawText
    {
        get => GetValue(RawTextProperty);
        set => SetValue(RawTextProperty, value);
    }

    /// <summary>让有效有限双精度原文在 decimal 控件投影之外仍可编辑和提交。</summary>
    public bool PreserveDoublePrecision
    {
        get => GetValue(PreserveDoublePrecisionProperty);
        set => SetValue(PreserveDoublePrecisionProperty, value);
    }

    /// <summary>将焦点移至模板内的原文输入框，供字段验证定位使用。</summary>
    public bool FocusInput()
    {
        this.BringIntoView();
        ApplyTemplate();
        return this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus() ?? Focus();
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        initializingDraft = IsSet(RawTextProperty);
        base.OnInitialized();
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        var text = RawText;
        var wasPreservingDraft = preservingDraft;
        preservingDraft = true;
        try
        {
            base.OnApplyTemplate(e);
            SetCurrentValue(TextProperty, text);
        }
        finally
        {
            preservingDraft = wasPreservingDraft;
        }
        ResetHorizontalScrollPosition();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowButtonSpinnerProperty && ShowButtonSpinner)
        {
            SetCurrentValue(ShowButtonSpinnerProperty, false);
        }
        if (change.Property == PreserveDoublePrecisionProperty)
        {
            var text = RawText;
            var wasPreservingDraft = preservingDraft;
            preservingDraft = true;
            try
            {
                SetCurrentValue(TextConverterProperty, PreserveDoublePrecision ? new FiniteDoubleDraftConverter(this) : null);
                SetCurrentValue(TextProperty, text);
            }
            finally
            {
                preservingDraft = wasPreservingDraft;
            }
        }
        if (change.Property == RawTextProperty && !synchronizing && !preservingDraft)
        {
            synchronizing = true;
            try
            {
                SetCurrentValue(TextProperty, RawText);
            }
            finally
            {
                synchronizing = false;
            }
            ResetHorizontalScrollPosition();
        }
    }

    /// <inheritdoc />
    protected override void OnTextChanged(string? oldValue, string? newValue)
    {
        if (!synchronizing && !preservingDraft && !initializingDraft)
        {
            synchronizing = true;
            try
            {
                SetCurrentValue(RawTextProperty, newValue ?? string.Empty);
            }
            finally
            {
                synchronizing = false;
            }
        }
        base.OnTextChanged(oldValue, newValue);
    }

    /// <inheritdoc />
    protected override void OnValueChanged(decimal? oldValue, decimal? newValue)
    {
        var text = RawText;
        if (text.Length == 0)
        {
            base.OnValueChanged(oldValue, newValue);
            return;
        }

        var wasPreservingDraft = preservingDraft;
        preservingDraft = true;
        try
        {
            base.OnValueChanged(oldValue, newValue);
            SetCurrentValue(TextProperty, text);
        }
        finally
        {
            preservingDraft = wasPreservingDraft;
        }
    }

    /// <inheritdoc />
    protected override void OnSpin(SpinEventArgs e)
    {
        e.Handled = true;
    }

    internal void BeginTitleDrag() => IsTitleDragging = true;

    internal void EndTitleDrag() => IsTitleDragging = false;

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        var text = RawText;
        var valid = !PreserveDoublePrecision && decimal.TryParse(text, NumberStyles.Float, NumberFormat ?? CultureInfo.CurrentCulture.NumberFormat,
            out var value) && value >= Minimum && value <= Maximum;
        if (valid)
        {
            base.OnLostFocus(e);
            ResetHorizontalScrollPosition();
            return;
        }
        var wasPreservingDraft = preservingDraft;
        preservingDraft = true;
        try
        {
            base.OnLostFocus(e);
            SetCurrentValue(TextProperty, text);
        }
        finally
        {
            preservingDraft = wasPreservingDraft;
        }
        ResetHorizontalScrollPosition();
    }

    private void ResetHorizontalScrollPosition()
    {
        if (IsKeyboardFocusWithin)
        {
            return;
        }
        var scroll = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?
            .GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is not null)
        {
            scroll.Offset = new(0, scroll.Offset.Y);
        }
    }
}
