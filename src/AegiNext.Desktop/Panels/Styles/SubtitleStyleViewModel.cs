using System.Windows.Input;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using Avalonia.Media;
using TextAlignment = AegiNext.Core.Projects.TextAlignment;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed class SubtitleStyleViewModel : StyleEditorViewModel
{
    private string fontSizeText = "64";
    private string strokeWidthText = "2";
    private readonly NumericValueDraft shadowX = new() { RawText = "2" };
    private readonly NumericValueDraft shadowY = new() { RawText = "2" };
    private readonly NumericValueDraft shadowBlur = new() { RawText = "2" };
    private readonly NumericValueDraft lineHeight = new() { RawText = "1.2" };
    private readonly NumericValueDraft letterSpacing = new() { RawText = "0" };
    private readonly NumericValueDraft fillBlur = new() { RawText = "0" };
    private readonly NumericValueDraft strokeBlur = new() { RawText = "0" };
    private bool loadingStyleNumbers;
    private readonly WorkbenchSession session;
    private string fontFamily = "Noto Sans CJK SC";
    private string fontDraft = "Noto Sans CJK SC";
    private SubtitleFontVariant? fontVariant;
    private bool fontSelectionCommitted;
    private bool synchronizingFont;
    private bool currentSystemFont = true;
    private decimal? fontSize = 64;
    private decimal? strokeWidth = 2;
    private bool? bold = false;
    private bool? italic = false;
    private int alignment = (int)TextAlignment.BOTTOM_CENTER;
    private bool alignmentSelectionCommitted;
    private bool hasCue;
    private int wrapMode;
    private int canvasWidth = 1920;
    private int canvasHeight = 1080;
    private StylePresetListItem[] presets = [];
    private StylePresetListItem? selectedPreset;
    private bool canApplyPreset;

    internal SubtitleStyleViewModel(WorkbenchSession session) : base(session)
    {
        this.session = session;
        FillDraft.Changed += (_, _) => OnPropertyChanged(nameof(FillDraft));
        StrokeDraft.Changed += (_, _) => OnPropertyChanged(nameof(StrokeDraft));
        ShadowDraft.Changed += (_, _) => OnPropertyChanged(nameof(ShadowDraft));
        shadowX.PropertyChanged += (_, e) => OnStyleNumberChanged(e.PropertyName, nameof(ShadowXText), nameof(ShadowX));
        shadowY.PropertyChanged += (_, e) => OnStyleNumberChanged(e.PropertyName, nameof(ShadowYText), nameof(ShadowY));
        shadowBlur.PropertyChanged += (_, e) => OnStyleNumberChanged(e.PropertyName, nameof(ShadowBlurText), nameof(ShadowBlur));
        lineHeight.PropertyChanged += (_, e) => OnStyleNumberChanged(e.PropertyName, nameof(LineHeightText), nameof(LineHeight));
        letterSpacing.PropertyChanged += (_, e) => OnStyleNumberChanged(e.PropertyName, nameof(LetterSpacingText), nameof(LetterSpacing));
        fillBlur.PropertyChanged += (_, e) => OnStyleNumberChanged(e.PropertyName, nameof(FillBlurText), nameof(FillBlur));
        strokeBlur.PropertyChanged += (_, e) => OnStyleNumberChanged(e.PropertyName, nameof(StrokeBlurText), nameof(StrokeBlur));
        Position.Changed += (_, _) => OnPropertyChanged(nameof(Position));
        Margins.Changed += (_, _) =>
        {
            if (!loadingStyleNumbers)
            {
                OnPropertyChanged(nameof(Margins));
            }
        };
        ApplyStyleCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.ApplySelectedStyleAsync()));
        ManageStylesCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.RequestSettingsAsync(SettingsPage.STYLES)));
        RestoreAutomaticPositionCommand = new AsyncRelayCommand(RestoreAutomaticPositionAsync);
    }

    public override LayerKind Kind => LayerKind.SUBTITLE;
    protected override void LoadCore(ProjectDocument document, ProjectLayer layer) => session.LoadSubtitleStyleDraft(this, document, layer);
    protected override ProjectDocument PrepareCore(ProjectDocument document, ProjectLayer layer, bool preview) => session.PrepareSubtitleStyleDraft(document, layer, preview);
    public override bool RestoreField(string fieldKey) => session.RestoreStyleNumericDraftField(fieldKey);
    internal void NotifyDraftChanged() => MarkDraftChanged();

    public string FontFamily
    {
        get => fontFamily;
        set => SetProperty(ref fontFamily, value);
    }

    public string FontDraft
    {
        get => fontDraft;
        set => SetProperty(ref fontDraft, value);
    }

    public SubtitleFontVariant? FontVariant
    {
        get => fontVariant;
        set => SetProperty(ref fontVariant, value);
    }

    public bool FontSelectionCommitted
    {
        get => fontSelectionCommitted;
        private set => SetProperty(ref fontSelectionCommitted, value);
    }

    internal FontSelection CurrentFont => new(FontFamily, FontVariant, currentSystemFont);
    internal SubtitleFontSelectionService Fonts => session.Fonts;

    public decimal? FontSize
    {
        get => fontSize;
        set => SetProperty(ref fontSize, value);
    }

    public decimal? StrokeWidth
    {
        get => strokeWidth;
        set => SetProperty(ref strokeWidth, value);
    }

    public ColorDraft FillDraft { get; } = new();
    public ColorDraft StrokeDraft { get; } = new(SceneColor.Black);
    public ColorDraft ShadowDraft { get; } = new(new(0, 0, 0, 0.6));

    public bool CanEditLetterSpacing => CanEditAppearanceProperty(AnimationProperty.LETTER_SPACING);
    public bool CanEditFillBlur => CanEditAppearanceProperty(AnimationProperty.FILL_BLUR);
    public bool CanEditStrokeBlur => CanEditAppearanceProperty(AnimationProperty.STROKE_BLUR);
    public bool CanEditStrokeWidth => CanEditAppearanceProperty(AnimationProperty.STROKE_WIDTH);
    public string? LetterSpacingEditingHint => CanEditLetterSpacing ? null : Localization.Get("Workbench.StyleOrderedTransformHint");
    public string? FillBlurEditingHint => CanEditFillBlur ? null : Localization.Get("Workbench.StyleOrderedTransformHint");
    public string? StrokeBlurEditingHint => CanEditStrokeBlur ? null : Localization.Get("Workbench.StyleOrderedTransformHint");
    public string? StrokeWidthEditingHint => CanEditStrokeWidth ? null : Localization.Get("Workbench.StyleOrderedTransformHint");
    public static decimal StrokeWidthMinimum => (decimal)AnimationPropertyMetadata.GetMinimum(AnimationProperty.STROKE_WIDTH);
    public static decimal StrokeWidthMaximum => (decimal)AnimationPropertyMetadata.GetMaximum(AnimationProperty.STROKE_WIDTH);

    internal void RefreshAppearanceEditing()
    {
        OnPropertyChanged(nameof(CanEditLetterSpacing));
        OnPropertyChanged(nameof(CanEditFillBlur));
        OnPropertyChanged(nameof(CanEditStrokeBlur));
        OnPropertyChanged(nameof(CanEditStrokeWidth));
        OnPropertyChanged(nameof(LetterSpacingEditingHint));
        OnPropertyChanged(nameof(FillBlurEditingHint));
        OnPropertyChanged(nameof(StrokeBlurEditingHint));
        OnPropertyChanged(nameof(StrokeWidthEditingHint));
    }

    private bool CanEditAppearanceProperty(AnimationProperty property) => session.SelectedLayer?.Tracks.Any(track =>
        track.Property == property && !track.Transforms.IsEmpty) != true;

    public decimal? ShadowX
    {
        get => shadowX.Value;
        set => shadowX.Value = value;
    }

    public decimal? ShadowY
    {
        get => shadowY.Value;
        set => shadowY.Value = value;
    }

    public decimal? ShadowBlur
    {
        get => shadowBlur.Value;
        set => shadowBlur.Value = value;
    }

    public string ShadowXText
    {
        get => shadowX.RawText;
        set => shadowX.RawText = value;
    }

    public string ShadowYText
    {
        get => shadowY.RawText;
        set => shadowY.RawText = value;
    }

    public string ShadowBlurText
    {
        get => shadowBlur.RawText;
        set => shadowBlur.RawText = value;
    }

    public decimal? LetterSpacing
    {
        get => letterSpacing.Value;
        set => letterSpacing.Value = value;
    }

    public string LetterSpacingText
    {
        get => letterSpacing.RawText;
        set => letterSpacing.RawText = value;
    }

    public decimal? FillBlur
    {
        get => fillBlur.Value;
        set => fillBlur.Value = value;
    }

    public string FillBlurText
    {
        get => fillBlur.RawText;
        set => fillBlur.RawText = value;
    }

    public decimal? StrokeBlur
    {
        get => strokeBlur.Value;
        set => strokeBlur.Value = value;
    }

    public string StrokeBlurText
    {
        get => strokeBlur.RawText;
        set => strokeBlur.RawText = value;
    }

    public decimal? LineHeight
    {
        get => lineHeight.Value;
        set => lineHeight.Value = value;
    }

    public string LineHeightText
    {
        get => lineHeight.RawText;
        set => lineHeight.RawText = value;
    }

    internal void LoadStyleNumbers(SubtitleStyle style, CultureInfo culture)
    {
        loadingStyleNumbers = true;
        try
        {
            LoadStyleNumberDraft(lineHeight, style.LineHeight, culture);
            LoadStyleNumberDraft(shadowX, style.ShadowOffset.X, culture);
            LoadStyleNumberDraft(shadowY, style.ShadowOffset.Y, culture);
            LoadStyleNumberDraft(shadowBlur, style.ShadowBlur, culture);
            LoadStyleNumberDraft(letterSpacing, style.LetterSpacing, culture);
            LoadStyleNumberDraft(fillBlur, style.FillBlur, culture);
            LoadStyleNumberDraft(strokeBlur, style.StrokeBlur, culture);
            Margins.Load(style.Margins, culture);
        }
        finally
        {
            loadingStyleNumbers = false;
        }
        PublishStyleNumber(nameof(LetterSpacingText), nameof(LetterSpacing));
        PublishStyleNumber(nameof(FillBlurText), nameof(FillBlur));
        PublishStyleNumber(nameof(StrokeBlurText), nameof(StrokeBlur));
        PublishStyleNumber(nameof(LineHeightText), nameof(LineHeight));
        PublishStyleNumber(nameof(ShadowXText), nameof(ShadowX));
        PublishStyleNumber(nameof(ShadowYText), nameof(ShadowY));
        PublishStyleNumber(nameof(ShadowBlurText), nameof(ShadowBlur));
    }

    internal void LoadStyleNumber(string fieldKey, double number, CultureInfo culture)
    {
        if (fieldKey is "MarginLeftInput" or "MarginRightInput" or "MarginVerticalInput")
        {
            loadingStyleNumbers = true;
            try
            {
                Margins.LoadField(fieldKey, number, culture);
            }
            finally
            {
                loadingStyleNumbers = false;
            }
            OnPropertyChanged(nameof(Margins));
            return;
        }
        var (draft, textProperty, valueProperty) = fieldKey switch
        {
            "LetterSpacingInput" => (letterSpacing, nameof(LetterSpacingText), nameof(LetterSpacing)),
            "FillBlurInput" => (fillBlur, nameof(FillBlurText), nameof(FillBlur)),
            "StrokeBlurInput" => (strokeBlur, nameof(StrokeBlurText), nameof(StrokeBlur)),
            "LineHeightInput" => (lineHeight, nameof(LineHeightText), nameof(LineHeight)),
            "ShadowXInput" => (shadowX, nameof(ShadowXText), nameof(ShadowX)),
            "ShadowYInput" => (shadowY, nameof(ShadowYText), nameof(ShadowY)),
            "ShadowBlurInput" => (shadowBlur, nameof(ShadowBlurText), nameof(ShadowBlur)),
            _ => throw new ArgumentOutOfRangeException(nameof(fieldKey))
        };
        loadingStyleNumbers = true;
        try
        {
            LoadStyleNumberDraft(draft, number, culture);
        }
        finally
        {
            loadingStyleNumbers = false;
        }
        PublishStyleNumber(textProperty, valueProperty);
    }

    private void OnStyleNumberChanged(string? property, string textProperty, string valueProperty)
    {
        if (!loadingStyleNumbers)
        {
            if (property == nameof(NumericValueDraft.RawText))
            {
                OnPropertyChanged(textProperty);
            }
            else if (property == nameof(NumericValueDraft.Value))
            {
                OnPropertyChanged(valueProperty);
            }
        }
    }

    private static void LoadStyleNumberDraft(NumericValueDraft draft, double number, CultureInfo culture)
    {
        var value = (decimal)number;
        draft.RawText = value.ToString(culture);
        draft.Value = value;
    }

    private void PublishStyleNumber(string textProperty, string valueProperty)
    {
        OnPropertyChanged(textProperty);
        OnPropertyChanged(valueProperty);
    }

    internal bool RestoreShadowField(string fieldKey) => fieldKey is "ShadowXInput" or "ShadowYInput" or "ShadowBlurInput"
        && session.RestoreStyleNumericDraftField(fieldKey);

    internal bool RestoreNumberField(string fieldKey) => session.RestoreStyleNumericDraftField(fieldKey);

    public Color Fill
    {
        get => SceneColorConversion.ToColor(FillDraft.Value);
        set
        {
            if (value != Fill)
            {
                FillDraft.SetValue(SceneColorConversion.FromColor(value));
            }
        }
    }

    public Color Stroke
    {
        get => SceneColorConversion.ToColor(StrokeDraft.Value);
        set
        {
            if (value != Stroke)
            {
                StrokeDraft.SetValue(SceneColorConversion.FromColor(value));
            }
        }
    }

    public bool? Bold
    {
        get => bold;
        set => SetProperty(ref bold, value);
    }

    public bool? Italic
    {
        get => italic;
        set => SetProperty(ref italic, value);
    }

    public int Alignment
    {
        get => alignment;
        set
        {
            if (!Enum.IsDefined((TextAlignment)value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            SetProperty(ref alignment, value);
        }
    }

    /// <summary>记录用户主动选择对齐，以在提交时清除历史独立文字对齐。</summary>
    public bool AlignmentSelectionCommitted
    {
        get => alignmentSelectionCommitted;
        private set => SetProperty(ref alignmentSelectionCommitted, value);
    }

    public int WrapMode
    {
        get => wrapMode;
        set
        {
            if (Enum.IsDefined((SubtitleWrapMode)value))
            {
                SetProperty(ref wrapMode, value);
            }
        }
    }

    internal bool CommitWrapMode(int value)
    {
        if (!IsActive || !Enum.IsDefined((SubtitleWrapMode)value))
        {
            return false;
        }
        WrapMode = value;
        return session.TryCommitDrafts();
    }

    public bool HasCue
    {
        get => hasCue;
        set => SetProperty(ref hasCue, value);
    }

    public StylePresetListItem[] Presets
    {
        get => presets;
        set => SetProperty(ref presets, value);
    }

    public StylePresetListItem? SelectedPreset
    {
        get => selectedPreset;
        set => SetProperty(ref selectedPreset, value);
    }

    public bool CanApplyPreset
    {
        get => canApplyPreset;
        set => SetProperty(ref canApplyPreset, value);
    }

    /// <summary>通过会话事务恢复当前字幕的自动对齐与位置。</summary>
    public Task RestoreAutomaticPositionAsync() => IsActive ? session.RunCommandAsync(() => session.EditAsync(session.ResetAutomaticPosition)) : Task.CompletedTask;

    public ICommand ApplyStyleCommand { get; }
    public SubtitlePositionDraft Position { get; } = new();
    public SubtitleMarginsDraft Margins { get; } = new();
    public int CanvasWidth
    {
        get => canvasWidth;
        private set => SetProperty(ref canvasWidth, value);
    }
    public int CanvasHeight
    {
        get => canvasHeight;
        private set => SetProperty(ref canvasHeight, value);
    }

    internal void LoadCanvasSize(int width, int height)
    {
        CanvasWidth = width;
        CanvasHeight = height;
    }

    public ICommand ManageStylesCommand { get; }

    public ICommand RestoreAutomaticPositionCommand { get; }

    /// <summary>确认字体输入并通过统一事务提交。</summary>
    public void CommitFont(string family)
    {
        CommitFont(new FontSelection(family));
    }

    internal void CommitFont(FontSelection selection)
    {
        if (session.IsUpdating || !IsActive)
        {
            return;
        }
        synchronizingFont = true;
        try
        {
            FontFamily = selection.FamilyName;
            FontVariant = selection.Variant;
            currentSystemFont = selection.IsSystemFont;
            FontDraft = selection.DisplayName;
            if (selection.Variant is { } variant)
            {
                Bold = variant.Weight >= 700;
                Italic = variant.Italic;
            }
            FontSelectionCommitted = true;
            OnPropertyChanged(nameof(CurrentFont));
        }
        finally
        {
            synchronizingFont = false;
        }
        session.TryCommitDrafts();
    }

    internal void LoadFont(SubtitleStyle style)
    {
        synchronizingFont = true;
        try
        {
            FontFamily = style.FontFamily;
            FontVariant = style.FontVariant;
            currentSystemFont = !style.FontAssetId.HasValue;
            FontDraft = CurrentFont.DisplayName;
            Bold = style.Bold;
            Italic = style.Italic;
            FontSelectionCommitted = false;
            OnPropertyChanged(nameof(CurrentFont));
        }
        finally
        {
            synchronizingFont = false;
        }
    }
    public string FontSizeText
    {
        get => fontSizeText;
        set => SetProperty(ref fontSizeText, value);
    }
    public string StrokeWidthText
    {
        get => strokeWidthText;
        set => SetProperty(ref strokeWidthText, value);
    }
    /// <summary>提交用户切换后的粗体状态。</summary>
    public void CommitBold(bool value)
    {
        if (synchronizingFont || session.IsUpdating || !IsActive)
        {
            return;
        }
        if (TryReadFontStyle(out var style))
        {
            SetFormatting(Fonts.ToggleBold(style, value), style);
        }
        session.TryCommitDrafts();
    }
    /// <summary>提交用户切换后的斜体状态。</summary>
    public void CommitItalic(bool value)
    {
        if (synchronizingFont || session.IsUpdating || !IsActive)
        {
            return;
        }
        if (TryReadFontStyle(out var style))
        {
            SetFormatting(Fonts.ToggleItalic(style, value), style);
        }
        session.TryCommitDrafts();
    }

    private bool TryReadFontStyle(out SubtitleStyle style)
    {
        var source = session.SelectedCue?.Style ?? new();
        if (!FontSelectionResolver.TryResolve(Fonts.Candidates, CurrentFont, FontDraft, out var selection))
        {
            style = source;
            return false;
        }
        var parsesDraft = selection.FamilyName != FontFamily || selection.Variant != FontVariant;
        var fontChanged = selection.FamilyName != source.FontFamily || selection.Variant != source.FontVariant || FontSelectionCommitted;
        style = source with
        {
            FontFamily = selection.FamilyName, FontVariant = selection.Variant,
            Bold = parsesDraft && selection.Variant is { } variant ? variant.Weight >= 700 : Bold == true,
            Italic = parsesDraft && selection.Variant is { } italicVariant ? italicVariant.Italic : Italic == true,
            FontAssetId = fontChanged ? null : source.FontAssetId
        };
        return true;
    }

    private void SetFormatting(SubtitleInlineStyleOverride edit, SubtitleStyle source)
    {
        var style = edit.ApplyTo(source);
        synchronizingFont = true;
        try
        {
            FontFamily = style.FontFamily;
            FontVariant = style.FontVariant;
            currentSystemFont = !style.FontAssetId.HasValue;
            Bold = style.Bold;
            Italic = style.Italic;
            FontDraft = CurrentFont.DisplayName;
            OnPropertyChanged(nameof(CurrentFont));
        }
        finally
        {
            synchronizingFont = false;
        }
    }
    /// <summary>提交用户选择的填充颜色。</summary>
    public void CommitFill(Color value)
    {
        if (!IsActive)
        {
            return;
        }
        Fill = value;
        session.TryCommitDrafts();
    }
    /// <summary>提交用户选择的描边颜色。</summary>
    public void CommitStroke(Color value)
    {
        if (!IsActive)
        {
            return;
        }
        Stroke = value;
        session.TryCommitDrafts();
    }
    /// <summary>提交用户选择的对齐方式。</summary>
    public bool CommitAlignment(int value)
    {
        if (!Enum.IsDefined((TextAlignment)value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        if (session.IsUpdating || !HasCue || !IsActive)
        {
            return false;
        }
        AlignmentSelectionCommitted = true;
        Alignment = value;
        return session.TryCommitDrafts();
    }

    internal void LoadAlignment(TextAlignment value)
    {
        Alignment = (int)value;
        AlignmentSelectionCommitted = false;
    }
}
