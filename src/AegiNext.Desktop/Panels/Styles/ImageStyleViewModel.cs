using System.ComponentModel;
using System.Windows.Input;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed class ImageStyleViewModel : GraphicStyleEditorViewModel
{
    private int fit;
    private int playbackMode;
    private bool animationEnabled;
    private bool aspectRatioLocked = true;
    private bool resizing;
    private double aspectRatio = 1;
    private string? resizeDriver;

    internal ImageStyleViewModel(WorkbenchSession session) : base(session)
    {
        Width.PropertyChanged += OnWidthChanged;
        Height.PropertyChanged += OnHeightChanged;
        foreach (var (field, draft) in new (string, NumericValueDraft)[]
                 {
                     (nameof(CropX), CropX), (nameof(CropY), CropY), (nameof(CropWidth), CropWidth),
                     (nameof(CropHeight), CropHeight), (nameof(PlaybackOffset), PlaybackOffset), (nameof(PlaybackSpeed), PlaybackSpeed)
                 })
        {
            ObserveGraphicNumber(draft, field);
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
    internal string? ResizeDriver => resizeDriver;
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
            if (SetProperty(ref aspectRatioLocked, value))
            {
                if (value && Width.Parse() is > 0 && Height.Parse() is > 0)
                {
                    aspectRatio = (double)(Width.Parse()!.Value / Height.Parse()!.Value);
                }
                if (!value)
                {
                    resizeDriver = null;
                }
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
                MarkGraphicFieldChanged(nameof(AnimationEnabled));
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
                MarkGraphicFieldChanged(nameof(PlaybackMode));
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
                MarkGraphicFieldChanged(nameof(Fit));
            }
        }
    }

    internal override ScenePoint PositionSize(ProjectLayer layer) => new(
        Width.Parse() is > 0 and <= 1000000 ? (double)Width.Parse()!.Value : layer.Image!.Width,
        Height.Parse() is > 0 and <= 1000000 ? (double)Height.Parse()!.Value : layer.Image!.Height);

    protected override void LoadGraphicCore(ProjectDocument document, ProjectLayer layer)
    {
        resizeDriver = null;
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

    protected override ProjectDocument PrepareGraphicCore(ProjectDocument document, ProjectLayer layer, bool preview)
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

    /// <inheritdoc />
    public override void Accept()
    {
        resizeDriver = null;
        base.Accept();
    }

    internal override Action CaptureDraftIntent()
    {
        var restore = base.CaptureDraftIntent();
        var driver = resizeDriver;
        return () =>
        {
            restore();
            resizeDriver = driver;
        };
    }

    internal override Action? CaptureNumericGesture(string? fieldKey)
    {
        var driver = resizeDriver;
        var restore = AspectRatioLocked && fieldKey is ("LayerWidthInput" or "LayerHeightInput")
            ? CaptureGraphicNumbers((nameof(Width), Width), (nameof(Height), Height)) : fieldKey switch
        {
            "LayerWidthInput" => CaptureGraphicNumbers((nameof(Width), Width)),
            "LayerHeightInput" => CaptureGraphicNumbers((nameof(Height), Height)),
            "ImageCropXInput" => CaptureGraphicNumbers((nameof(CropX), CropX)),
            "ImageCropYInput" => CaptureGraphicNumbers((nameof(CropY), CropY)),
            "ImageCropWidthInput" => CaptureGraphicNumbers((nameof(CropWidth), CropWidth)),
            "ImageCropHeightInput" => CaptureGraphicNumbers((nameof(CropHeight), CropHeight)),
            "ImagePlaybackOffsetInput" => CaptureGraphicNumbers((nameof(PlaybackOffset), PlaybackOffset)),
            "ImagePlaybackSpeedInput" => CaptureGraphicNumbers((nameof(PlaybackSpeed), PlaybackSpeed)),
            _ => null
        };
        if (restore is null)
        {
            return null;
        }
        return () =>
        {
            restore();
            resizeDriver = driver;
        };
    }

    protected override bool RestoreGraphicField(string fieldKey)
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
        ForgetDraftField(fieldKey switch
        {
            "LayerWidthInput" => nameof(Width),
            "LayerHeightInput" => nameof(Height),
            "ImageCropXInput" => nameof(CropX),
            "ImageCropYInput" => nameof(CropY),
            "ImageCropWidthInput" => nameof(CropWidth),
            "ImageCropHeightInput" => nameof(CropHeight),
            "ImagePlaybackOffsetInput" => nameof(PlaybackOffset),
            "ImagePlaybackSpeedInput" => nameof(PlaybackSpeed),
            _ => throw new ArgumentOutOfRangeException(nameof(fieldKey))
        });
        if (fieldKey is "LayerWidthInput" or "LayerHeightInput")
        {
            resizeDriver = null;
        }
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
        RecordDraftField(ReferenceEquals(changed, Width) ? nameof(Width) : nameof(Height));
        resizeDriver = null;
        if (AspectRatioLocked && changed.Parse() is > 0 && paired.Parse() is > 0 &&
            (double)changed.Parse()!.Value * multiplier is >= 0.001 and <= 1e6)
        {
            resizing = true;
            try
            {
                paired.Load((double)changed.Parse()!.Value * multiplier);
                RecordDraftField(ReferenceEquals(paired, Width) ? nameof(Width) : nameof(Height));
                resizeDriver = ReferenceEquals(changed, Width) ? nameof(Width) : nameof(Height);
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
        foreach (var field in new[] { nameof(CropX), nameof(CropY), nameof(CropWidth), nameof(CropHeight) })
        {
            RecordDraftField(field);
        }
        MarkDraftChanged();
        CommitDrafts();
    }
}
