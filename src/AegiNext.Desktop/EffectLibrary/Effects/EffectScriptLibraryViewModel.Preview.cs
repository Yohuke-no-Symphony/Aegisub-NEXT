using System.Collections.Immutable;
using System.ComponentModel;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Rendering.Fonts;
using CommunityToolkit.Mvvm.Input;
using AegiNext.Core.Effects;

namespace AegiNext.Desktop.EffectLibrary.Effects;

public sealed partial class EffectScriptLibraryViewModel
{
    private readonly SubtitleStylePreset defaultPreviewStyle = new(Guid.NewGuid(),
        Localization.Get("EffectLibrary.EffectPreviewDefaultStyle"), new()
        {
            FontSize = 32,
            Alignment = TextAlignment.MIDDLE_CENTER,
            Margins = new(16, 16, 16),
            Scaling = new() { ReferenceWidth = 320, ReferenceHeight = 180 }
        });
    private ImmutableArray<SubtitleStylePreset> previewStyles = [];
    private SubtitleStylePreset? selectedPreviewStyle;
    private string previewText = "YOSYMPH666";
    private string? previewError;
    private Guid? previewDraftId;
    private string previewSource = string.Empty;
    private long previewRevision;
    private double previewPosition;
    private bool previewPlaying;
    private bool previewCompiling;
    private bool previewLoop = true;
    private bool refreshingPreviewChoices;
    private SystemFontCatalog? previewFonts;
    private MediaTime? effectivePreviewDuration;
    private EffectScriptParameterDraftSet? previewParameters;

    public NumericValueDraft PreviewDuration { get; } = new();
    public EffectScriptParameterDraftSet? PreviewParameters => previewParameters;
    public bool HasPreviewParameters => previewParameters?.Rows.Count > 0;

    /// <summary>只更新预览候选，保持源码声明与模板存储不变。</summary>
    public void CommitPreviewParameters()
    {
        if (previewParameters?.TryCapture(out _) == false)
        {
            SetPreviewError(Localization.Get("Workbench.EffectParameterInvalid"));
            return;
        }
        InvalidatePreview(true);
    }

    internal bool TryCapturePreviewParameters(out ImmutableDictionary<string, EffectScriptParameterValue> values)
    {
        values = ImmutableDictionary<string, EffectScriptParameterValue>.Empty;
        return previewParameters is null || previewParameters.TryCapture(out values);
    }
    public RelayCommand PreviewPlayPauseCommand { get; private set; } = null!;
    public ImmutableArray<SubtitleStylePreset> PreviewStyles => previewStyles;
    public bool HasPreview => HasDraft && SelectedIds.Length <= 1 && TryGetPreviewDuration(out _);
    public bool IsPreviewPlaying
    {
        get => previewPlaying;
        private set
        {
            if (SetProperty(ref previewPlaying, value))
            {
                OnPropertyChanged(nameof(PreviewPlayPauseText));
                OnPropertyChanged(nameof(PreviewPlayPauseIcon));
            }
        }
    }
    public bool IsPreviewCompiling
    {
        get => previewCompiling;
        private set => SetProperty(ref previewCompiling, value);
    }
    public string PreviewPlayPauseText => Localization.Get("EffectLibrary." +
        (IsPreviewPlaying ? "EffectPreviewPause" : "EffectPreviewPlay"));
    public string PreviewPlayPauseIcon => IsPreviewPlaying ? "Pause" : "Play";
    public string PreviewPlaceholderText => SelectedIds.Length > 1
        ? Localization.Get("EffectLibrary.EffectPreviewSingleSelection")
        : !HasDraft ? Localization.Get("EffectLibrary.EffectPreviewSelectScript") : string.Empty;
    public bool HasPreviewPlaceholder => PreviewPlaceholderText.Length > 0;
    public string? PreviewError => !TryGetPreviewDuration(out _)
        ? Localization.Get("EffectLibrary.EffectPreviewDurationInvalid") : previewError;
    public bool HasPreviewError => PreviewError is not null;
    public string PreviewPositionText => Localization.Format("EffectLibrary.EffectPreviewPosition",
        PreviewPosition, PreviewDurationSeconds);
    public double PreviewDurationSeconds => TryGetPreviewDuration(out var duration) ? Seconds(duration) : 0;
    internal long PreviewRevision => previewRevision;
    internal bool IsUpdatingPreviewPlayback { get; private set; }

    internal SystemFontCatalog? PreviewFonts
    {
        get => previewFonts;
        set
        {
            if (!ReferenceEquals(previewFonts, value))
            {
                previewFonts = value;
                InvalidatePreview(false);
            }
        }
    }

