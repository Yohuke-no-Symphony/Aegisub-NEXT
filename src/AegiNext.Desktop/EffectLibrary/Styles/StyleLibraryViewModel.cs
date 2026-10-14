using AegiNext.Desktop.EffectLibrary;
using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Editing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectTextAlignment = AegiNext.Core.Projects.TextAlignment;

namespace AegiNext.Desktop.EffectLibrary.Styles;

/// <summary>字幕样式库页面的独立草稿、验证和语义请求。</summary>
public sealed class StyleLibraryViewModel : ObservableObject
{
    private ImmutableArray<SubtitleStylePreset> styles = [];
    private LibraryStyleDraft? draft;
    internal event EventHandler? DraftChanging;
    private SubtitleStylePreset? selectedStyle;
    private string name = string.Empty;
    private string? error;
    private string? errorKey;
    private bool busy;
    private bool hasSelectedSubtitle;
    private bool loading;
    private bool switching;
    private ImmutableArray<Guid> selectedIds = [];
    internal Func<SubtitleStylePreset, Task<bool>>? SaveDraftAsync { get; set; }
    internal Func<Task<int>>? ConfirmLeaveAsync { get; set; }
    internal Func<bool>? CommitPendingInputs { get; set; }
    internal Func<bool>? HasPendingInputs { get; set; }
    internal Task SelectionCompletion { get; private set; } = Task.CompletedTask;
    private int draftVersion;
    private SubtitleStyle appearanceOriginal = new();
    private decimal? fontSize;
    private decimal? strokeWidth;
    private decimal? letterSpacing;
    private decimal? fillBlur;
    private decimal? strokeBlur;
    private decimal? lineHeight;
    private decimal? shadowBlur;
    private decimal? shadowX;
    private decimal? shadowY;
    private string fontSizeText = string.Empty;
    private string strokeWidthText = string.Empty;
    private string letterSpacingText = string.Empty;
    private string fillBlurText = string.Empty;
    private string strokeBlurText = string.Empty;
    private string lineHeightText = string.Empty;
    private string shadowBlurText = string.Empty;
    private string shadowXText = string.Empty;
    private string shadowYText = string.Empty;
    private string? invalidFieldKey;
    private Func<SubtitleStylePreset, string, SubtitlePositionMeasurement>? measurePosition;
    private string? positionMeasurementError;
    private string previewText = Localization.Get("Workbench.SubtitlePreviewText");
    private bool previewTextEdited;
    private long previewRevision;
    private string? previewError;
    internal SubtitleFontSelectionService Fonts { get; private set; } = new(Array.Empty<AegiNext.Rendering.Fonts.SystemFontFace>());

    internal void SetFonts(SubtitleFontSelectionService fonts)
    {
        Fonts = fonts;
        OnPropertyChanged(nameof(Fonts));
        InvalidatePreview();
    }

    /// <summary>创建样式命令；工程和存储操作交由会话处理。</summary>
    public StyleLibraryViewModel()
    {
        Scaling.Changed += (_, _) =>
        {
            if (Scaling.Validate() is null)
            {
                ChangeStyle(style => style with { Scaling = Scaling.CreateSettings() });
            }
            OnPropertyChanged(nameof(IsDirty));
            InvalidatePreview();
        };
        FillDraft.Committed += (_, args) => Fill = args.Value;
        StrokeDraft.Committed += (_, args) => Stroke = args.Value;
        ShadowDraft.Committed += (_, args) => ShadowColor = args.Value;
        FillDraft.Changed += (_, _) => InvalidatePreview();
        StrokeDraft.Changed += (_, _) => InvalidatePreview();
        ShadowDraft.Changed += (_, _) => InvalidatePreview();
        Margins.Changed += (_, _) =>
        {
            if (Margins.Validate() is null)
            {
                ChangeStyle(style => style with { Margins = Margins.CreateMargins() });
            }
            OnPropertyChanged(nameof(IsDirty));
            InvalidatePreview();
        };
        Position.Changed += (_, _) =>
        {
            if (Position.Validate() is null)
            {
                ChangeStyle(style => style with { Position = Position.CreatePosition() });
            }
            InvalidatePreview();
        };
        AddCommand = new(() => { SelectionCompletion = CreateDraftAsync(false); }, () => !IsBusy && !switching);
        DuplicateCommand = new(() => { SelectionCompletion = CreateDraftAsync(true); }, () => CanEdit && !switching);
        DeleteCommand = new(Delete, () => CanDelete);
        SaveCommand = new(() =>
        {
            if (SaveDraftAsync is null)
            {
                Submit(false);
            }
            else
            {
                SelectionCompletion = SavePendingAsync();
            }
        }, () => CanEdit && !switching);
        ApplyCommand = new(() => Submit(true), () => CanApply);
        CaptureCommand = new(() => CaptureRequested?.Invoke(this, EventArgs.Empty),
            () => HasSelectedSubtitle && !IsBusy);
        ImportCommand = new(() => ImportRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);
        ExportCommand = new(() => ExportRequested?.Invoke(this,
            new(styles.Where(value => selectedIds.Contains(value.Id)).ToImmutableArray())),
            () => !selectedIds.IsEmpty && !IsBusy && !switching);
        RefreshLanguage();
    }

