using AegiNext.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;

namespace AegiNext.Desktop.Editing;

/// <summary>图层、蒙版和模板编辑共用的非拉伸位置草稿，数值验证先于工程或预设事务。</summary>
public class LayerPositionDraft : ObservableObject
{
    private bool isExplicit;
    private bool loading;
    private bool canCustomize = true;
    private LayerPositionDraftGeometry? geometry;
    private LayerPosition source = new();
    private ScenePoint loadedPivot;
    private ScenePoint compensatedPivot;
    private SceneVector3 loadedProjectionTranslation;
    private readonly List<LayerPositionDraftEdit> edits = [];
    private readonly HashSet<string> editedFields = new(StringComparer.Ordinal);

    /// <summary>创建独立的六项数值草稿，并统一发布位置变化。</summary>
    public LayerPositionDraft()
    {
        foreach (var field in new (string Key, NumericValueDraft Draft)[]
                 {
                     ("AnchorXInput", AnchorX), ("AnchorYInput", AnchorY),
                     ("PivotXInput", PivotX), ("PivotYInput", PivotY),
                     ("OffsetXInput", OffsetX), ("OffsetYInput", OffsetY)
                 })
        {
            field.Draft.PropertyChanged += (_, args) =>
            {
                if (!loading && args.PropertyName == nameof(NumericValueDraft.RawText))
                {
                    RecordFieldEdit(field.Key, field.Draft.RawText);
                    CompensateAffinePivot();
                    RaiseChanged();
                }
            };
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
    public bool HasEdits => IsExplicit && editedFields.Count > 0 ||
        edits.Any(edit => edit is LayerPositionPresetEdit or LayerPositionModeEdit);
    /// <summary>允许编辑的归一化轴心下界；普通图层使用零，蒙版可保留框外轴心。</summary>
    public decimal PivotMinimum { get; init; }
    /// <summary>允许编辑的归一化轴心上界；普通图层使用一。</summary>
    public decimal PivotMaximum { get; init; } = 1;
    public LayerPositionDraftGeometry? Geometry => geometry;
    public LayerPositionDraftGeometry? DiagramGeometry
    {
        get
        {
            if (geometry is null || !IsExplicit || Validate() is not null)
            {
                return geometry;
            }
            var transform = geometry.Transform with
            {
                ProjectionTranslation = loadedProjectionTranslation + ProjectionTranslationDelta
            };
            return transform == geometry.Transform ? geometry : geometry with { Transform = transform };
        }
    }

    /// <summary>以当前编辑姿态计算轴心改变所需的静态三维补偿，不修改任何动画轨道。</summary>
    public SceneVector3 ProjectionTranslationDelta => IsExplicit && geometry is not null &&
        SceneProjection.RequiresPerspective(geometry.Transform) && Validate() is null
        ? SceneProjection.RotateScaled(new((Read(PivotX, source.Pivot.X) - loadedPivot.X) * geometry.GlyphSize.X,
            (Read(PivotY, source.Pivot.Y) - loadedPivot.Y) * geometry.GlyphSize.Y), geometry.Transform)
        : default;
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
                if (!loading)
                {
                    if (edits.LastOrDefault() is LayerPositionModeEdit)
                    {
                        edits[^1] = new LayerPositionModeEdit(value);
                    }
                    else
                    {
                        edits.Add(new LayerPositionModeEdit(value));
                    }
                }
                RaiseChanged();
            }
        }
    }

    /// <summary>加载图层或蒙版的位置和逻辑几何；字段始终可编辑。</summary>
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
            loadedPivot = position.Pivot;
            compensatedPivot = position.Pivot;
            loadedProjectionTranslation = geometry?.Transform.ProjectionTranslation ?? default;
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
        ClearEdits();
        NotifySelection();
    }

    /// <summary>在已载入自身位置和几何的目标草稿上重放用户操作，由目标独立计算保位补偿。</summary>
    public void ApplyEditsTo(LayerPositionDraft target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(this, target))
        {
            throw new ArgumentException("位置编辑必须应用到另一个草稿。", nameof(target));
        }
        if (Validate() is { } sourceField)
        {
            throw new InvalidDataException(sourceField);
        }
        if (!HasEdits)
        {
            return;
        }
        if (!IsExplicit)
        {
            target.IsExplicit = false;
            return;
        }

        target.IsExplicit = true;
        foreach (var edit in edits)
        {
            edit.ApplyTo(target);
        }
        if (target.Validate() is { } targetField)
        {
            throw new InvalidDataException(targetField);
        }
    }

    /// <summary>清除待重放的用户操作；宿主接受或取消整份草稿时调用，不改变当前输入。</summary>
    public void ClearEdits()
    {
        if (edits.Count == 0 && editedFields.Count == 0)
        {
            return;
        }
        edits.Clear();
        editedFields.Clear();
        OnPropertyChanged(nameof(HasEdits));
    }

    /// <summary>捕获当前目标的原始输入及操作意图，取消手势时完整恢复并只发布一次位置变化。</summary>
    public Action CaptureEditState()
    {
        var fields = new[] { AnchorX, AnchorY, PivotX, PivotY, OffsetX, OffsetY };
        var numbers = fields.Select(field => (field.RawText, field.Value)).ToArray();
        var position = source;
        var explicitPosition = isExplicit;
        var customizable = canCustomize;
        var originalGeometry = geometry;
        var originalLoadedPivot = loadedPivot;
        var originalCompensatedPivot = compensatedPivot;
        var originalProjectionTranslation = loadedProjectionTranslation;
        var originalEdits = edits.ToArray();
        var originalEditedFields = editedFields.ToArray();
        return () =>
        {
            loading = true;
            try
            {
                source = position;
                loadedPivot = originalLoadedPivot;
                compensatedPivot = originalCompensatedPivot;
                loadedProjectionTranslation = originalProjectionTranslation;
                CanCustomize = customizable;
                IsExplicit = explicitPosition;
                for (var index = 0; index < fields.Length; index++)
                {
                    var field = fields[index];
                    var number = numbers[index];
                    if (number.Value is null && field.RawText.Length > 0)
                    {
                        field.RawText = string.Empty;
                    }
                    field.Value = number.Value;
                    field.RawText = number.RawText;
                }
                edits.Clear();
                edits.AddRange(originalEdits);
                editedFields.Clear();
                editedFields.UnionWith(originalEditedFields);
                UpdateGeometry(originalGeometry);
            }
            finally
            {
                loading = false;
            }
            RaiseChanged();
        };
    }

    /// <summary>更新内容几何而保留数值草稿和未完成输入。</summary>
    public void UpdateGeometry(LayerPositionDraftGeometry? value)
    {
        geometry = value;
        OnPropertyChanged(nameof(Geometry));
        OnPropertyChanged(nameof(DiagramGeometry));
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
        if (setPivot && !SceneProjection.RequiresPerspective(geometry.Transform))
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
            compensatedPivot = next.Pivot;
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
        CompensateAffinePivot();
        edits.Add(new LayerPositionPresetEdit(anchor, setPivot, resetOffset));
        editedFields.Clear();
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
                     (PivotX, PivotMinimum, PivotMaximum, "PivotXInput"),
                     (PivotY, PivotMinimum, PivotMaximum, "PivotYInput"),
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
        CompensateAffinePivot();
        editedFields.Remove(fieldKey);
        if (editedFields.Count > 0 || edits.Any(edit => edit is LayerPositionPresetEdit or LayerPositionModeEdit))
        {
            edits.Add(new LayerPositionFieldEdit(fieldKey, null));
        }
        else
        {
            edits.Clear();
        }
        RaiseChanged();
        return true;
    }

    internal void ApplyFieldText(string fieldKey, string rawText)
    {
        var field = fieldKey switch
        {
            "AnchorXInput" => AnchorX,
            "AnchorYInput" => AnchorY,
            "PivotXInput" => PivotX,
            "PivotYInput" => PivotY,
            "OffsetXInput" => OffsetX,
            "OffsetYInput" => OffsetY,
            _ => throw new ArgumentException("未知的位置字段。", nameof(fieldKey))
        };
        field.RawText = rawText;
    }

    private void RecordFieldEdit(string fieldKey, string rawText)
    {
        editedFields.Add(fieldKey);
        var edit = new LayerPositionFieldEdit(fieldKey, rawText);
        if (edits.LastOrDefault() is LayerPositionFieldEdit { RawText: not null } previous && previous.FieldKey == fieldKey)
        {
            edits[^1] = edit;
        }
        else
        {
            edits.Add(edit);
        }
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

    /// <summary>将验证后的轴心补偿应用到工程基础变换；重复准备不会累加草稿补偿。</summary>
    public LayerTransform PrepareTransform(LayerTransform transform)
    {
        if (Validate() is { } key)
        {
            throw new InvalidDataException(key);
        }
        if (!IsExplicit || geometry is null || !SceneProjection.RequiresPerspective(geometry.Transform))
        {
            return transform;
        }
        var translation = loadedProjectionTranslation + ProjectionTranslationDelta;
        return translation == transform.ProjectionTranslation ? transform : transform with { ProjectionTranslation = translation };
    }

    private void CompensateAffinePivot()
    {
        if (loading || !IsExplicit || geometry is null || PivotX.Parse() is not { } x || PivotY.Parse() is not { } y ||
            x < PivotMinimum || x > PivotMaximum || y < PivotMinimum || y > PivotMaximum ||
            OffsetX.Parse() is not { } offsetX || OffsetY.Parse() is not { } offsetY)
        {
            return;
        }
        var next = new ScenePoint((double)x, (double)y);
        if (next == compensatedPivot)
        {
            return;
        }
        if (!SceneProjection.RequiresPerspective(geometry.Transform))
        {
            var displacement = geometry.PivotDisplacement(compensatedPivot, next);
            var offset = new ScenePoint((double)offsetX + displacement.X, (double)offsetY + displacement.Y);
            loading = true;
            try
            {
                OffsetX.RawText = offset.X.ToString("R", CultureInfo.CurrentCulture);
                OffsetY.RawText = offset.Y.ToString("R", CultureInfo.CurrentCulture);
            }
            finally
            {
                loading = false;
            }
        }
        compensatedPivot = next;
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
            OnPropertyChanged(nameof(HasEdits));
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
        OnPropertyChanged(nameof(DiagramGeometry));
        OnPropertyChanged(nameof(ProjectionTranslationDelta));
    }
}