    public string PreviewText
    {
        get => previewText;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref previewText, value))
            {
                InvalidatePreview(true);
            }
        }
    }

    public SubtitleStylePreset? SelectedPreviewStyle
    {
        get => selectedPreviewStyle;
        set
        {
            if (refreshingPreviewChoices || value is null ||
                PreviewStyles.FirstOrDefault(preset => preset.Id == value.Id) is not { } selected)
            {
                return;
            }
            var previous = selectedPreviewStyle;
            if (SetProperty(ref selectedPreviewStyle, selected) && !HasSamePreviewStyle(previous, selected))
            {
                InvalidatePreview(true);
            }
        }
    }

    public bool PreviewLoop
    {
        get => previewLoop;
        set => SetProperty(ref previewLoop, value);
    }

    public double PreviewPosition
    {
        get => previewPosition;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }
            if (SetProperty(ref previewPosition, Math.Clamp(value, 0, PreviewDurationSeconds)))
            {
                OnPropertyChanged(nameof(PreviewPositionText));
            }
        }
    }

    /// <summary>同步个人样式候选，保留稳定选择；被删除的样式回退到预览默认样式。</summary>
    public void UpdatePreviewStyles(IEnumerable<SubtitleStylePreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        var values = presets.ToImmutableArray();
        var previous = selectedPreviewStyle;
        refreshingPreviewChoices = true;
        try
        {
            previewStyles = [defaultPreviewStyle with { Name = Localization.Get("EffectLibrary.EffectPreviewDefaultStyle") }, ..values];
            selectedPreviewStyle = previewStyles.FirstOrDefault(preset => preset.Id == previous?.Id) ?? previewStyles[0];
            OnPropertyChanged(nameof(PreviewStyles));
            OnPropertyChanged(nameof(SelectedPreviewStyle));
        }
        finally
        {
            refreshingPreviewChoices = false;
        }
        if (!HasSamePreviewStyle(previous, selectedPreviewStyle))
        {
            InvalidatePreview(true);
        }
    }

    internal bool TryGetPreviewDuration(out MediaTime duration)
    {
        duration = MediaTime.Zero;
        if (PreviewDuration.Parse() is not { } seconds || seconds is < 0.001m or > 86400m)
        {
            return false;
        }
        var ticks = decimal.ToInt64(decimal.Round(seconds * TimeSpan.TicksPerSecond, 0, MidpointRounding.ToEven));
        duration = new(ticks, TimeSpan.TicksPerSecond);
        return true;
    }

    internal void SetPreviewError(string? message)
    {
        if (previewError != message)
        {
            previewError = message;
            NotifyPreviewError();
        }
    }

    internal void UpdatePreviewPlayback(MediaTime position, bool playing, bool compiling)
    {
        IsUpdatingPreviewPlayback = true;
        try
        {
            PreviewPosition = Seconds(position);
            IsPreviewPlaying = playing;
            IsPreviewCompiling = compiling;
        }
        finally
        {
            IsUpdatingPreviewPlayback = false;
        }
    }

    private void InitializePreview()
    {
        PreviewDuration.Load(1);
        effectivePreviewDuration = new(1);
        PreviewDuration.PropertyChanged += OnPreviewDurationChanged;
        PreviewPlayPauseCommand = new(() => IsPreviewPlaying = !IsPreviewPlaying, () => HasPreview);
        UpdatePreviewStyles([]);
    }

    private void OnPreviewDurationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(NumericValueDraft.RawText))
        {
            MediaTime? duration = TryGetPreviewDuration(out var parsed) ? parsed : null;
            if (duration == effectivePreviewDuration)
            {
                return;
            }
            effectivePreviewDuration = duration;
            InvalidatePreview(true);
        }
    }

    private void RefreshPreviewInput()
    {
        var id = Draft?.Id;
        var selectionChanged = id != previewDraftId;
        if (!selectionChanged && previewSource == Source)
        {
            return;
        }
        previewDraftId = id;
        previewSource = Source;
        try
        {
            previewParameters = id is null ? null : new(Source);
        }
        catch (Exception error) when (error is EffectScriptException or ArgumentException or InvalidOperationException)
        {
            previewParameters = null;
        }
        OnPropertyChanged(nameof(PreviewParameters));
        OnPropertyChanged(nameof(HasPreviewParameters));
        IsUpdatingPreviewPlayback = true;
        try
        {
            if (selectionChanged)
            {
                IsPreviewPlaying = id is not null;
                if (id is not null)
                {
                    PreviewLoop = true;
                }
            }
        }
        finally
        {
            IsUpdatingPreviewPlayback = false;
        }
        InvalidatePreview(true);
    }

    private void InvalidatePreview(bool resetPosition)
    {
        SetPreviewError(null);
        IsUpdatingPreviewPlayback = true;
        try
        {
            if (resetPosition)
            {
                PreviewPosition = 0;
            }
            IsPreviewCompiling = HasPreview;
        }
        finally
        {
            IsUpdatingPreviewPlayback = false;
        }
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(PreviewPlaceholderText));
        OnPropertyChanged(nameof(HasPreviewPlaceholder));
        OnPropertyChanged(nameof(PreviewDurationSeconds));
        OnPropertyChanged(nameof(PreviewPositionText));
        NotifyPreviewError();
        PreviewPlayPauseCommand?.NotifyCanExecuteChanged();
        previewRevision++;
        OnPropertyChanged(nameof(PreviewRevision));
    }

    private void RefreshPreviewLanguage()
    {
        UpdatePreviewStyles(previewStyles.Skip(1));
        OnPropertyChanged(nameof(PreviewPlayPauseText));
        OnPropertyChanged(nameof(PreviewPlaceholderText));
        OnPropertyChanged(nameof(PreviewPositionText));
        NotifyPreviewError();
    }

    private void NotifyPreviewError()
    {
        OnPropertyChanged(nameof(PreviewError));
        OnPropertyChanged(nameof(HasPreviewError));
    }

    private static bool HasSamePreviewStyle(SubtitleStylePreset? left, SubtitleStylePreset? right)
    {
        return left?.Style == right?.Style;
    }

    private static double Seconds(MediaTime time) => (double)time.Numerator / time.Denominator;
}
