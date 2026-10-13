using System.Windows.Input;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

/// <summary>通过显式草稿和命令组合动画属性行，编辑目标与事务由宿主协调层管理。</summary>
public sealed partial class AnimationPropertyRowControl : UserControl
{
    public static readonly StyledProperty<NumericValueDraft?> XProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, NumericValueDraft?>(nameof(X), null);
    public static readonly StyledProperty<NumericValueDraft?> YProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, NumericValueDraft?>(nameof(Y), null);
    public static readonly StyledProperty<NumericValueDraft?> ZProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, NumericValueDraft?>(nameof(Z), null);
    public static readonly StyledProperty<ColorDraft?> ColorProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, ColorDraft?>(nameof(Color), null);
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string>(nameof(Label), string.Empty);
    public static readonly StyledProperty<string?> FieldKeyProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string?>(nameof(FieldKey), null);
    public static readonly StyledProperty<string?> XFieldKeyProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string?>(nameof(XFieldKey), null);
    public static readonly StyledProperty<string?> YFieldKeyProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string?>(nameof(YFieldKey), null);
    public static readonly StyledProperty<string?> ZFieldKeyProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string?>(nameof(ZFieldKey), null);
    public static readonly StyledProperty<string> XInputNameProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string>(nameof(XInputName), "RangeValueXInput");
    public static readonly StyledProperty<string> YInputNameProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string>(nameof(YInputName), "RangeValueYInput");
    public static readonly StyledProperty<string> ZInputNameProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string>(nameof(ZInputName), "RangeValueZInput");
    public static readonly StyledProperty<string> ScalarInputNameProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string>(nameof(ScalarInputName), "ScalarInput");
    public static readonly StyledProperty<bool> IsScalarProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, bool>(nameof(IsScalar), false);
    public static readonly StyledProperty<bool> IsVectorProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, bool>(nameof(IsVector), false);
    public static readonly StyledProperty<bool> HasZProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, bool>(nameof(HasZ), false);
    public static readonly StyledProperty<bool> IsColorProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, bool>(nameof(IsColor), false);
    public static readonly StyledProperty<bool> CanEditProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, bool>(nameof(CanEdit), true);
    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, bool>(nameof(IsAnimated), false);
    public static readonly StyledProperty<bool> CanToggleAnimationProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, bool>(nameof(CanToggleAnimation), false);
    public static readonly StyledProperty<bool> CanAddKeyframeProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, bool>(nameof(CanAddKeyframe), false);
    public static readonly StyledProperty<bool> CanResetProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, bool>(nameof(CanReset), false);
    public static readonly StyledProperty<bool> ShowAnimationActionsProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, bool>(nameof(ShowAnimationActions), true);
    public static readonly StyledProperty<bool> IsOrderedProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, bool>(nameof(IsOrdered), false);
    public static readonly StyledProperty<decimal> MinimumProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, decimal>(nameof(Minimum), -1000000000m);
    public static readonly StyledProperty<decimal> MaximumProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, decimal>(nameof(Maximum), 1000000000m);
    public static readonly StyledProperty<decimal> IncrementProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, decimal>(nameof(Increment), 1m);
    public static readonly StyledProperty<string?> EditingHintProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string?>(nameof(EditingHint), null);
    public static readonly StyledProperty<string?> AnimationHintProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string?>(nameof(AnimationHint), null);
    public static readonly StyledProperty<string?> KeyframeHintProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string?>(nameof(KeyframeHint), null);
    public static readonly StyledProperty<string?> ResetHintProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string?>(nameof(ResetHint), null);
    public static readonly StyledProperty<string?> DetailsHintProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string?>(nameof(DetailsHint), null);
    public static readonly StyledProperty<string?> ErrorProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string?>(nameof(Error), null);
    public static readonly StyledProperty<string?> InvalidFieldKeyProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, string?>(nameof(InvalidFieldKey), null);
    public static readonly StyledProperty<ICommand?> SelectCommandProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, ICommand?>(nameof(SelectCommand), null);
    public static readonly StyledProperty<ICommand?> ToggleAnimationCommandProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, ICommand?>(nameof(ToggleAnimationCommand), null);
    public static readonly StyledProperty<ICommand?> AddKeyframeCommandProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, ICommand?>(nameof(AddKeyframeCommand), null);
    public static readonly StyledProperty<ICommand?> ResetCommandProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, ICommand?>(nameof(ResetCommand), null);
    public static readonly StyledProperty<ICommand?> DetailsCommandProperty =
        AvaloniaProperty.Register<AnimationPropertyRowControl, ICommand?>(nameof(DetailsCommand), null);