    public event EventHandler<LibraryStyleEventArgs>? UpsertRequested;
    public event EventHandler<LibraryStyleEventArgs>? ApplyRequested;
    public event EventHandler<LibraryStyleDeleteEventArgs>? DeleteRequested;
    public event EventHandler? CaptureRequested;
    public event EventHandler? ImportRequested;
    public event EventHandler<LibraryStylesExportEventArgs>? ExportRequested;
    public RelayCommand AddCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand CaptureCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    public ImmutableArray<SubtitleStylePreset> Styles => styles;
    public SubtitlePositionDraft Position { get; } = new();
    public SubtitleMarginsDraft Margins { get; } = new();
    public SubtitleScalingDraft Scaling { get; } = new();
    public ColorDraft FillDraft { get; } = new();
    public ColorDraft StrokeDraft { get; } = new(SceneColor.Black);
    public ColorDraft ShadowDraft { get; } = new(SceneColor.Black);
    public string? PositionMeasurementError => positionMeasurementError;
    public bool HasPositionMeasurementError => positionMeasurementError is not null;
    public int CanvasWidth => Position.Geometry is { } geometry ? (int)geometry.ParentSize.X : new ProjectDocument().Width;
    public int CanvasHeight => Position.Geometry is { } geometry ? (int)geometry.ParentSize.Y : new ProjectDocument().Height;
    public SubtitleStylePreset? Draft => draft?.Preset;
    public bool HasDraft => draft is not null;
    public bool IsEmpty => styles.IsEmpty;
    public ImmutableArray<Guid> SelectedIds => selectedIds;
    public bool IsDirty => HasDraft && (draft!.Preset != styles.FirstOrDefault(value => value.Id == draft.Preset.Id) ||
        FillDraft.IsDirty || StrokeDraft.IsDirty || ShadowDraft.IsDirty || Scaling.IsDirty || Position.Validate() is not null || Margins.Validate() is not null ||
        new[] { LetterSpacingText, FillBlurText, StrokeBlurText, FontSizeText, StrokeWidthText, LineHeightText, ShadowBlurText, ShadowXText, ShadowYText }
            .Any(value => ParseNumber(value) is null) || HasPendingInputs?.Invoke() == true);
    public bool CanDelete => !IsBusy && !switching && (!selectedIds.IsEmpty ||
        draft is not null && !styles.Any(value => value.Id == draft.Preset.Id));
    public bool CanEdit => HasDraft && selectedIds.Length <= 1 && !IsBusy;
    public bool CanApply => CanEdit && HasSelectedSubtitle;
    public bool IsAvailable => !IsBusy;
    public string FontSource => Localization.Get("Settings." + (draft?.Preset.Font is null ? "SystemFont" : "EmbeddedFont"));
    public int DraftVersion => draftVersion;
    public bool HasPreview => HasDraft && selectedIds.Length <= 1;
    internal long PreviewRevision => previewRevision;
    public string? PreviewError => previewError is null ? null : Localization.Format("Settings.StylePreviewError", previewError);
    public bool HasPreviewError => previewError is not null;

    public string PreviewText
    {
        get => previewText;
        set
        {
            if (SetProperty(ref previewText, value))
            {
                previewTextEdited = true;
                RefreshPositionMeasurement(!Position.IsExplicit);
                InvalidatePreview();
            }
        }
    }

    internal void SetPreviewError(string? value)
    {
        previewError = value;
        OnPropertyChanged(nameof(PreviewError));
        OnPropertyChanged(nameof(HasPreviewError));
    }

