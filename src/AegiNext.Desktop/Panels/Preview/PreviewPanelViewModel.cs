using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Controls;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;

namespace AegiNext.Desktop.Panels.Preview;

internal sealed class PreviewPanelViewModel : ObservableObject
{
    private readonly WorkbenchSession session;
    private ScenePreviewState scene = new(new(), null, MediaTime.Zero, CanvasEditMode.POSITION, Path.GetTempPath(), false);
    private string fileTitle = string.Empty;
    private string timeLabel = string.Empty;
    private string playLabel = string.Empty;
    private string emptyLabel = string.Empty;
    private bool isOpening;
    private bool canPlay;
    private bool canSeek;
    private bool hasFrame;
    private double position;
    private double duration = 1;
    private double volume = 1;
    private bool isMuted;
    private bool isPlaying;
    private bool isCatchingUp;
    private string muteLabel = string.Empty;
    private string volumeLabel = string.Empty;
    private bool isScrubbing;
    private PreviewQualityChoice[] qualityChoices = [];
    private PreviewQualityChoice? selectedQuality;
    private string qualityLabel = string.Empty;
    private PreviewCanvasToolChoice[] canvasTools = [];
    private PreviewCanvasToolChoice? selectedCanvasTool;
    private bool synchronizingCanvasTool;

