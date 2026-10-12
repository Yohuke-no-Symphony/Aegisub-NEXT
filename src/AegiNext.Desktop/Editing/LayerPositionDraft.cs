using AegiNext.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Editing;

/// <summary>字幕、图片、绘图和模板编辑共用的非拉伸位置草稿，数值验证先于工程或预设事务。</summary>
public class LayerPositionDraft : ObservableObject
{
    private bool isExplicit;
    private bool loading;
    private bool canCustomize = true;
    private LayerPositionDraftGeometry? geometry;
    private LayerPosition source = new();

    /// <summary>创建独立的六项数值草稿，并统一发布位置变化。</summary>
    public LayerPositionDraft()
    {
        foreach (var field in new[] { AnchorX, AnchorY, PivotX, PivotY, OffsetX, OffsetY })
        {
            field.PropertyChanged += (_, _) => RaiseChanged();
        }

        LoadCore(new(), true, true, null);
    }

    public event EventHandler? Changed;
    public NumericValueDraft AnchorX { get; } = new();
    public NumericValueDraft AnchorY { get; } = new();
    public NumericValueDraft PivotX { get; } = new();
    public NumericValueDraft PivotY { get; } = new();
    public NumericValueDraft OffsetX { get; } = new();
    public NumericValueDraft OffsetY { get; } = new();
    public LayerPositionDraftGeometry? Geometry => geometry;
    public bool CanSelectPreset => CanCustomize && geometry is { OffsetScale: > 0 } && double.IsFinite(geometry.OffsetScale);
    public ScenePoint? AnchorSelection => Selection(AnchorX, AnchorY);
    public ScenePoint? PivotSelection => Selection(PivotX, PivotY);
    public LayerPosition? DiagramPosition => !CanCustomize ? null :
        !IsExplicit ? source : Validate() is null ? CreatePosition() : null;
    public bool CanCustomize
    {
        get => canCustomize;
        private set => SetProperty(ref canCustomize, value);
    }

    public bool IsExplicit
    {
        get => isExplicit;
        set
        {
            if (SetProperty(ref isExplicit, value))
            {
                RaiseChanged();
            }
        }
    }

    /// <summary>加载图片或绘图共用的位置和逻辑几何；字段始终可编辑。</summary>
    public void Load(LayerPosition position, LayerPositionDraftGeometry geometry)
    {
        LoadCore(position, true, true, geometry);
    }

    /// <summary>加载已求解的位置，保留宿主的自动布局和可编辑状态。</summary>
    protected void LoadCore(LayerPosition position, bool explicitPosition, bool canCustomize,
        LayerPositionDraftGeometry? geometry)
    {
        loading = true;
        try
        {
            source = position;
            CanCustomize = canCustomize;
            UpdateGeometry(geometry);
            IsExplicit = explicitPosition;
            AnchorX.Load(source.Anchor.X);
            AnchorY.Load(source.Anchor.Y);
            PivotX.Load(source.Pivot.X);
            PivotY.Load(source.Pivot.Y);
            OffsetX.Load(source.Offset.X);
            OffsetY.Load(source.Offset.Y);
            if (!CanCustomize)
            {
                OffsetX.RawText = string.Empty;
                OffsetY.RawText = string.Empty;
            }
        }
        finally
        {
            loading = false;
        }
        NotifySelection();
    }

    /// <summary>更新内容几何而保留数值草稿和未完成输入。</summary>
    public void UpdateGeometry(LayerPositionDraftGeometry? value)
    {
        geometry = value;
        OnPropertyChanged(nameof(Geometry));
        OnPropertyChanged(nameof(CanSelectPreset));
    }

    /// <summary>选择非拉伸锚点；Shift 同步 pivot 并保位，Alt 明确将像素偏移清零。</summary>
    public bool SelectPreset(ScenePoint anchor, bool setPivot = false, bool resetOffset = false)
    {
        if (!CanSelectPreset || anchor.X is < 0 or > 1 || anchor.Y is < 0 or > 1 ||
            !double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y) || Validate() is not null)
        {
            return false;
        }