    internal bool TryCreatePreviewPreset(out SubtitleStylePreset? preset)
    {
        preset = null;
        if (!HasPreview || Position.Validate() is not null || Margins.Validate() is not null || Scaling.Validate() is not null || FillDraft.HasError || StrokeDraft.HasError || ShadowDraft.HasError ||
            NumericFields().Any(field => field.Value is null || field.Value < field.Minimum || field.Value > field.Maximum))
        {
            return false;
        }
        var source = draft!.Preset.Style;
        var style = source with
        {
            Scaling = Scaling.CreateSettings(),
            LetterSpacing = ReadPreviewNumber(LetterSpacingText, source.LetterSpacing),
            FillBlur = ReadPreviewNumber(FillBlurText, source.FillBlur),
            StrokeBlur = ReadPreviewNumber(StrokeBlurText, source.StrokeBlur),
            FontSize = ReadPreviewNumber(FontSizeText, source.FontSize),
            StrokeWidth = ReadPreviewNumber(StrokeWidthText, source.StrokeWidth),
            Margins = Margins.CreateMargins(),
            LineHeight = ReadPreviewNumber(LineHeightText, source.LineHeight),
            ShadowBlur = ReadPreviewNumber(ShadowBlurText, source.ShadowBlur),
            ShadowOffset = new(ReadPreviewNumber(ShadowXText, source.ShadowOffset.X), ReadPreviewNumber(ShadowYText, source.ShadowOffset.Y)),
            Fill = FillDraft.Value, Stroke = StrokeDraft.Value, ShadowColor = ShadowDraft.Value,
            Position = Position.CreatePosition()
        };
        try
        {
            ProjectValidator.ValidateSubtitleStyle(style);
            preset = draft.Preset with { Style = style };
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static double ReadPreviewNumber(string text, double source)
    {
        var number = ParseNumber(text)!.Value;
        return number == (decimal)source ? source : (double)number;
    }

    private void InvalidatePreview()
    {
        if (!loading)
        {
            previewRevision++;
            OnPropertyChanged(nameof(PreviewRevision));
        }
    }

    public string? InvalidFieldKey
    {
        get => invalidFieldKey;
        private set => SetProperty(ref invalidFieldKey, value);
    }

    public string? Error
    {
        get => error;
        private set => SetProperty(ref error, value);
    }

    public bool IsBusy
    {
        get => busy;
        set
        {
            if (SetProperty(ref busy, value))
            {
                RefreshActions();
            }
        }
    }

    public bool HasSelectedSubtitle
    {
        get => hasSelectedSubtitle;
        set
        {
            if (SetProperty(ref hasSelectedSubtitle, value))
            {
                RefreshActions();
            }
        }
    }

    public SubtitleStylePreset? SelectedStyle
    {
        get => selectedStyle;
        set
        {
            if (SetProperty(ref selectedStyle, value) && !loading)
            {
                SetSelection(value?.Id, value is null ? [] : [value.Id]);
                LoadDraft(value is null ? null : new(value));
            }
        }
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value) && !loading)
            {
                draft?.Rename(value);
            }
        }
    }

    public decimal? FontSize
    {
        get => fontSize;
        set => SetNumericValue(ref fontSize, value, nameof(FontSize),
            (style, number) => style with { FontSize = (double)number });
    }

    public string FontSizeText
    {
        get => fontSizeText;
        set => SetNumericText(ref fontSizeText, value, nameof(FontSizeText), number => FontSize = number);
    }

    public decimal? StrokeWidth
    {
        get => strokeWidth;
        set => SetNumericValue(ref strokeWidth, value, nameof(StrokeWidth),
            (style, number) => style with { StrokeWidth = (double)number });
    }

    public string StrokeWidthText
    {
        get => strokeWidthText;
        set => SetNumericText(ref strokeWidthText, value, nameof(StrokeWidthText), number => StrokeWidth = number);
    }

    public int WrapModeIndex
    {
        get => (int)(draft?.Preset.Style.WrapMode ?? SubtitleWrapMode.GRAPHEME);
        set
        {
            if (Enum.IsDefined((SubtitleWrapMode)value) && WrapModeIndex != value)
            {
                ChangeStyle(style => style with { WrapMode = (SubtitleWrapMode)value });
                OnPropertyChanged();
            }
        }
    }

    internal bool RestoreAppearanceField(string fieldKey)
    {
        if (Scaling.RestoreField(fieldKey))
        {
            if (InvalidFieldKey == fieldKey)
            {
                InvalidFieldKey = null;
                errorKey = null;
                Error = null;
            }
            return true;
        }
        switch (fieldKey)
        {
            case "LetterSpacingInput":
                LetterSpacingText = FormatNumber((decimal)appearanceOriginal.LetterSpacing);
                break;
            case "FillBlurInput":
                FillBlurText = FormatNumber((decimal)appearanceOriginal.FillBlur);
                break;
            case "StrokeBlurInput":
                StrokeBlurText = FormatNumber((decimal)appearanceOriginal.StrokeBlur);
                break;
            default:
                return false;
        }
        if (InvalidFieldKey == fieldKey)
        {
            InvalidFieldKey = null;
            errorKey = null;
            Error = null;
        }
        return true;
    }

    public decimal? LetterSpacing
    {
        get => letterSpacing;
        set => SetNumericValue(ref letterSpacing, value, nameof(LetterSpacing),
            (style, number) => style with { LetterSpacing = (double)number });
    }

    public string LetterSpacingText
    {
        get => letterSpacingText;
        set => SetNumericText(ref letterSpacingText, value, nameof(LetterSpacingText), number => LetterSpacing = number);
    }

    public decimal? FillBlur
    {
        get => fillBlur;
        set => SetNumericValue(ref fillBlur, value, nameof(FillBlur),
            (style, number) => style with { FillBlur = (double)number });
    }

    public string FillBlurText
    {
        get => fillBlurText;
        set => SetNumericText(ref fillBlurText, value, nameof(FillBlurText), number => FillBlur = number);
    }

    public decimal? StrokeBlur
    {
        get => strokeBlur;
        set => SetNumericValue(ref strokeBlur, value, nameof(StrokeBlur),
            (style, number) => style with { StrokeBlur = (double)number });
    }

    public string StrokeBlurText
    {
        get => strokeBlurText;
        set => SetNumericText(ref strokeBlurText, value, nameof(StrokeBlurText), number => StrokeBlur = number);
    }

    public decimal? LineHeight
    {
        get => lineHeight;
        set => SetNumericValue(ref lineHeight, value, nameof(LineHeight),
            (style, number) => style with { LineHeight = (double)number });
    }

    public string LineHeightText
    {
        get => lineHeightText;
        set => SetNumericText(ref lineHeightText, value, nameof(LineHeightText), number => LineHeight = number);
    }

    public decimal? ShadowBlur
    {
        get => shadowBlur;
        set => SetNumericValue(ref shadowBlur, value, nameof(ShadowBlur),
            (style, number) => style with { ShadowBlur = (double)number });
    }

    public string ShadowBlurText
    {
        get => shadowBlurText;
        set => SetNumericText(ref shadowBlurText, value, nameof(ShadowBlurText), number => ShadowBlur = number);
    }

    public decimal? ShadowX
    {
        get => shadowX;
        set => SetNumericValue(ref shadowX, value, nameof(ShadowX),
            (style, number) => style with { ShadowOffset = new((double)number, style.ShadowOffset.Y) });
    }

    public string ShadowXText
    {
        get => shadowXText;
        set => SetNumericText(ref shadowXText, value, nameof(ShadowXText), number => ShadowX = number);
    }

    public decimal? ShadowY
    {
        get => shadowY;
        set => SetNumericValue(ref shadowY, value, nameof(ShadowY),
            (style, number) => style with { ShadowOffset = new(style.ShadowOffset.X, (double)number) });
    }

    public string ShadowYText
    {
        get => shadowYText;
        set => SetNumericText(ref shadowYText, value, nameof(ShadowYText), number => ShadowY = number);
    }

    public bool Bold
    {
        get => draft?.Preset.Style.Bold ?? false;
        set
        {
            if (!EqualityComparer<bool>.Default.Equals(Bold, value))
            {
                ChangeStyle(style => (draft?.Preset.Font is null ? Fonts.ToggleBold(style, value) :
                    new SubtitleInlineStyleOverride { Bold = value }).ApplyTo(style));
                NotifyFontChanged();
                OnPropertyChanged();
            }
        }
    }

    public bool Italic
    {
        get => draft?.Preset.Style.Italic ?? false;
        set
        {
            if (!EqualityComparer<bool>.Default.Equals(Italic, value))
            {
                ChangeStyle(style => (draft?.Preset.Font is null ? Fonts.ToggleItalic(style, value) :
                    new SubtitleInlineStyleOverride { Italic = value }).ApplyTo(style));
                NotifyFontChanged();
                OnPropertyChanged();
            }
        }
    }

    public SceneColor Fill
    {
        get => draft?.Preset.Style.Fill ?? new();
        set
        {
            if (!EqualityComparer<SceneColor>.Default.Equals(Fill, value))
            {
                ChangeStyle(style => style with { Fill = value });
                OnPropertyChanged();
            }
            FillDraft.Load(Fill);
        }
    }

    public SceneColor Stroke
    {
        get => draft?.Preset.Style.Stroke ?? new();
        set
        {
            if (!EqualityComparer<SceneColor>.Default.Equals(Stroke, value))
            {
                ChangeStyle(style => style with { Stroke = value });
                OnPropertyChanged();
            }
            StrokeDraft.Load(Stroke);
        }
    }

    public SceneColor ShadowColor
    {
        get => draft?.Preset.Style.ShadowColor ?? new();
        set
        {
            if (!EqualityComparer<SceneColor>.Default.Equals(ShadowColor, value))
            {
                ChangeStyle(style => style with { ShadowColor = value });
                OnPropertyChanged();
            }
            ShadowDraft.Load(ShadowColor);
        }
    }

    public int AlignmentIndex
    {
        get => (int)(draft?.Preset.Style.Alignment ?? ProjectTextAlignment.BOTTOM_CENTER);
        set
        {
            if (Enum.IsDefined((ProjectTextAlignment)value) && AlignmentIndex != value)
            {
                ChangeStyle(style => style with { Alignment = (ProjectTextAlignment)value });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>同步已提交样式库并保留选择标识。</summary>
    public void UpdateStyles(IEnumerable<SubtitleStylePreset> presets, Guid? selectedId = null)
    {
        ArgumentNullException.ThrowIfNull(presets);
        DraftChanging?.Invoke(this, EventArgs.Empty);
        var pending = draft;
        var dirty = IsDirty;
        var wasNew = pending is not null && styles.All(value => value.Id != pending.Preset.Id);
        var previousSelection = selectedIds;
        styles = presets.ToImmutableArray();
        var committed = pending is not null && styles.FirstOrDefault(value => value.Id == pending.Preset.Id) == pending.Preset;
        var selection = selectedId ?? (committed ? pending?.Preset.Id : SelectedStyle?.Id);
        loading = true;
        try
        {
            OnPropertyChanged(nameof(Styles));
            if (wasNew && !committed && selectedId is null)
            {
                SetSelection(null, []);
            }
            else
            {
                var ids = selectedId is not null || committed
                    ? (selection is { } id ? new[] { id } : [])
                    : previousSelection.Where(id => styles.Any(value => value.Id == id)).ToArray();
                if (ids.Length == 0 && !previousSelection.IsEmpty)
                {
                    ids = styles.Take(1).Select(value => value.Id).ToArray();
                }
                if (pending is null && ids.Length == 0)
                {
                    ids = styles.Take(1).Select(value => value.Id).ToArray();
                }
                SetSelection(selection, ids);
            }
        }
        finally
        {
            loading = false;
        }
        if (dirty && !committed && selectedId is null && (wasNew || SelectedStyle?.Id == pending?.Preset.Id))
        {
            if (SelectedStyle is not null)
            {
                pending!.UpdateTimingPostProcessor(SelectedStyle.TimingPostProcessor);
                OnPropertyChanged(nameof(Draft));
            }
            RefreshActions();
            return;
        }
        LoadDraft(SelectedStyle is null ? null : new(SelectedStyle));
    }

    /// <summary>按稳定身份同步列表选择；多选支持批量导出与删除。</summary>
    public void SelectStyles(Guid? primaryId, IEnumerable<Guid> ids)
    {
        if (loading)
        {
            return;
        }
        var previous = SelectedStyle?.Id;
        SetSelection(primaryId, ids);
        if (previous != SelectedStyle?.Id || draft is null)
        {
            LoadDraft(SelectedStyle is null ? null : new(SelectedStyle));
        }
        RefreshActions();
    }

    /// <summary>等待未保存修改的处理结果，再切换列表选择。</summary>
    public Task<bool> SelectStylesAsync(Guid? primaryId, IEnumerable<Guid> ids)
    {
        if (switching || loading)
        {
            return Task.FromResult(false);
        }
        var values = ids.ToArray();
        var completion = SelectStylesCoreAsync(primaryId, values);
        SelectionCompletion = completion;
        return completion;
    }

    private async Task<bool> SelectStylesCoreAsync(Guid? primaryId, Guid[] ids)
    {
        if (loading || switching)
        {
            return false;
        }
        if (SelectedStyle?.Id == primaryId && selectedIds.ToHashSet().SetEquals(ids))
        {
            return true;
        }
        switching = true;
        RefreshActions();
        try
        {
            if (!await PrepareToLeaveAsync())
            {
                return false;
            }
            SelectStyles(primaryId, ids);
            return true;
        }
        finally
        {
            switching = false;
            RefreshActions();
        }
    }

    /// <summary>有未保存修改时，等待保存、恢复或取消的决定。</summary>
    public async Task<bool> PrepareToLeaveAsync()
    {
        DraftChanging?.Invoke(this, EventArgs.Empty);
        if (!IsDirty)
        {
            return true;
        }
        var choice = ConfirmLeaveAsync is null ? 0 : await ConfirmLeaveAsync();
        if (choice == 1)
        {
            DiscardDraft();
            return true;
        }
        return choice == 0 && await SavePendingAsync();
    }

    /// <summary>验证有效草稿并等待持久化，失败时保持编辑状态。</summary>
    public async Task<bool> SavePendingAsync()
    {
        if (CommitPendingInputs?.Invoke() == false)
        {
            return false;
        }
        if (!IsDirty)
        {
            return true;
        }
        var preset = PreparePreset(false);
        return preset is not null && SaveDraftAsync is not null && await SaveDraftAsync(preset);
    }

    /// <summary>恢复当前已保存样式，或直接丢弃尚未入库的新草稿。</summary>
    public void DiscardDraft()
    {
        var saved = draft is null ? null : styles.FirstOrDefault(value => value.Id == draft.Preset.Id);
        LoadDraft(saved is null ? null : new(saved));
    }

    private void SetSelection(Guid? primaryId, IEnumerable<Guid> ids)
    {
        var available = ids.ToHashSet();
        selectedIds = styles.Where(value => available.Contains(value.Id)).Select(value => value.Id).ToImmutableArray();
        var wasLoading = loading;
        loading = true;
        try
        {
            SelectedStyle = styles.FirstOrDefault(value => value.Id == primaryId && selectedIds.Contains(value.Id)) ??
                styles.FirstOrDefault(value => selectedIds.Contains(value.Id));
            OnPropertyChanged(nameof(SelectedIds));
        }
        finally
        {
            loading = wasLoading;
        }
    }

    private async Task CreateDraftAsync(bool duplicate)
    {
        if (switching)
        {
            return;
        }
        DraftChanging?.Invoke(this, EventArgs.Empty);
        switching = true;
        RefreshActions();
        try
        {
            if (SaveDraftAsync is not null && !await PrepareToLeaveAsync())
            {
                return;
            }
            if (duplicate)
            {
                Duplicate();
            }
            else
            {
                Add();
            }
        }
        finally
        {
            switching = false;
            RefreshActions();
        }
    }

    private void Delete()
    {
        if (!CanDelete)
        {
            return;
        }

        if (!selectedIds.IsEmpty)
        {
            DeleteRequested?.Invoke(this, new(selectedIds));
        }
        else if (draft is not null)
        {
            DeleteRequested?.Invoke(this, new([], true, draft.Preset.Id));
        }
    }

    /// <summary>注入组合根提供的纯测量入口；页面模型不拥有字体或原生渲染器。</summary>
    public void SetPositionMeasurement(Func<SubtitleStylePreset, SubtitlePositionMeasurement> measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        SetPositionMeasurement((preset, _) => measure(preset));
    }

    /// <summary>按当前示例文字测量字体和位置，不提交页面草稿。</summary>
    public void SetPositionMeasurement(Func<SubtitleStylePreset, string, SubtitlePositionMeasurement> measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        measurePosition = measure;
        RefreshPositionMeasurement(true);
        InvalidatePreview();
    }

    /// <summary>提交局部字体控件已确认的字体名。</summary>
    public void CommitFont(string familyName)
    {
        CommitFont(new FontSelection(familyName));
    }

    internal void CommitFont(FontSelection selection)
    {
        ChangeStyle(style => SubtitleFontSelectionService.CreateOverride(selection).ApplyTo(style), true);
        NotifyFontChanged();
    }

    /// <summary>确认九宫格对齐并清除旧的独立文字对齐覆盖。</summary>
    public void CommitAlignment(ProjectTextAlignment alignment)
    {
        if (!Enum.IsDefined(alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }
        if (CanEdit && draft is not null &&
            (draft.Preset.Style.Alignment != alignment || draft.Preset.Style.TextAlign is not null))
        {
            ChangeStyle(style => style with { Alignment = alignment, TextAlign = null });
            OnPropertyChanged(nameof(AlignmentIndex));
        }
    }

    private void NotifyFontChanged()
    {
        var wasLoading = loading;
        loading = true;
        try
        {
            OnPropertyChanged(nameof(Bold));
            OnPropertyChanged(nameof(Italic));
            OnPropertyChanged(nameof(Draft));
            draftVersion++;
            OnPropertyChanged(nameof(DraftVersion));
        }
        finally
        {
            loading = wasLoading;
        }
    }

    /// <summary>显示字体控件拒绝空白输入的结果。</summary>
    public void RejectFont()
    {
        InvalidFieldKey = "FontInput";
        SetError("FontRequired");
    }

    /// <summary>刷新标签但不覆盖尚未确认的草稿。</summary>
    public void RefreshLanguage()
    {
        var wasLoading = loading;
        loading = true;
        try
        {
            OnPropertyChanged(nameof(AlignmentIndex));
            OnPropertyChanged(nameof(FontSource));
            FillDraft.RefreshLanguage();
            StrokeDraft.RefreshLanguage();
            ShadowDraft.RefreshLanguage();
            OnPropertyChanged(nameof(PreviewError));
            if (!previewTextEdited && SetProperty(ref previewText, Localization.Get("Workbench.SubtitlePreviewText"), nameof(PreviewText)))
            {
                RefreshPositionMeasurement(!Position.IsExplicit);
            }
            if (errorKey is not null)
            {
                Error = Localization.Get("Settings." + errorKey);
            }
        }
        finally
        {
            loading = wasLoading;
        }
        InvalidatePreview();
    }

    private void Add()
    {
        ClearSelection();
        LoadDraft(new(new(Guid.NewGuid(), LibraryStyleDraft.UniqueName(Localization.Get("Settings.NewStyle"), styles), new())));
    }

    private void Duplicate()
    {
        if (draft is null)
        {
            return;
        }

        var next = draft.Duplicate(
            LibraryStyleDraft.UniqueName($"{draft.Preset.Name} {Localization.Get("Settings.CopySuffix")}", styles));
        ClearSelection();
        LoadDraft(next);
    }

    private void ClearSelection()
    {
        SetSelection(null, []);
    }

    private void ChangeStyle(Func<SubtitleStyle, SubtitleStyle> change, bool clearFont = false)
    {
        if (!loading && draft is not null)
        {
            draft.UpdateStyle(change(draft.Preset.Style), clearFont);
            RefreshPositionMeasurement(!Position.IsExplicit);
            OnPropertyChanged(nameof(FontSource));
            InvalidatePreview();
        }
    }

    private void SetNumericValue(ref decimal? field, decimal? value, string property,
        Func<SubtitleStyle, decimal, SubtitleStyle> change)
    {
        if (SetProperty(ref field, value, property) && value is { } number)
        {
            ChangeStyle(style => change(style, number));
        }
    }

    private void SetNumericText(ref string field, string value, string property, Action<decimal> change)
    {
        if (SetProperty(ref field, value, property))
        {
            if (ParseNumber(value) is { } number)
            {
                change(number);
            }
            InvalidatePreview();
        }
    }

    private static decimal? ParseNumber(string value)
    {
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) ? number : null;
    }

    private static string FormatNumber(decimal? value)
    {
        return value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    }

    private void LoadDraft(LibraryStyleDraft? next)
    {
        DraftChanging?.Invoke(this, EventArgs.Empty);
        draft = next;
        loading = true;
        try
        {
            var style = draft?.Preset.Style ?? new();
            appearanceOriginal = style;
            Scaling.Load(style.Scaling, CultureInfo.CurrentCulture);
            Name = draft?.Preset.Name ?? string.Empty;
            LetterSpacingText = FormatNumber((decimal)style.LetterSpacing);
            FillBlurText = FormatNumber((decimal)style.FillBlur);
            StrokeBlurText = FormatNumber((decimal)style.StrokeBlur);
            FontSizeText = FormatNumber((decimal)style.FontSize);
            StrokeWidthText = FormatNumber((decimal)style.StrokeWidth);
            LineHeightText = FormatNumber((decimal)style.LineHeight);
            ShadowBlurText = FormatNumber((decimal)style.ShadowBlur);
            ShadowXText = FormatNumber((decimal)style.ShadowOffset.X);
            ShadowYText = FormatNumber((decimal)style.ShadowOffset.Y);
            LetterSpacing = (decimal)style.LetterSpacing;
            FillBlur = (decimal)style.FillBlur;
            StrokeBlur = (decimal)style.StrokeBlur;
            FontSize = (decimal)style.FontSize;
            StrokeWidth = (decimal)style.StrokeWidth;
            Margins.Load(style.Margins, CultureInfo.CurrentCulture);
            LineHeight = (decimal)style.LineHeight;
            ShadowBlur = (decimal)style.ShadowBlur;
            ShadowX = (decimal)style.ShadowOffset.X;
            ShadowY = (decimal)style.ShadowOffset.Y;
            FillDraft.Load(style.Fill);
            StrokeDraft.Load(style.Stroke);
            ShadowDraft.Load(style.ShadowColor);
            RefreshPositionMeasurement(true);
            foreach (var property in new[]
                     {
                         nameof(Bold), nameof(Italic), nameof(Fill), nameof(Stroke), nameof(ShadowColor),
                         nameof(WrapModeIndex), nameof(AlignmentIndex), nameof(FontSource), nameof(Draft)
                     })
            {
                OnPropertyChanged(property);
            }

            draftVersion++;
            OnPropertyChanged(nameof(DraftVersion));
        }
        finally
        {
            loading = false;
        }

        errorKey = null;
        Error = null;
        InvalidFieldKey = null;
        RefreshActions();
    }

    private void RefreshPositionMeasurement(bool reload)
    {
        var measurement = draft is not null ? measurePosition?.Invoke(draft.Preset, PreviewText) : null;
        positionMeasurementError = measurement?.Error;
        OnPropertyChanged(nameof(PositionMeasurementError));
        OnPropertyChanged(nameof(HasPositionMeasurementError));
        if (reload)
        {
            Position.Load(draft?.Preset.Style ?? new(), measurement?.Position, true, measurement?.Geometry);
        }
        else
        {
            Position.UpdateGeometry(measurement?.Geometry);
        }
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    private void RefreshActions()
    {
        OnPropertyChanged(nameof(HasDraft));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(HasPreview));
        InvalidatePreview();
        foreach (var command in new[]
                 {
                     AddCommand, DuplicateCommand, DeleteCommand, SaveCommand, ApplyCommand, CaptureCommand,
                     ImportCommand, ExportCommand
                 })
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private SubtitleStylePreset? PreparePreset(bool apply)
    {
        if (draft is null)
        {
            return null;
        }

        var preset = draft.Preset;
        if (Scaling.Validate() is { } scalingKey)
        {
            InvalidFieldKey = scalingKey;
            SetError("StyleValidation");
            return null;
        }
        preset = preset with { Style = preset.Style with { Scaling = Scaling.CreateSettings() } };
        if (Margins.Validate() is { } marginKey)
        {
            InvalidFieldKey = marginKey;
            SetError("StyleValidation");
            return null;
        }
        preset = preset with { Style = preset.Style with { Margins = Margins.CreateMargins() } };
        foreach (var color in new (ColorDraft Draft, string Key)[]
                 { (FillDraft, "FillPicker"), (StrokeDraft, "StrokePicker"), (ShadowDraft, "ShadowPicker") })
        {
            if (!color.Draft.TryCommit(out _))
            {
                InvalidFieldKey = color.Key + "." + color.Draft.InvalidFieldKey;
                SetError("StyleValidation");
                return null;
            }
        }
        _ = FillDraft.TryCommit(out var fillColor);
        _ = StrokeDraft.TryCommit(out var strokeColor);
        _ = ShadowDraft.TryCommit(out var shadowColorValue);
        preset = preset with { Style = preset.Style with { Fill = fillColor, Stroke = strokeColor, ShadowColor = shadowColorValue } };
        if (Position.Validate() is { } positionKey)
        {
            InvalidFieldKey = positionKey;
            SetError("StyleValidation");
            return null;
        }
        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            InvalidFieldKey = "StyleNameInput";
            SetError("NameRequired");
            return null;
        }

        if (!apply && styles.Any(value =>
                value.Id != preset.Id && string.Equals(value.Name, preset.Name, StringComparison.OrdinalIgnoreCase)))
        {
            InvalidFieldKey = "StyleNameInput";
            SetError("DuplicateName");
            return null;
        }

        if (string.IsNullOrWhiteSpace(preset.Style.FontFamily))
        {
            RejectFont();
            return null;
        }

        foreach (var field in NumericFields())
        {
            if (field.Value is null || field.Value < field.Minimum || field.Value > field.Maximum)
            {
                InvalidFieldKey = field.Key;
                SetError("StyleValidation");
                return null;
            }
        }

        try
        {
            SubtitleStylePresetValidator.Validate(preset);
        }
        catch (InvalidDataException)
        {
            SetError("StyleValidation");
            return null;
        }

        errorKey = null;
        Error = null;
        InvalidFieldKey = null;
        draft.UpdateStyle(preset.Style);
        FillDraft.Load(fillColor);
        StrokeDraft.Load(strokeColor);
        ShadowDraft.Load(shadowColorValue);
        return preset;
    }

    private void Submit(bool apply)
    {
        if (PreparePreset(apply) is not { } preset)
        {
            return;
        }
        if (apply)
        {
            ApplyRequested?.Invoke(this, new(preset));
        }
        else
        {
            UpsertRequested?.Invoke(this, new(preset));
        }
    }

    private void SetError(string key)
    {
        errorKey = key;
        Error = Localization.Get("Settings." + key);
    }

    private (decimal? Value, decimal Minimum, decimal Maximum, string Key)[] NumericFields()
    {
        return
        [
            (LetterSpacing is null ? null : ParseNumber(LetterSpacingText), -4096, 4096, "LetterSpacingInput"),
            (FillBlur is null ? null : ParseNumber(FillBlurText), 0, 512, "FillBlurInput"),
            (StrokeBlur is null ? null : ParseNumber(StrokeBlurText), 0, 512, "StrokeBlurInput"),
            (FontSize is null ? null : ParseNumber(FontSizeText), 0.01m, 4096, "FontSizeInput"),
            (StrokeWidth is null ? null : ParseNumber(StrokeWidthText), 0, 4096, "StrokeWidthInput"),
            (LineHeight is null ? null : ParseNumber(LineHeightText), 0.1m, 10, "LineHeightInput"),
            (ShadowBlur is null ? null : ParseNumber(ShadowBlurText), 0, 512, "ShadowBlurInput"),
            (ShadowX is null ? null : ParseNumber(ShadowXText), -1000000000, 1000000000, "ShadowXInput"),
            (ShadowY is null ? null : ParseNumber(ShadowYText), -1000000000, 1000000000, "ShadowYInput")
        ];
    }
}
