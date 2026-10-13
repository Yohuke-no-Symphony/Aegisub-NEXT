using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace AegiNext.Desktop.Controls;

/// <summary>以同一行编辑二维或三维向量，保留各分量的原始草稿；不拥有工程或提交事务。</summary>
public sealed class VectorDraftInput : UserControl
{
    public static readonly StyledProperty<decimal?> xProperty = AvaloniaProperty.Register<VectorDraftInput, decimal?>(nameof(X), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<decimal?> yProperty = AvaloniaProperty.Register<VectorDraftInput, decimal?>(nameof(Y), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<decimal?> zProperty = AvaloniaProperty.Register<VectorDraftInput, decimal?>(nameof(Z), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> xTextProperty = AvaloniaProperty.Register<VectorDraftInput, string>(nameof(XText), string.Empty, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> yTextProperty = AvaloniaProperty.Register<VectorDraftInput, string>(nameof(YText), string.Empty, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> zTextProperty = AvaloniaProperty.Register<VectorDraftInput, string>(nameof(ZText), string.Empty, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> hasZProperty = AvaloniaProperty.Register<VectorDraftInput, bool>(nameof(HasZ));
    public static readonly StyledProperty<bool> preserveDoublePrecisionProperty = AvaloniaProperty.Register<VectorDraftInput, bool>(nameof(PreserveDoublePrecision));
    public static readonly StyledProperty<bool> xIsEnabledProperty = AvaloniaProperty.Register<VectorDraftInput, bool>(nameof(XIsEnabled), true);
    public static readonly StyledProperty<bool> yIsEnabledProperty = AvaloniaProperty.Register<VectorDraftInput, bool>(nameof(YIsEnabled), true);
    public static readonly StyledProperty<bool> zIsEnabledProperty = AvaloniaProperty.Register<VectorDraftInput, bool>(nameof(ZIsEnabled), true);
    public static readonly StyledProperty<string?> xHintProperty = AvaloniaProperty.Register<VectorDraftInput, string?>(nameof(XHint));
    public static readonly StyledProperty<string?> yHintProperty = AvaloniaProperty.Register<VectorDraftInput, string?>(nameof(YHint));
    public static readonly StyledProperty<string?> zHintProperty = AvaloniaProperty.Register<VectorDraftInput, string?>(nameof(ZHint));
    public static readonly StyledProperty<decimal> minimumProperty = AvaloniaProperty.Register<VectorDraftInput, decimal>(nameof(Minimum), -1000000000m);
    public static readonly StyledProperty<decimal> maximumProperty = AvaloniaProperty.Register<VectorDraftInput, decimal>(nameof(Maximum), 1000000000m);
    public static readonly StyledProperty<decimal> incrementProperty = AvaloniaProperty.Register<VectorDraftInput, decimal>(nameof(Increment), 1m);
    public static readonly StyledProperty<string?> xFieldKeyProperty = AvaloniaProperty.Register<VectorDraftInput, string?>(nameof(XFieldKey));
    public static readonly StyledProperty<string?> yFieldKeyProperty = AvaloniaProperty.Register<VectorDraftInput, string?>(nameof(YFieldKey));
    public static readonly StyledProperty<string?> zFieldKeyProperty = AvaloniaProperty.Register<VectorDraftInput, string?>(nameof(ZFieldKey));
    public static readonly StyledProperty<string?> xInputNameProperty = AvaloniaProperty.Register<VectorDraftInput, string?>(nameof(XInputName));
    public static readonly StyledProperty<string?> yInputNameProperty = AvaloniaProperty.Register<VectorDraftInput, string?>(nameof(YInputName));
    public static readonly StyledProperty<string?> zInputNameProperty = AvaloniaProperty.Register<VectorDraftInput, string?>(nameof(ZInputName));
    private readonly NumericDraftInput xInput;
    private readonly NumericDraftInput yInput;
    private readonly NumericDraftInput zInput;
    private readonly NumericDragLabel zLabel;
    private readonly Grid grid;
    private readonly Dictionary<AvaloniaProperty, (BindingBase Binding, BindingExpressionBase Expression)> bindings = new();
    private bool xInputNameFrozen;
    private bool yInputNameFrozen;
    private bool zInputNameFrozen;

    /// <summary>创建共用验证范围的 X/Y 输入，按需显示 Z 分量。</summary>
    public VectorDraftInput()
    {
        grid = new Grid { ColumnDefinitions = new("Auto,*,Auto,*"), ColumnSpacing = 6 };
        xInput = CreateInput(nameof(X), nameof(XText), nameof(XIsEnabled), nameof(XHint));
        xInput.AttachedToLogicalTree += (_, _) => xInputNameFrozen = true;
        grid.Children.Add(new NumericDragLabel { Text = "X", Input = xInput, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        Grid.SetColumn(xInput, 1);
        grid.Children.Add(xInput);
        var yLabel = new NumericDragLabel { Text = "Y", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Grid.SetColumn(yLabel, 2);
        grid.Children.Add(yLabel);
        yInput = CreateInput(nameof(Y), nameof(YText), nameof(YIsEnabled), nameof(YHint));
        yInput.AttachedToLogicalTree += (_, _) => yInputNameFrozen = true;
        yLabel.Input = yInput;
        Grid.SetColumn(yInput, 3);
        grid.Children.Add(yInput);
        zInput = CreateInput(nameof(Z), nameof(ZText), nameof(ZIsEnabled), nameof(ZHint));
        zInput.AttachedToLogicalTree += (_, _) => zInputNameFrozen = true;
        zLabel = new() { Text = "Z", Input = zInput, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Grid.SetColumn(zLabel, 4);
        Grid.SetColumn(zInput, 5);
        Content = grid;
    }

    public decimal? X { get => GetValue(xProperty); set => SetValue(xProperty, value); }
    public decimal? Y { get => GetValue(yProperty); set => SetValue(yProperty, value); }
    public decimal? Z { get => GetValue(zProperty); set => SetValue(zProperty, value); }
    public string XText { get => GetValue(xTextProperty); set => SetValue(xTextProperty, value); }
    public string YText { get => GetValue(yTextProperty); set => SetValue(yTextProperty, value); }
    public string ZText { get => GetValue(zTextProperty); set => SetValue(zTextProperty, value); }
    public bool HasZ { get => GetValue(hasZProperty); set => SetValue(hasZProperty, value); }
    public bool PreserveDoublePrecision { get => GetValue(preserveDoublePrecisionProperty); set => SetValue(preserveDoublePrecisionProperty, value); }
    public bool XIsEnabled { get => GetValue(xIsEnabledProperty); set => SetValue(xIsEnabledProperty, value); }
    public bool YIsEnabled { get => GetValue(yIsEnabledProperty); set => SetValue(yIsEnabledProperty, value); }
    public bool ZIsEnabled { get => GetValue(zIsEnabledProperty); set => SetValue(zIsEnabledProperty, value); }
    public string? XHint { get => GetValue(xHintProperty); set => SetValue(xHintProperty, value); }
    public string? YHint { get => GetValue(yHintProperty); set => SetValue(yHintProperty, value); }
    public string? ZHint { get => GetValue(zHintProperty); set => SetValue(zHintProperty, value); }
    public decimal Minimum { get => GetValue(minimumProperty); set => SetValue(minimumProperty, value); }
    public decimal Maximum { get => GetValue(maximumProperty); set => SetValue(maximumProperty, value); }
    public decimal Increment { get => GetValue(incrementProperty); set => SetValue(incrementProperty, value); }

    /// <summary>绑定可选的第三分量显示状态。</summary>
    [AssignBinding]
    public BindingBase? HasZBinding
    {
        get => GetBinding(hasZProperty);
        set => SetBinding(hasZProperty, value);
    }
    /// <summary>绑定分量输入保留双精度原始草稿的状态。</summary>
    [AssignBinding]
    public BindingBase? PreserveDoublePrecisionBinding
    {
        get => GetBinding(preserveDoublePrecisionProperty);
        set => SetBinding(preserveDoublePrecisionProperty, value);
    }
    /// <summary>接收 X 数值的绑定；清空时只释放此入口持有的绑定。</summary>
    [AssignBinding]
    public BindingBase? XBinding
    {
        get => GetBinding(xProperty);
        set => SetBinding(xProperty, value);
    }

    /// <summary>接收 Y 数值的绑定；保留注册属性的默认绑定模式。</summary>
    [AssignBinding]
    public BindingBase? YBinding
    {
        get => GetBinding(yProperty);
        set => SetBinding(yProperty, value);
    }

    /// <summary>接收可选 Z 数值的绑定。</summary>
    [AssignBinding]
    public BindingBase? ZBinding
    {
        get => GetBinding(zProperty);
        set => SetBinding(zProperty, value);
    }

    /// <summary>接收 X 原始草稿的绑定，保留未解析和无效文本。</summary>
    [AssignBinding]
    public BindingBase? XTextBinding
    {
        get => GetBinding(xTextProperty);
        set => SetBinding(xTextProperty, value);
    }

    /// <summary>接收 Y 原始草稿的绑定，保留未解析和无效文本。</summary>
    [AssignBinding]
    public BindingBase? YTextBinding
    {
        get => GetBinding(yTextProperty);
        set => SetBinding(yTextProperty, value);
    }

    /// <summary>接收 Z 原始草稿的绑定，保留未解析和无效文本。</summary>
    [AssignBinding]
    public BindingBase? ZTextBinding
    {
        get => GetBinding(zTextProperty);
        set => SetBinding(zTextProperty, value);
    }

    /// <summary>接收各分量共用的最小值绑定。</summary>
    [AssignBinding]
    public BindingBase? MinimumBinding
    {
        get => GetBinding(minimumProperty);
        set => SetBinding(minimumProperty, value);
    }

    /// <summary>接收各分量共用的最大值绑定。</summary>
    [AssignBinding]
    public BindingBase? MaximumBinding
    {
        get => GetBinding(maximumProperty);
        set => SetBinding(maximumProperty, value);
    }

    /// <summary>接收各分量共用的拖动步长绑定。</summary>
    [AssignBinding]
    public BindingBase? IncrementBinding
    {
        get => GetBinding(incrementProperty);
        set => SetBinding(incrementProperty, value);
    }

    /// <summary>接收 X 分量的稳定字段身份绑定。</summary>
    [AssignBinding]
    public BindingBase? XFieldKeyBinding
    {
        get => GetBinding(xFieldKeyProperty);
        set => SetBinding(xFieldKeyProperty, value);
    }

    /// <summary>接收 Y 分量的稳定字段身份绑定。</summary>
    [AssignBinding]
    public BindingBase? YFieldKeyBinding
    {
        get => GetBinding(yFieldKeyProperty);
        set => SetBinding(yFieldKeyProperty, value);
    }

    /// <summary>接收 Z 分量的稳定字段身份绑定。</summary>
    [AssignBinding]
    public BindingBase? ZFieldKeyBinding
    {
        get => GetBinding(zFieldKeyProperty);
        set => SetBinding(zFieldKeyProperty, value);
    }

    /// <summary>接收 X 分量输入控件的名称绑定。</summary>
    [AssignBinding]
    public BindingBase? XInputNameBinding
    {
        get => GetBinding(xInputNameProperty);
        set => SetBinding(xInputNameProperty, value);
    }

    /// <summary>接收 Y 分量输入控件的名称绑定。</summary>
    [AssignBinding]
    public BindingBase? YInputNameBinding
    {
        get => GetBinding(yInputNameProperty);
        set => SetBinding(yInputNameProperty, value);
    }

    /// <summary>接收 Z 分量输入控件的名称绑定。</summary>
    [AssignBinding]
    public BindingBase? ZInputNameBinding
    {
        get => GetBinding(zInputNameProperty);
        set => SetBinding(zInputNameProperty, value);
    }

    public string? XFieldKey
    {
        get => GetValue(xFieldKeyProperty);
        set => SetValue(xFieldKeyProperty, value);
    }
    public string? YFieldKey
    {
        get => GetValue(yFieldKeyProperty);
        set => SetValue(yFieldKeyProperty, value);
    }
    public string? ZFieldKey
    {
        get => GetValue(zFieldKeyProperty);
        set => SetValue(zFieldKeyProperty, value);
    }
    public string? XInputName
    {
        get => GetValue(xInputNameProperty);
        set => SetValue(xInputNameProperty, value);
    }
    public string? YInputName
    {
        get => GetValue(yInputNameProperty);
        set => SetValue(yInputNameProperty, value);
    }
    public string? ZInputName
    {
        get => GetValue(zInputNameProperty);
        set => SetValue(zInputNameProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (!xInputNameFrozen && (change.Property == xFieldKeyProperty || change.Property == xInputNameProperty))
        {
            xInput.Name = XInputName ?? XFieldKey;
        }
        else if (!yInputNameFrozen && (change.Property == yFieldKeyProperty || change.Property == yInputNameProperty))
        {
            yInput.Name = YInputName ?? YFieldKey;
        }
        else if (!zInputNameFrozen && (change.Property == zFieldKeyProperty || change.Property == zInputNameProperty))
        {
            zInput.Name = ZInputName ?? ZFieldKey;
        }
        if (change.Property == hasZProperty && grid is not null)
        {
            grid.ColumnDefinitions = new(HasZ ? "Auto,*,Auto,*,Auto,*" : "Auto,*,Auto,*");
            if (HasZ)
            {
                grid.Children.Add(zLabel);
                grid.Children.Add(zInput);
            }
            else
            {
                zLabel.CancelDrag();
                grid.Children.Remove(zLabel);
                grid.Children.Remove(zInput);
            }
        }
    }

    /// <summary>按稳定分量字段标识定位输入；不依赖宿主 XAML 的名称范围。</summary>
    public bool FocusField(string fieldKey)
    {
        var input = FindFieldInput(fieldKey);
        if (input is null)
        {
            return false;
        }

        return input.FocusInput();
    }

    internal void SetFieldError(string fieldKey, string? error)
    {
        var input = FindFieldInput(fieldKey);
        if (input is not null)
        {
            DataValidationErrors.SetErrors(input, error is null ? null : new[] { error });
        }
    }

    /// <summary>返回本控件数值输入对应的稳定字段身份。</summary>
    public string? GetInputField(NumericDraftInput input)
    {
        return ReferenceEquals(input, xInput) ? XFieldKey : ReferenceEquals(input, yInput) ? YFieldKey :
            HasZ && ReferenceEquals(input, zInput) ? ZFieldKey : null;
    }

    internal NumericDraftInput? FindFieldInput(string? fieldKey)
    {
        return fieldKey is null ? null : fieldKey == XFieldKey ? xInput : fieldKey == YFieldKey ? yInput :
            HasZ && fieldKey == ZFieldKey ? zInput : null;
    }

    private BindingBase? GetBinding(AvaloniaProperty property)
    {
        VerifyAccess();
        return bindings.TryGetValue(property, out var current) ? current.Binding : null;
    }

    private void SetBinding(AvaloniaProperty property, BindingBase? binding)
    {
        VerifyAccess();
        var hasPrevious = bindings.TryGetValue(property, out var previous);
        if (hasPrevious && ReferenceEquals(previous.Binding, binding))
        {
            return;
        }

        if (binding is null)
        {
            if (hasPrevious)
            {
                bindings.Remove(property);
                previous.Expression.Dispose();
            }

            return;
        }

        var expression = Bind(property, binding);
        bindings[property] = (binding, expression);
        if (hasPrevious)
        {
            previous.Expression.Dispose();
        }
    }

    private NumericDraftInput CreateInput(string valueProperty, string textProperty, string enabledProperty, string hintProperty)
    {
        var input = new NumericDraftInput();
        input.Bind(NumericUpDown.ValueProperty, new Binding(valueProperty) { Source = this, Mode = BindingMode.TwoWay });
        input.Bind(NumericDraftInput.RawTextProperty, new Binding(textProperty) { Source = this, Mode = BindingMode.TwoWay });
        input.Bind(NumericUpDown.MinimumProperty, new Binding(nameof(Minimum)) { Source = this });
        input.Bind(NumericUpDown.MaximumProperty, new Binding(nameof(Maximum)) { Source = this });
        input.Bind(NumericUpDown.IncrementProperty, new Binding(nameof(Increment)) { Source = this });
        input.Bind(IsEnabledProperty, new Binding(enabledProperty) { Source = this });
        input.Bind(ToolTip.TipProperty, new Binding(hintProperty) { Source = this });
        input.Bind(NumericDraftInput.PreserveDoublePrecisionProperty, new Binding(nameof(PreserveDoublePrecision)) { Source = this });
        return input;
    }
}
