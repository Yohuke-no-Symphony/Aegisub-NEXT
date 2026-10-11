using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed class ImageStyleViewModel : StyleEditorViewModel
{
    private int fit;

    internal ImageStyleViewModel(WorkbenchSession session) : base(session)
    {
        Width.PropertyChanged += (_, _) => MarkDraftChanged();
        Height.PropertyChanged += (_, _) => MarkDraftChanged();
    }

    public override LayerKind Kind => LayerKind.IMAGE;
    public NumericValueDraft Width { get; } = new();
    public NumericValueDraft Height { get; } = new();
    public string[] FitChoices { get; private set; } = [];
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
        Fit = (int)image.Fit;
        RefreshLanguage();
    }

    protected override ProjectDocument PrepareCore(ProjectDocument document, ProjectLayer layer, bool preview)
    {
        var image = layer.Image!;
        var width = Session.ReadStyleNumber(Width.RawText, image.Width, "Size", "LayerWidthInput");
        var height = Session.ReadStyleNumber(Height.RawText, image.Height, "Size", "LayerHeightInput");
        return WorkspaceDraftOperations.UpdateLayer(document, layer.Id, current => current with
        {
            Image = current.Image! with { Width = width, Height = height, Fit = (ImageFit)Fit }
        });
    }

    internal void RefreshLanguage()
    {
        FitChoices = [Localization.Get("Workbench.ImageFitFit"), Localization.Get("Workbench.ImageFitFillCrop"), Localization.Get("Workbench.ImageFitStretch")];
        OnPropertyChanged(nameof(FitChoices));
    }

    public override bool RestoreField(string fieldKey)
    {
        if (!IsActive || Session.SelectedLayer?.Image is not { } image || fieldKey is not ("LayerWidthInput" or "LayerHeightInput"))
        {
            return false;
        }
        Session.RestoreGraphicStyleField(() =>
        {
            if (fieldKey == "LayerWidthInput")
            {
                Width.Load(image.Width);
            }
            else
            {
                Height.Load(image.Height);
            }
        });
        return true;
    }
}
