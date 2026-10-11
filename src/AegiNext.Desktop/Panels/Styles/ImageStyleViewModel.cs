using System.ComponentModel;
using System.Windows.Input;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed class ImageStyleViewModel : StyleEditorViewModel
{
    private int fit;
    private int playbackMode;
    private bool animationEnabled;
    private bool aspectRatioLocked = true;
    private bool resizing;
    private double aspectRatio = 1;

    internal ImageStyleViewModel(WorkbenchSession session) : base(session)
    {
        Width.PropertyChanged += OnWidthChanged;
        Height.PropertyChanged += OnHeightChanged;
        foreach (var draft in new[] { CropX, CropY, CropWidth, CropHeight, PlaybackOffset, PlaybackSpeed })
        {
            draft.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(NumericValueDraft.RawText))
                {
                    MarkDraftChanged();
                }
            };
        }
        ReplaceImageCommand = new AsyncRelayCommand(() => IsActive ? Session.RunCommandAsync(Session.Assets.ReplaceSelectedAsync) : Task.CompletedTask);
        EditCropCommand = new RelayCommand(() =>
        {
            if (IsActive)
            {
                Session.SetGraphicCanvasMode(CanvasEditMode.CROP);
            }
        });
        ResetCropCommand = new RelayCommand(ResetCrop);
        MatchSubtitleTimingCommand = new AsyncRelayCommand(() => IsActive ? Session.Graphics.MatchSelectedSubtitleTimingAsync() : Task.CompletedTask);
    }

    public override LayerKind Kind => LayerKind.IMAGE;
    public NumericValueDraft Width { get; } = new();
    public NumericValueDraft Height { get; } = new();
    public NumericValueDraft CropX { get; } = new();
    public NumericValueDraft CropY { get; } = new();
    public NumericValueDraft CropWidth { get; } = new();
    public NumericValueDraft CropHeight { get; } = new();
    public NumericValueDraft PlaybackOffset { get; } = new();
    public NumericValueDraft PlaybackSpeed { get; } = new();
    public ICommand ReplaceImageCommand { get; }
    public ICommand EditCropCommand { get; }
    public ICommand ResetCropCommand { get; }
    public ICommand MatchSubtitleTimingCommand { get; }
    public bool CanMatchSubtitleTiming => IsActive && Session.Graphics.CanMatchSubtitleTiming;
    public GraphicStyleChoice[] FitChoices { get; } = [new("Workbench.ImageFitFit"), new("Workbench.ImageFitFillCrop"), new("Workbench.ImageFitStretch")];
    public GraphicStyleChoice[] PlaybackModeChoices { get; } = [new("Workbench.ImagePlaybackLoop"), new("Workbench.ImagePlaybackFile"), new("Workbench.ImagePlaybackOnce")];
    public bool AspectRatioLocked
    {
        get => aspectRatioLocked;
        set
        {
            if (SetProperty(ref aspectRatioLocked, value) && value && Width.Parse() is > 0 && Height.Parse() is > 0)
            {
                aspectRatio = (double)(Width.Parse()!.Value / Height.Parse()!.Value);
            }
        }
    }
    public bool AnimationEnabled
    {
        get => animationEnabled;
        set
        {
            if (SetProperty(ref animationEnabled, value))
            {
                MarkDraftChanged();
            }
        }
    }
    public int PlaybackMode
    {
        get => playbackMode;
        set
        {
            if (Enum.IsDefined((ImagePlaybackMode)value) && SetProperty(ref playbackMode, value))
            {
                MarkDraftChanged();
            }
        }
    }
    public int Fit
    {
        get => fit;
        set
        {
            if (Enum.IsDefined((ImageFit)value) && SetProperty(ref fit, value))
            {
                MarkDraftChanged();
            }
        }
    }

    protected override void LoadCore(ProjectDocument document, ProjectLayer layer)
    {
        var image = layer.Image!;
        Width.Load(image.Width);
        Height.Load(image.Height);
        aspectRatio = image.Width / image.Height;
        Fit = (int)image.Fit;
        CropX.Load(image.Crop.X * 100);
        CropY.Load(image.Crop.Y * 100);
        CropWidth.Load(image.Crop.Width * 100);
        CropHeight.Load(image.Crop.Height * 100);
        AnimationEnabled = image.Playback is not null;
        PlaybackOffset.Load(WorkbenchSession.ToSeconds(image.Playback?.SourceOffset ?? default));
        PlaybackSpeed.Load(image.Playback?.Speed ?? 1);
        PlaybackMode = (int)(image.Playback?.Mode ?? ImagePlaybackMode.LOOP);
        RefreshLanguage();
        RefreshContext();
    }

    protected override ProjectDocument PrepareCore(ProjectDocument document, ProjectLayer layer, bool preview)
    {
        return Session.PrepareImageStyleDraft(document, layer, this);
    }

    internal void RefreshLanguage()
    {
        foreach (var choice in FitChoices.Concat(PlaybackModeChoices))
        {
            choice.RefreshLanguage();
        }
    }

    internal void RefreshContext() => OnPropertyChanged(nameof(CanMatchSubtitleTiming));

    internal override Action? CaptureNumericGesture(string? fieldKey)
    {
        if (!AspectRatioLocked || fieldKey is not ("LayerWidthInput" or "LayerHeightInput"))
        {
            return null;
        }
        var width = Width.RawText;
        var height = Height.RawText;
        return () => Session.RestoreGraphicStyleField(() =>
        {
            Width.RawText = width;
            Height.RawText = height;
        });
    }

    public override bool RestoreField(string fieldKey)
    {
        if (!IsActive || Session.SelectedLayer?.Image is not { } image ||
            fieldKey is not ("LayerWidthInput" or "LayerHeightInput" or "ImageCropXInput" or "ImageCropYInput" or
                "ImageCropWidthInput" or "ImageCropHeightInput" or "ImagePlaybackOffsetInput" or "ImagePlaybackSpeedInput"))
        {
            return false;
        }
        Session.RestoreGraphicStyleField(() =>
        {
            switch (fieldKey)
            {
                case "LayerWidthInput": Width.Load(image.Width); break;
                case "LayerHeightInput": Height.Load(image.Height); break;
                case "ImageCropXInput": CropX.Load(image.Crop.X * 100); break;
                case "ImageCropYInput": CropY.Load(image.Crop.Y * 100); break;
                case "ImageCropWidthInput": CropWidth.Load(image.Crop.Width * 100); break;
                case "ImageCropHeightInput": CropHeight.Load(image.Crop.Height * 100); break;
                case "ImagePlaybackOffsetInput": PlaybackOffset.Load(WorkbenchSession.ToSeconds(image.Playback?.SourceOffset ?? default)); break;
                case "ImagePlaybackSpeedInput": PlaybackSpeed.Load(image.Playback?.Speed ?? 1); break;
            }
        });
        return true;
    }

    private void OnWidthChanged(object? sender, PropertyChangedEventArgs e) => Resize(Width, Height, 1 / aspectRatio, e);
    private void OnHeightChanged(object? sender, PropertyChangedEventArgs e) => Resize(Height, Width, aspectRatio, e);

    private void Resize(NumericValueDraft changed, NumericValueDraft paired, double multiplier, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(NumericValueDraft.RawText) || IsLoading || Session.IsUpdating || !IsActive || resizing)
        {
            return;
        }
        if (AspectRatioLocked && changed.Parse() is > 0 && paired.Parse() is > 0 &&
            (double)changed.Parse()!.Value * multiplier is >= 0.001 and <= 1e6)
        {
            resizing = true;
            try
            {
                paired.Load((double)changed.Parse()!.Value * multiplier);
            }
            finally
            {
                resizing = false;
            }
        }
        MarkDraftChanged();
    }

    private void ResetCrop()
    {
        if (!IsActive)
        {
            return;
        }
        CropX.Load(0);
        CropY.Load(0);
        CropWidth.Load(100);
        CropHeight.Load(100);
        CommitDrafts();
    }
}