    internal PreviewPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        RefreshCanvasTools();
    }

    public ScenePreviewState Scene
    {
        get => scene;
        set
        {
            if (SetProperty(ref scene, value))
            {
                SynchronizeCanvasTool();
                OnPropertyChanged(nameof(IsShapePathEditing));
                OnPropertyChanged(nameof(IsShapePathCreation));
                OnPropertyChanged(nameof(CanCreateSubtitleBackground));
                OnPropertyChanged(nameof(ShowEmptyHint));
            }
        }
    }

    public PreviewCanvasToolChoice[] CanvasTools
    {
        get => canvasTools;
        private set => SetProperty(ref canvasTools, value);
    }

    public PreviewCanvasToolChoice? SelectedCanvasTool
    {
        get => selectedCanvasTool;
        set
        {
            if (!SetProperty(ref selectedCanvasTool, value) || synchronizingCanvasTool || value is null)
            {
                return;
            }
            if (value.ShapeKind is { } kind)
            {
                session.SceneEditing.CreationShapeKind = kind;
            }
            session.SetGraphicCanvasMode(value.Mode);
            SynchronizeCanvasTool();
            OnPropertyChanged(nameof(SelectedCanvasTool));
        }
    }

    public bool IsShapePathEditing => Scene.Mode == CanvasEditMode.SHAPE_PATH;
    public bool IsShapePathCreation => Scene.Mode == CanvasEditMode.CREATE_PATH;
    public bool CanCreateSubtitleBackground => Scene.SelectedLayer?.SubtitleId is not null;
    public bool ShowEmptyHint => !HasFrame && Scene.Document.Layers.IsEmpty;

    private void RefreshCanvasTools()
    {
        synchronizingCanvasTool = true;
        CanvasTools =
        [
            new(CanvasEditMode.POSITION, null, Localization.Get("Preview.SelectGraphic")),
            new(CanvasEditMode.CREATE_SHAPE, ShapeKind.RECTANGLE, Localization.Get("Preview.CreateRectangle")),
            new(CanvasEditMode.CREATE_SHAPE, ShapeKind.ROUNDED_RECTANGLE, Localization.Get("Preview.CreateRoundedRectangle")),
            new(CanvasEditMode.CREATE_SHAPE, ShapeKind.ELLIPSE, Localization.Get("Preview.CreateEllipse")),
            new(CanvasEditMode.CREATE_SHAPE, ShapeKind.LINE, Localization.Get("Preview.CreateLine")),
            new(CanvasEditMode.CREATE_SHAPE, ShapeKind.ARROW, Localization.Get("Preview.CreateArrow")),
            new(CanvasEditMode.CREATE_PATH, ShapeKind.PATH, Localization.Get("Preview.CreatePath")),
            new(CanvasEditMode.CROP, null, Localization.Get("Preview.CropImage")),
            new(CanvasEditMode.SHAPE_PATH, null, Localization.Get("Preview.EditShapePath"))
        ];
        SynchronizeCanvasTool();
        synchronizingCanvasTool = false;
    }

    private void SynchronizeCanvasTool()
    {
        synchronizingCanvasTool = true;
        try
        {
            SelectedCanvasTool = CanvasTools.FirstOrDefault(tool => tool.Mode == Scene.Mode &&
                (tool.Mode != CanvasEditMode.CREATE_SHAPE || tool.ShapeKind == session.SceneEditing.CreationShapeKind));
        }
        finally
        {
            synchronizingCanvasTool = false;
        }
    }

    public PreviewQualityChoice[] QualityChoices
    {
        get => qualityChoices;
        private set => SetProperty(ref qualityChoices, value);
    }

    public PreviewQualityChoice? SelectedQuality
    {
        get => selectedQuality;
        set => SetProperty(ref selectedQuality, value);
    }

    public string QualityLabel
    {
        get => qualityLabel;
        private set => SetProperty(ref qualityLabel, value);
    }

    internal void RefreshQualities(PreviewQuality quality)
    {
        QualityChoices =
        [
            new(PreviewQuality.LOWEST, Localization.Get("Preview.QualityLowest")),
            new(PreviewQuality.LOW, Localization.Get("Preview.QualityLow")),
            new(PreviewQuality.STANDARD, Localization.Get("Preview.QualityStandard")),
            new(PreviewQuality.HIGH, Localization.Get("Preview.QualityHigh"))
        ];
        SelectedQuality = QualityChoices.Single(choice => choice.Id == quality);
        QualityLabel = Localization.Get("Preview.Quality");
        RefreshCanvasTools();
    }

    internal bool BeginCanvasGesture() => session.BeginCanvasGesture();
    internal void CancelCanvasGesture() => session.CancelCanvasGesture();
    internal Task CommitCanvasAsync(CanvasLayerEditEventArgs value) => session.CommitCanvasAsync(value);
    internal void ReportRenderingError(Exception error)
    {
        if (session.IsClosing)
        {
            return;
        }

        session.SetDiagnosticError("Video editing preview", error);
        session.ShowError(error, false);
    }
    internal void ReportRenderingRecovery()
    {
        if (!session.IsClosing)
        {
            session.SetDiagnosticError("Video editing preview", null);
        }
    }

    public string FileTitle
    {
        get => fileTitle;
        set => SetProperty(ref fileTitle, value);
    }

    public string TimeLabel
    {
        get => timeLabel;
        set => SetProperty(ref timeLabel, value);
    }

    public string PlayLabel
    {
        get => playLabel;
        set => SetProperty(ref playLabel, value);
    }

    public string MuteLabel
    {
        get => muteLabel;
        set => SetProperty(ref muteLabel, value);
    }

    public string VolumeLabel
    {
        get => volumeLabel;
        set => SetProperty(ref volumeLabel, value);
    }

    public bool IsPlaying
    {
        get => isPlaying;
        set => SetProperty(ref isPlaying, value);
    }

    public bool IsCatchingUp
    {
        get => isCatchingUp;
        set => SetProperty(ref isCatchingUp, value);
    }

    public string EmptyLabel
    {
        get => emptyLabel;
        set => SetProperty(ref emptyLabel, value);
    }

    public bool IsOpening
    {
        get => isOpening;
        set => SetProperty(ref isOpening, value);
    }

    public bool CanPlay
    {
        get => canPlay;
        set => SetProperty(ref canPlay, value);
    }

    public bool CanSeek
    {
        get => canSeek;
        set => SetProperty(ref canSeek, value);
    }

    public bool HasFrame
    {
        get => hasFrame;
        set
        {
            if (SetProperty(ref hasFrame, value))
            {
                OnPropertyChanged(nameof(ShowEmptyHint));
            }
        }
    }

    public double Position
    {
        get => position;
        set => SetProperty(ref position, value);
    }

    public double Duration
    {
        get => duration;
        set => SetProperty(ref duration, value);
    }

    public double Volume
    {
        get => volume;
        set => SetProperty(ref volume, value);
    }

    public bool IsMuted
    {
        get => isMuted;
        set => SetProperty(ref isMuted, value);
    }

    public bool IsScrubbing
    {
        get => isScrubbing;
        set => SetProperty(ref isScrubbing, value);
    }

    public ICommand PlayCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.PLAY_PAUSE);
    /// <summary>提交工程相对时间的播放定位请求。</summary>
    public Task SeekAsync(MediaTime target) => session.SeekProjectTimeAsync(target);
}