    private readonly NumericDraftInput scalar;
    private readonly ColorDraftInput color;
    private readonly VectorDraftInput vector;
    private readonly Grid grid;
    private readonly StackPanel values;
    private readonly StackPanel actions;

    /// <summary>创建保留原始输入、支持标题拖拽及窄面板布局的属性行。</summary>
    public AnimationPropertyRowControl()
    {
        AvaloniaXamlLoader.Load(this);
        scalar = this.FindControl<NumericDraftInput>("ScalarInput")!;
        color = this.FindControl<ColorDraftInput>("ColorInput")!;
        vector = this.FindControl<VectorDraftInput>("VectorInput")!;
        grid = this.FindControl<Grid>("RowGrid")!;
        values = this.FindControl<StackPanel>("ValueEditor")!;
        actions = this.FindControl<StackPanel>("RowActions")!;
        SizeChanged += (_, _) => RefreshLayout();
        RefreshEditor();
        RefreshErrors();
    }

    public NumericValueDraft? X
    {
        get => GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    public NumericValueDraft? Y
    {
        get => GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    public NumericValueDraft? Z
    {
        get => GetValue(ZProperty);
        set => SetValue(ZProperty, value);
    }

    public ColorDraft? Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? FieldKey
    {
        get => GetValue(FieldKeyProperty);
        set => SetValue(FieldKeyProperty, value);
    }

    public string? XFieldKey
    {
        get => GetValue(XFieldKeyProperty);
        set => SetValue(XFieldKeyProperty, value);
    }

    public string? YFieldKey
    {
        get => GetValue(YFieldKeyProperty);
        set => SetValue(YFieldKeyProperty, value);
    }

    public string? ZFieldKey
    {
        get => GetValue(ZFieldKeyProperty);
        set => SetValue(ZFieldKeyProperty, value);
    }

    public bool IsScalar
    {
        get => GetValue(IsScalarProperty);
        set => SetValue(IsScalarProperty, value);
    }

    public string XInputName
    {
        get => GetValue(XInputNameProperty);
        set => SetValue(XInputNameProperty, value);
    }

    public string YInputName
    {
        get => GetValue(YInputNameProperty);
        set => SetValue(YInputNameProperty, value);
    }

    public string ZInputName
    {
        get => GetValue(ZInputNameProperty);
        set => SetValue(ZInputNameProperty, value);
    }

    public string ScalarInputName
    {
        get => GetValue(ScalarInputNameProperty);
        set => SetValue(ScalarInputNameProperty, value);
    }

    public bool IsVector
    {
        get => GetValue(IsVectorProperty);
        set => SetValue(IsVectorProperty, value);
    }

    public bool HasZ
    {
        get => GetValue(HasZProperty);
        set => SetValue(HasZProperty, value);
    }

    public bool IsColor
    {
        get => GetValue(IsColorProperty);
        set => SetValue(IsColorProperty, value);
    }

    public bool CanEdit
    {
        get => GetValue(CanEditProperty);
        set => SetValue(CanEditProperty, value);
    }

    public bool IsAnimated
    {
        get => GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    public bool CanToggleAnimation
    {
        get => GetValue(CanToggleAnimationProperty);
        set => SetValue(CanToggleAnimationProperty, value);
    }

    public bool CanAddKeyframe
    {
        get => GetValue(CanAddKeyframeProperty);
        set => SetValue(CanAddKeyframeProperty, value);
    }

    public bool CanReset
    {
        get => GetValue(CanResetProperty);
        set => SetValue(CanResetProperty, value);
    }

    public bool ShowAnimationActions
    {
        get => GetValue(ShowAnimationActionsProperty);
        set => SetValue(ShowAnimationActionsProperty, value);
    }

    public bool IsOrdered
    {
        get => GetValue(IsOrderedProperty);
        set => SetValue(IsOrderedProperty, value);
    }

    public decimal Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public decimal Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public decimal Increment
    {
        get => GetValue(IncrementProperty);
        set => SetValue(IncrementProperty, value);
    }

    public string? EditingHint
    {
        get => GetValue(EditingHintProperty);
        set => SetValue(EditingHintProperty, value);
    }

    public string? AnimationHint
    {
        get => GetValue(AnimationHintProperty);
        set => SetValue(AnimationHintProperty, value);
    }

    public string? KeyframeHint
    {
        get => GetValue(KeyframeHintProperty);
        set => SetValue(KeyframeHintProperty, value);
    }

    public string? ResetHint
    {
        get => GetValue(ResetHintProperty);
        set => SetValue(ResetHintProperty, value);
    }

    public string? DetailsHint
    {
        get => GetValue(DetailsHintProperty);
        set => SetValue(DetailsHintProperty, value);
    }

    public string? Error
    {
        get => GetValue(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    public string? InvalidFieldKey
    {
        get => GetValue(InvalidFieldKeyProperty);
        set => SetValue(InvalidFieldKeyProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public ICommand? ToggleAnimationCommand
    {
        get => GetValue(ToggleAnimationCommandProperty);
        set => SetValue(ToggleAnimationCommandProperty, value);
    }

    public ICommand? AddKeyframeCommand
    {
        get => GetValue(AddKeyframeCommandProperty);
        set => SetValue(AddKeyframeCommandProperty, value);
    }

    public ICommand? ResetCommand
    {
        get => GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

    public ICommand? DetailsCommand
    {
        get => GetValue(DetailsCommandProperty);
        set => SetValue(DetailsCommandProperty, value);
    }

    /// <summary>判断完整属性或分量字段是否属于当前行。</summary>
    public bool OwnsField(string? field)
    {
        return field is not null && (field == FieldKey || field == XFieldKey || field == YFieldKey || HasZ && field == ZFieldKey);
    }

    /// <summary>将焦点定位到当前行内的属性或分量输入。</summary>
    public bool FocusField(string? field)
    {
        if (!OwnsField(field))
        {
            return false;
        }
        if (IsColor)
        {
            return color.TryFocusInvalidField();
        }
        if (IsVector)
        {
            var componentKey = field == YFieldKey ? YFieldKey : HasZ && field == ZFieldKey ? ZFieldKey : XFieldKey;
            return componentKey is not null && vector.FocusField(componentKey);
        }
        return scalar.FocusInput();
    }

    /// <summary>将本地数值输入映射到宿主提供的稳定字段标识，供字段恢复使用。</summary>
    public string? GetInputField(Control source)
    {
        var input = source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault();
        if (input is null || !input.GetVisualAncestors().Contains(this))
        {
            return null;
        }
        return IsVector ? vector.GetInputField(input) : XFieldKey;
    }

    internal NumericDraftInput? FindFieldInput(string? field) =>
        IsVector ? vector.FindFieldInput(field) : field == XFieldKey ? scalar : null;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ErrorProperty || change.Property == InvalidFieldKeyProperty ||
            change.Property == XFieldKeyProperty || change.Property == YFieldKeyProperty || change.Property == ZFieldKeyProperty ||
            change.Property == XInputNameProperty || change.Property == YInputNameProperty || change.Property == ZInputNameProperty ||
            change.Property == HasZProperty)
        {
            RefreshErrors();
        }
        if (change.Property == IsScalarProperty || change.Property == IsVectorProperty || change.Property == IsColorProperty)
        {
            RefreshEditor();
            RefreshLayout();
        }
        if (change.Property == ScalarInputNameProperty && scalar is not null)
        {
            scalar.Name = ScalarInputName;
        }
    }

    private void RefreshErrors()
    {
        if (scalar is null || vector is null)
        {
            return;
        }
        var xError = InvalidFieldKey is not null && InvalidFieldKey == XFieldKey ? Error : null;
        var yError = InvalidFieldKey is not null && InvalidFieldKey == YFieldKey ? Error : null;
        var zError = HasZ && InvalidFieldKey is not null && InvalidFieldKey == ZFieldKey ? Error : null;
        DataValidationErrors.SetErrors(scalar, xError is null ? null : new[] { xError });
        if (XFieldKey is not null)
        {
            vector.SetFieldError(XFieldKey, xError);
        }
        if (YFieldKey is not null)
        {
            vector.SetFieldError(YFieldKey, yError);
        }
        if (ZFieldKey is not null)
        {
            vector.SetFieldError(ZFieldKey, zError);
        }
    }

    private void RefreshLayout()
    {
        if (grid is null)
        {
            return;
        }
        var narrow = Bounds.Width < 350 || IsVector && Bounds.Width < 520;
        grid.ColumnDefinitions = new(narrow ? "*,Auto" : "108,*,Auto");
        Grid.SetColumn(values, narrow ? 0 : 1);
        Grid.SetRow(values, narrow ? 1 : 0);
        Grid.SetColumnSpan(values, narrow ? 2 : 1);
        Grid.SetColumn(actions, narrow ? 1 : 2);
    }

    private void RefreshEditor()
    {
        if (values is null)
        {
            return;
        }
        Control? editor = IsColor ? color : IsVector ? vector : IsScalar ? scalar : null;
        if (editor is null && values.Children.Count == 0 || editor is not null && values.Children.Count == 1 && ReferenceEquals(values.Children[0], editor))
        {
            return;
        }
        values.Children.Clear();
        if (editor is not null)
        {
            values.Children.Add(editor);
        }
    }
}
