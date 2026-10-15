using System.Windows.Input;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelViewModel
{
    private EffectScopeChoice[] scopes = [];
    private EffectStateChoice[] states = [];
    private EffectScopeChoice? notifiedScope;
    private EffectStateChoice? notifiedState;
    private bool refreshingScopes;
    private AnimationPropertyRowViewModel[] rotationRows = [];
    private AnimationPropertyRowViewModel[] perspectiveRows = [];
    private AnimationPropertyRowViewModel[] typographyRows = [];
    private AnimationPropertyRowViewModel[] fillRows = [];
    private AnimationPropertyRowViewModel[] strokeRows = [];
    private AnimationPropertyRowViewModel[] shadowRows = [];
    private AnimationPropertyRowViewModel[] pathRows = [];

    public EffectScopeChoice[] Scopes { get => scopes; private set => SetItems(ref scopes, value, nameof(Scopes)); }
    public EffectStateChoice[] States { get => states; private set => SetItems(ref states, value, nameof(States)); }
    public EffectScopeChoice? SelectedScope
    {
        get => Scopes.FirstOrDefault(scope => scope.Id == Target.TextRangeId);
        set
        {
            if (refreshingScopes || value is null)
            {
                return;
            }
            var next = Target with { TextRangeId = value.Id, NodeId = null };
            if (value.Id is not null && !IsRangeProperty(next.Property))
            {
                next = next with { Property = AnimationProperty.FONT_SIZE };
            }
            Target = next;
        }
    }
    public EffectStateChoice? SelectedState
    {
        get => States.FirstOrDefault(state => state.State == Target.State);
        set
        {
            if (refreshingScopes || value is null)
            {
                return;
            }
            var next = Target with { State = value.State, NodeId = null };
            if (value.State != SubtitleAnimationState.NORMAL && !IsAppearanceProperty(next.Property))
            {
                next = next with { Property = AnimationProperty.FILL };
            }
            Target = next;
        }
    }
    public bool CanEditTextScope => SelectedLayer?.SubtitleId is not null;
    public bool CanManageRange => Target.TextRangeId is not null;
    public bool CanCreateRange => CanEditTextScope && session.Details.TextSelectionLength > 0;
    public bool IsWholeLayerTarget => Target.TextRangeId is null && Target.State == SubtitleAnimationState.NORMAL;
    public bool CanEditTargetTransform => Target.State == SubtitleAnimationState.NORMAL;
    public string PositionLabel => Localization.Get(Target.TextRangeId is null ? "Workbench.PositionVector" : "Workbench.LocalPositionVector");
    public string? PositionEditingHint => HasOrderedPosition ? Localization.Get("Workbench.StyleOrderedTransformHint") :
        Target.TextRangeId is null ? null : Localization.Get("Workbench.LocalPositionHint");
    private bool HasOrderedPosition => SelectedLayer?.Tracks.Any(track =>
        track.Target == Target with { Property = AnimationProperty.POSITION, NodeId = null } && track.IsOrdered) == true;
    public AnimationPropertyRowViewModel[] RotationRows { get => rotationRows; private set => SetItems(ref rotationRows, value, nameof(RotationRows)); }
    public AnimationPropertyRowViewModel? RotationRow => RotationRows.FirstOrDefault();
    public AnimationPropertyRowViewModel[] PerspectiveRows { get => perspectiveRows; private set => SetItems(ref perspectiveRows, value, nameof(PerspectiveRows)); }
    public AnimationPropertyRowViewModel[] TypographyRows { get => typographyRows; private set => SetItems(ref typographyRows, value, nameof(TypographyRows)); }
    public AnimationPropertyRowViewModel[] FillRows { get => fillRows; private set => SetItems(ref fillRows, value, nameof(FillRows)); }
    public AnimationPropertyRowViewModel[] StrokeRows { get => strokeRows; private set => SetItems(ref strokeRows, value, nameof(StrokeRows)); }
    public AnimationPropertyRowViewModel[] ShadowRows { get => shadowRows; private set => SetItems(ref shadowRows, value, nameof(ShadowRows)); }
    public AnimationPropertyRowViewModel[] PathRows { get => pathRows; private set => SetItems(ref pathRows, value, nameof(PathRows)); }
    public ICommand CreateRangeCommand { get; private set; } = null!;
    public ICommand DeleteRangeCommand { get; private set; } = null!;
    public ICommand AnimationOptionsPropertyCommand { get; private set; } = null!;
    public ICommand AddPropertyKeyframeCommand { get; private set; } = null!;
    public ICommand ResetPropertyCommand { get; private set; } = null!;
    public bool CanResetPositionAnimation => IsPositionAnimated && CanEditPosition;
    public bool CanResetScaleAnimation => IsScaleAnimated && CanEditScale;
    public bool CanResetRotationAnimation => IsRotationAnimated && CanEditRotation;
    public bool CanResetOpacityAnimation => IsOpacityAnimated && CanEditOpacity;
    public bool CanResetBlurAnimation => IsBlurAnimated && SelectedLayer?.Tracks.Any(track =>
        track.Target == Target with { Property = AnimationProperty.BLUR, NodeId = null } && !track.Transforms.IsEmpty) != true;
    public bool CanAddPositionKeyframe => CanEditPosition && CanAddTargetKeyframe(AnimationProperty.POSITION);
    public bool CanAddScaleKeyframe => CanEditScale && CanAddTargetKeyframe(AnimationProperty.SCALE);
    public bool CanAddOpacityKeyframe => CanEditOpacity && CanAddTargetKeyframe(AnimationProperty.OPACITY);
    public bool CanAddBlurKeyframe => CanEditBlur && CanAddTargetKeyframe(AnimationProperty.BLUR);
    private bool CanAddTargetKeyframe(AnimationProperty property) => SelectedLayer is { } layer &&
        session.CanAddEffectPropertyKeyframe(layer.Id, Target with { Property = property, NodeId = null });
    public bool IsPositionAnimated => HasAnimation(AnimationProperty.POSITION);
    public bool IsScaleAnimated => HasAnimation(AnimationProperty.SCALE);
    public bool IsRotationAnimated => RotationRow?.IsAnimated == true;
    public bool IsOpacityAnimated => HasAnimation(AnimationProperty.OPACITY);
    public bool IsBlurAnimated => HasAnimation(AnimationProperty.BLUR);
    public ICommand TogglePropertyAnimationCommand { get; private set; } = null!;
    internal bool HasPropertyDrafts => session.PropertyEditing.HasDrafts;

    public bool TransformExpanded { get => IsExpanded("transform"); set => SetExpanded("transform", value); }
    public bool TypographyExpanded { get => IsExpanded("typography"); set => SetExpanded("typography", value); }
    public bool FillExpanded { get => IsExpanded("fill"); set => SetExpanded("fill", value); }
    public bool StrokeExpanded { get => IsExpanded("stroke"); set => SetExpanded("stroke", value); }
    public bool ShadowExpanded { get => IsExpanded("shadow"); set => SetExpanded("shadow", value); }
    public bool CompositeExpanded { get => IsExpanded("composite"); set => SetExpanded("composite", value); }
    public bool PathExpanded { get => IsExpanded("path"); set => SetExpanded("path", value); }
    public bool AnimationExpanded { get => IsExpanded("animation"); set => SetExpanded("animation", value); }

    private void InitializePropertyGrid()
    {
        ClearMaskNodeAnimationCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() =>
            session.EditAsync(session.MaskEditing.ClearNodeAnimation)), () => CanClearMaskNodeAnimation);
        CreateRangeCommand = new AsyncRelayCommand(session.CreateSelectedTextAnimationRangeAsync);
        DeleteRangeCommand = new AsyncRelayCommand(session.DeleteSelectedTextAnimationRangeAsync);
        AnimationOptionsPropertyCommand = new RelayCommand<AnimationProperty>(property =>
        {
            if (session.SelectedLayerId is { } layerId)
            {
                session.SelectAnimationProperty(layerId, Target with { Property = property, NodeId = null });
            }
        });
        AddPropertyKeyframeCommand = new AsyncRelayCommand<AnimationProperty>(property => session.AddEffectPropertyKeyframeAsync(Target with { Property = property, NodeId = null }));
        ResetPropertyCommand = new AsyncRelayCommand<AnimationProperty>(property => session.ResetEffectPropertyAsync(Target with { Property = property, NodeId = null }));
        TogglePropertyAnimationCommand = new AsyncRelayCommand<AnimationProperty>(property => session.ToggleEffectPropertyAnimationAsync(property));
    }

    internal void RefreshPropertyGrid()
    {
        session.PropertyEditing.Refresh();
        RefreshMaskPropertyGrid();
        var line = SelectedLayer?.SubtitleId is { } id ? Document.Subtitles.FirstOrDefault(line => line.Id == id) : null;
        if (Target.TextRangeId is { } selectedRange && line?.AnimationRanges.Any(range => range.Id == selectedRange) != true)
        {
            session.SceneEditing.Target = Target with { TextRangeId = null };
            session.ViewModel.Timeline.EffectTarget = Target;
        }
        if (line is null && (Target.TextRangeId is not null || Target.State != SubtitleAnimationState.NORMAL ||
            AnimationPropertyMetadata.IsSubtitleOnlyProperty(Target.Property)))
        {
            session.SceneEditing.Target = new(AnimationProperty.OPACITY);
            session.ViewModel.Timeline.EffectTarget = Target;
        }
        Properties = session.MaskPropertyChoices();
        refreshingScopes = true;
        try
        {
            Scopes = [new(null, Localization.Get("Workbench.WholeSubtitle")), .. (line?.AnimationRanges ?? [])
                .Select(range => new EffectScopeChoice(range.Id, Localization.Format("Workbench.TextRangeLabel",
                    range.Utf16Start + 1, range.Utf16Start + range.Utf16Length,
                    line!.Text.Substring(range.Utf16Start, range.Utf16Length).Replace('\n', ' '))))];
            States = [new(SubtitleAnimationState.NORMAL, Localization.Get("Workbench.NormalAppearance")),
                new(SubtitleAnimationState.INACTIVE, Localization.Get("Workbench.InactiveAppearance")),
                new(SubtitleAnimationState.ACTIVE, Localization.Get("Workbench.ActiveAppearance"))];
            if (!ReferenceEquals(notifiedScope, SelectedScope))
            {
                notifiedScope = SelectedScope;
                OnPropertyChanged(nameof(SelectedScope));
            }
            if (!ReferenceEquals(notifiedState, SelectedState))
            {
                notifiedState = SelectedState;
                OnPropertyChanged(nameof(SelectedState));
            }
        }
        finally
        {
            refreshingScopes = false;
        }
        RotationRows = CanEditTargetTransform ? Rows(AnimationProperty.ROTATION_3D) : [];
        OnPropertyChanged(nameof(RotationRow));
        OnPropertyChanged(nameof(Rotation));
        OnPropertyChanged(nameof(RotationText));
        PerspectiveRows = CanEditTargetTransform ? Rows(AnimationProperty.PERSPECTIVE_DISTANCE) : [];
        TypographyRows = line is not null && Target.State == SubtitleAnimationState.NORMAL
            ? Rows(AnimationProperty.FONT_SIZE, AnimationProperty.LETTER_SPACING) : [];
        FillRows = Rows(AnimationProperty.FILL, AnimationProperty.FILL_BLUR);
        StrokeRows = Rows(AnimationProperty.STROKE, AnimationProperty.STROKE_WIDTH, AnimationProperty.STROKE_BLUR);
        ShadowRows = line is not null ? Rows(AnimationProperty.SHADOW_COLOR, AnimationProperty.SHADOW_OFFSET, AnimationProperty.SHADOW_BLUR) : [];
        PathRows = IsWholeLayerTarget ? Rows(AnimationProperty.PATH_PROGRESS) : [];
        foreach (var property in new[] { nameof(CanEditTextScope), nameof(CanCreateRange),
            nameof(CanManageRange), nameof(IsWholeLayerTarget), nameof(CanEditTargetTransform), nameof(CanEditPosition), nameof(PositionLabel), nameof(PositionEditingHint),
            nameof(TransformExpanded), nameof(TypographyExpanded), nameof(FillExpanded),
            nameof(StrokeExpanded), nameof(ShadowExpanded), nameof(CompositeExpanded), nameof(PathExpanded), nameof(AnimationExpanded),
            nameof(IsPositionAnimated), nameof(IsScaleAnimated), nameof(IsRotationAnimated), nameof(IsOpacityAnimated), nameof(IsBlurAnimated), nameof(ColorSpace), nameof(CanResetPositionAnimation), nameof(CanResetScaleAnimation),
            nameof(CanResetRotationAnimation), nameof(CanResetOpacityAnimation), nameof(CanResetBlurAnimation),
            nameof(CanAddPositionKeyframe), nameof(CanAddScaleKeyframe), nameof(CanAddOpacityKeyframe), nameof(CanAddBlurKeyframe),
            nameof(CanEditBlur), nameof(BlurEditingHint) })
        {
            OnPropertyChanged(property);
        }
    }

    private AnimationPropertyRowViewModel[] Rows(params AnimationProperty[] properties)
    {
        if (SelectedLayer is not { } layer)
        {
            return [];
        }
        return properties.Where(property => layer.SubtitleId is not null || !AnimationPropertyMetadata.IsSubtitleOnlyProperty(property))
            .Select(property =>
            {
                var target = Target with { Property = property, NodeId = null };
                return session.PropertyEditing.GetRow(layer.Id, target);
            }).ToArray();
    }

    internal bool RestorePropertyField(string? key)
    {
        if (session.MaskEditing.RestorePositionField(key))
        {
            return true;
        }
        if (session.PropertyEditing.Restore(key))
        {
            return true;
        }
        if (session.MaskEditing.Fields.FirstOrDefault(field => field.Key == key) is not { } maskField)
        {
            return false;
        }
        session.MaskEditing.Restore(maskField);
        return true;
    }

    internal bool ExpandPropertyField(string? field)
    {
        var row = session.PropertyEditing.FindRow(field);
        if (row is null)
        {
            switch (field)
            {
                case "PositionXInput" or "PositionYInput" or "ScaleXInput" or "ScaleYInput" or "RotationInput":
                    TransformExpanded = true;
                    break;
                case "OpacityInput" or "BlurInput":
                    CompositeExpanded = true;
                    break;
                default:
                    AnimationExpanded = true;
                    break;
            }
            return false;
        }
        if (AnimationPropertyMetadata.IsMaskProperty(row.Target.Property))
        {
            MaskExpanded = true;
            return true;
        }
        switch (row.Target.Property)
        {
            case AnimationProperty.ROTATION_3D or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y or AnimationProperty.PERSPECTIVE_DISTANCE:
                TransformExpanded = true;
                break;
            case AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING:
                TypographyExpanded = true;
                break;
            case AnimationProperty.FILL or AnimationProperty.FILL_BLUR:
                FillExpanded = true;
                break;
            case AnimationProperty.STROKE or AnimationProperty.STROKE_WIDTH or AnimationProperty.STROKE_BLUR:
                StrokeExpanded = true;
                break;
            case AnimationProperty.PATH_PROGRESS:
                PathExpanded = true;
                break;
            default:
                ShadowExpanded = true;
                break;
        }
        return true;
    }

    internal void RefreshTextSelection() => OnPropertyChanged(nameof(CanCreateRange));

    private bool HasAnimation(AnimationProperty property) => SelectedLayer?.Tracks.Any(track =>
        track.Target == Target with { Property = property, NodeId = null }) == true;

    private bool IsExpanded(string key) => !session.ApplicationContext.Preferences.CollapsedEffectCategories.Contains(key);

    private void SetExpanded(string key, bool value)
    {
        if (IsExpanded(key) == value)
        {
            return;
        }
        session.UpdatePreferences(preferences => preferences with
        {
            CollapsedEffectCategories = value ? preferences.CollapsedEffectCategories.Remove(key) :
                preferences.CollapsedEffectCategories.Contains(key) ? preferences.CollapsedEffectCategories : preferences.CollapsedEffectCategories.Add(key)
        });
    }

    private void SetItems<T>(ref T[] items, T[] value, string propertyName)
    {
        if (!items.SequenceEqual(value))
        {
            SetProperty(ref items, value, propertyName);
        }
    }

    internal static bool IsAppearanceProperty(AnimationProperty property) => AnimationPropertyMetadata.IsSubtitleVisualProperty(property);

    internal static bool IsRangeProperty(AnimationProperty property) => AnimationPropertyMetadata.IsTextRangeProperty(property);
}
