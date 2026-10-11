using System.Reflection;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.Panels.Log;
using AegiNext.Desktop.Panels.Export;
using AegiNext.Desktop.Panels.Preview;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Panels.Subtitles;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;

namespace AegiNext.Desktop.Tests.Workspace;

public sealed class WorkspaceViewModelBoundaryTests
{
    [Fact]
    public void SevenFunctionalPanelModelsAndSharedSceneStateDoNotHoldControlsWindowsOrDockObjects()
    {
        foreach (var type in new[]
                 {
                     typeof(WorkbenchViewModel), typeof(PreviewPanelViewModel), typeof(TimelinePanelViewModel),
                     typeof(SubtitlesPanelViewModel), typeof(StylesPanelViewModel), typeof(SubtitleStyleViewModel), typeof(ImageStyleViewModel), typeof(ShapeStyleViewModel), typeof(EffectsPanelViewModel),
                     typeof(ExportPanelViewModel), typeof(LogPanelViewModel), typeof(SceneEditingState), typeof(ScenePreviewState)
                 })
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(value => value.FieldType);
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(value => value.PropertyType);
            Assert.All(fields.Concat(properties), memberType =>
            {
                Assert.False(typeof(Avalonia.Media.Imaging.Bitmap).IsAssignableFrom(memberType), $"{type.Name} owns a bitmap.");
                Assert.False(typeof(Control).IsAssignableFrom(memberType), $"{type.Name} owns {memberType.Name}.");
                Assert.False(memberType.Namespace?.StartsWith("Dock.", StringComparison.Ordinal) == true,
                    $"{type.Name} owns Dock {memberType.Name}.");
            });
        }
    }
}
