using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Styles;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal ProjectDocument PrepareImageStyleDraft(ProjectDocument document, ProjectLayer layer, ImageStyleViewModel vm)
    {
        var image = layer.Image!;
        var width = ReadGraphicNumber(vm.Width, image.Width, "Size", "LayerWidthInput", 0.001m, 1000000m);
        var height = ReadGraphicNumber(vm.Height, image.Height, "Size", "LayerHeightInput", 0.001m, 1000000m);
        var crop = new NormalizedImageCrop(
            ReadGraphicPercent(vm.CropX, image.Crop.X, "ImageCropXInput"),
            ReadGraphicPercent(vm.CropY, image.Crop.Y, "ImageCropYInput"),
            ReadGraphicPercent(vm.CropWidth, image.Crop.Width, "ImageCropWidthInput"),
            ReadGraphicPercent(vm.CropHeight, image.Crop.Height, "ImageCropHeightInput"));
        RequireGraphicField(crop.Width > 0 && crop.Width <= 1 - crop.X, "ImageCropWidthInput", "ImageCrop");
        RequireGraphicField(crop.Height > 0 && crop.Height <= 1 - crop.Y, "ImageCropHeightInput", "ImageCrop");
        ImagePlayback? playback = null;
        if (vm.AnimationEnabled)
        {
            var original = image.Playback ?? new();
            var originalSeconds = ToSeconds(original.SourceOffset);
            var offset = ReadGraphicNumber(vm.PlaybackOffset, originalSeconds, "ImagePlaybackOffset", "ImagePlaybackOffsetInput", -1000000000m, 1000000000m);
            var speed = ReadGraphicNumber(vm.PlaybackSpeed, original.Speed, "ImagePlaybackSpeed", "ImagePlaybackSpeedInput", 0m, decimal.MaxValue);
            RequireGraphicField(speed > 0, "ImagePlaybackSpeedInput", "ImagePlaybackSpeed");
            playback = original with
            {
                SourceOffset = offset == originalSeconds ? original.SourceOffset : MediaTime.FromTimeSpan(TimeSpan.FromSeconds(offset)),
                Speed = speed, Mode = (ImagePlaybackMode)vm.PlaybackMode
            };
        }
        return WorkspaceDraftOperations.UpdateLayer(document, layer.Id, current => current with
        {
            Image = current.Image! with { Width = width, Height = height, Fit = (ImageFit)vm.Fit, Crop = crop, Playback = playback }
        });
    }

    private LayerShape ReadShapeGeometry(LayerShape original, ShapeStyleViewModel vm)
    {
        var width = ReadGraphicNumber(vm.Width, original.Width, "Size", "LayerWidthInput", 0.001m, 1000000m);
        var height = ReadGraphicNumber(vm.Height, original.Height, "Size", "LayerHeightInput", 0.001m, 1000000m);
        var shape = ShapeGeometry.Resize(original, width, height) with { Kind = vm.SelectedKind };
        if (vm.IsRoundedRectangle)
        {
            shape = shape with { CornerRadius = ReadGraphicNumber(vm.CornerRadius, original.CornerRadius,
                "ShapeCornerRadius", "ShapeCornerRadiusInput", 0m, 1000000m) };
        }
        if (vm.IsArrow)
        {
            var head = ReadGraphicPercent(vm.ArrowHead, original.ArrowHeadRatio, "ShapeArrowHeadInput");
            var shaft = ReadGraphicPercent(vm.ArrowShaft, original.ArrowShaftRatio, "ShapeArrowShaftInput");
            RequireGraphicField(head is > 0 and < 1, "ShapeArrowHeadInput", "ShapeArrowHead");
            RequireGraphicField(shaft is > 0 and <= 1, "ShapeArrowShaftInput", "ShapeArrowShaft");
            shape = shape with { ArrowHeadRatio = head, ArrowShaftRatio = shaft };
        }
        return shape;
    }

    private double ReadGraphicPercent(NumericValueDraft draft, double original, string field)
    {
        var value = ReadGraphicNumber(draft, original * 100, field.StartsWith("Image", StringComparison.Ordinal) ? "ImageCrop" : "ShapeParameters", field, 0m, 100m);
        return value == original * 100 ? original : value / 100;
    }

    private double ReadGraphicNumber(NumericValueDraft draft, double original, string label, string field, decimal minimum, decimal maximum)
    {
        ViewModel.InvalidPanelId = "styles";
        ViewModel.InvalidFieldKey = field;
        if (!decimal.TryParse(draft.RawText, NumberStyles.Float, InterfaceCulture, out var number) || number < minimum || number > maximum)
        {
            throw new InvalidDataException(Localization.Get("Workbench." + label));
        }
        return number == (decimal)original ? original : (double)number;
    }

    private void RequireGraphicField(bool valid, string field, string label)
    {
        if (!valid)
        {
            ViewModel.InvalidPanelId = "styles";
            ViewModel.InvalidFieldKey = field;
            throw new InvalidDataException(Localization.Get("Workbench." + label));
        }
    }
}