        var previous = IsExplicit ? CreatePosition()! : source;
        var offset = new ScenePoint(previous.Offset.X + (previous.Anchor.X - anchor.X) * geometry!.ParentSize.X / geometry.OffsetScale,
            previous.Offset.Y + (previous.Anchor.Y - anchor.Y) * geometry.ParentSize.Y / geometry.OffsetScale);
        if (setPivot)
        {
            var displacement = geometry.PivotDisplacement(previous.Pivot, anchor);
            offset = new(offset.X + displacement.X, offset.Y + displacement.Y);
        }
        if (resetOffset)
        {
            offset = new();
        }

        var next = previous with { Anchor = anchor, Pivot = setPivot ? anchor : previous.Pivot, Offset = offset };
        if (next.Offset.X is < -1000000000 or > 1000000000 || next.Offset.Y is < -1000000000 or > 1000000000)
        {
            return false;
        }

        loading = true;
        try
        {
            source = next;
            IsExplicit = true;
            AnchorX.Load(next.Anchor.X);
            AnchorY.Load(next.Anchor.Y);
            PivotX.Load(next.Pivot.X);
            PivotY.Load(next.Pivot.Y);
            OffsetX.Load(next.Offset.X);
            OffsetY.Load(next.Offset.Y);
        }
        finally
        {
            loading = false;
        }
        RaiseChanged();
        return true;
    }

    /// <summary>定位第一项无效数值；自动对齐状态不提交隐藏的显式数值。</summary>
    public string? Validate()
    {
        if (!IsExplicit)
        {
            return null;
        }
        if (!CanCustomize)
        {
            return "CustomPositionMode";
        }

        foreach (var field in new (NumericValueDraft Draft, decimal Minimum, decimal Maximum, string Key)[]
                 {
                     (AnchorX, 0, 1, "AnchorXInput"), (AnchorY, 0, 1, "AnchorYInput"),
                     (PivotX, 0, 1, "PivotXInput"), (PivotY, 0, 1, "PivotYInput"),
                     (OffsetX, -1000000000, 1000000000, "OffsetXInput"),
                     (OffsetY, -1000000000, 1000000000, "OffsetYInput")
                 })
        {
            if (field.Draft.Parse() is not { } number || number < field.Minimum || number > field.Maximum)
            {
                return field.Key;
            }
        }

        return null;
    }

    /// <summary>恢复指定位置分量到最近加载的值，保留其他字段的未完成输入。</summary>
    public bool RestoreField(string fieldKey)
    {
        var field = fieldKey switch
        {
            "AnchorXInput" => (AnchorX, source.Anchor.X),
            "AnchorYInput" => (AnchorY, source.Anchor.Y),
            "PivotXInput" => (PivotX, source.Pivot.X),
            "PivotYInput" => (PivotY, source.Pivot.Y),
            "OffsetXInput" => (OffsetX, source.Offset.X),
            "OffsetYInput" => (OffsetY, source.Offset.Y),
            _ => ((NumericValueDraft?)null, 0d)
        };
        if (field.Item1 is null)
        {
            return false;
        }

        loading = true;
        try
        {
            field.Item1.Load(field.Item2);
        }
        finally
        {
            loading = false;
        }
        RaiseChanged();
        return true;
    }

    /// <summary>创建验证后的持久化位置；未编辑的双精度值保持原值。</summary>
    public virtual LayerPosition? CreatePosition()
    {
        if (!IsExplicit)
        {
            return null;
        }

        if (Validate() is { } key)
        {
            throw new InvalidDataException(key);
        }

        return source with
        {
            Anchor = new(Read(AnchorX, source.Anchor.X), Read(AnchorY, source.Anchor.Y)),
            Pivot = new(Read(PivotX, source.Pivot.X), Read(PivotY, source.Pivot.Y)),
            Offset = new(Read(OffsetX, source.Offset.X), Read(OffsetY, source.Offset.Y))
        };
    }

    private static double Read(NumericValueDraft field, double original)
    {
        var number = field.Parse()!.Value;
        return number == (decimal)original ? original : (double)number;
    }

    private void RaiseChanged()
    {
        if (!loading)
        {
            NotifySelection();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static ScenePoint? Selection(NumericValueDraft x, NumericValueDraft y)
    {
        return x.Parse() is { } horizontal && y.Parse() is { } vertical
            ? new((double)horizontal, (double)vertical)
            : null;
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(AnchorSelection));
        OnPropertyChanged(nameof(PivotSelection));
        OnPropertyChanged(nameof(DiagramPosition));
    }
}
