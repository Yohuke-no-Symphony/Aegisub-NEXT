using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;

namespace AegiNext.Desktop.Controls.Common;

/// <summary>组合标题、值和可选操作区的通用属性行，仅管理工作台视觉与响应式布局。</summary>
[TemplatePart("PART_RowGrid", typeof(Grid))]
[TemplatePart("PART_ActionsPresenter", typeof(ContentPresenter))]
public sealed class PropertyRowControl : HeaderedContentControl
{
    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<PropertyRowControl, object?>(nameof(Actions));
    public static readonly StyledProperty<IDataTemplate?> ActionsTemplateProperty =
        AvaloniaProperty.Register<PropertyRowControl, IDataTemplate?>(nameof(ActionsTemplate));
    public static readonly StyledProperty<bool> IsVectorProperty =
        AvaloniaProperty.Register<PropertyRowControl, bool>(nameof(IsVector));
    public static readonly StyledProperty<bool> IsStackedProperty =
        AvaloniaProperty.Register<PropertyRowControl, bool>(nameof(IsStacked));
    public static readonly StyledProperty<Thickness> SeparatorThicknessProperty =
        AvaloniaProperty.Register<PropertyRowControl, Thickness>(nameof(SeparatorThickness), new(0, 0, 0, 1));

    private Grid? grid;
    private ContentPresenter? valuePresenter;
    private ContentPresenter? actionsPresenter;
    private bool? currentStackedLayout;

    /// <summary>取得或设置可选操作内容；控件内容保持宿主的数据上下文和名称范围。</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <summary>取得或设置操作内容的数据模板。</summary>
    public IDataTemplate? ActionsTemplate
    {
        get => GetValue(ActionsTemplateProperty);
        set => SetValue(ActionsTemplateProperty, value);
    }

    /// <summary>取得或设置值是否为向量；向量值在更宽的阈值下改为上下布局。</summary>
    public bool IsVector
    {
        get => GetValue(IsVectorProperty);
        set => SetValue(IsVectorProperty, value);
    }

    /// <summary>取得或设置是否强制将值放在标题下方；否则按行宽自动选择布局。</summary>
    public bool IsStacked
    {
        get => GetValue(IsStackedProperty);
        set => SetValue(IsStackedProperty, value);
    }

    /// <summary>取得或设置属性行分隔线宽度。</summary>
    public Thickness SeparatorThickness
    {
        get => GetValue(SeparatorThicknessProperty);
        set => SetValue(SeparatorThicknessProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(PropertyRowControl);

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        grid = e.NameScope.Find<Grid>("PART_RowGrid");
        valuePresenter = e.NameScope.Find<ContentPresenter>("PART_ContentPresenter");
        actionsPresenter = e.NameScope.Find<ContentPresenter>("PART_ActionsPresenter");
        currentStackedLayout = null;
        RefreshLayout();
    }

    /// <inheritdoc />
    protected override bool RegisterContentPresenter(ContentPresenter presenter)
    {
        return presenter.Name == "PART_ActionsPresenter" || base.RegisterContentPresenter(presenter);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActionsProperty)
        {
            if (change.OldValue is ILogical oldChild)
            {
                LogicalChildren.Remove(oldChild);
            }
            if (change.NewValue is ILogical newChild)
            {
                LogicalChildren.Add(newChild);
            }
        }
        if (change.Property == BoundsProperty || change.Property == IsVectorProperty || change.Property == IsStackedProperty)
        {
            RefreshLayout();
        }
    }

    private void RefreshLayout()
    {
        if (grid is null || valuePresenter is null || actionsPresenter is null)
        {
            return;
        }
        var stacked = IsStacked || Bounds.Width < 350 || IsVector && Bounds.Width < 520;
        if (currentStackedLayout == stacked)
        {
            return;
        }
        currentStackedLayout = stacked;
        grid.ColumnDefinitions = new(stacked ? "*,Auto" : "108,*,Auto");
        Grid.SetColumn(valuePresenter, stacked ? 0 : 1);
        Grid.SetRow(valuePresenter, stacked ? 1 : 0);
        Grid.SetColumnSpan(valuePresenter, stacked ? 2 : 1);
        Grid.SetColumn(actionsPresenter, stacked ? 1 : 2);
    }
}
