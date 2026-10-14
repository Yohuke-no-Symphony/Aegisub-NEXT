using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Masks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证蒙版定位和几何字段共用标题及 X/Y 输入列。</summary>
public sealed class MaskFieldAlignmentUiTests
{
    /// <summary>矩形角点和矢量节点在宽窄、语言及主题变化时与定位字段对齐。</summary>
    [AvaloniaTheory]
    [InlineData(260, "zh-CN", true)]
    [InlineData(650, "en-US", false)]
    public async Task GeometryTitlesAndVectorColumnsAlignWithAnchorPivotAndOffset(int width, string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        var node = new MaskNode { Position = new(40, 60) };
        using var masks = new MaskPanelView(context.ViewModel.Masks, context.Session);
        var host = new Window
        {
            Width = width,
            Height = 1100,
            Content = masks,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        host.Show();
        try
        {
            foreach (var mask in new ClipMask[]
            {
                new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(400, 300) },
                new VectorClipMask { Contours = [new() { Nodes = [node] }] }
            })
            {
                context.Session.Editor.SetClipMask(context.Session.SelectedLayer!.Id, mask);
                if (mask is VectorClipMask)
                {
                    context.Session.MaskEditing.SelectNode(node.Id);
                }
                Flush(host);
                var source = context.Session.DocumentSnapshot;
                var placement = UiTestActions.Find<LayerPositionEditor>(masks, "MaskPositionEditor");
                var anchor = placement.FindControl<VectorDraftInput>("AnchorInput")!;
                var anchorTitle = Assert.Single(placement.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Classes.Contains("field") && text.Text == Localization.Get("Workbench.Anchor"));
                var referenceInputs = Inputs(anchor);
                foreach (var vector in new[]
                {
                    placement.FindControl<VectorDraftInput>("PivotInput")!,
                    placement.FindControl<VectorDraftInput>("OffsetInput")!
                })
                {
                    AssertColumns(masks, referenceInputs, Inputs(vector));
                }
                var rows = masks.GetVisualDescendants().OfType<AnimationPropertyRowControl>().ToArray();
                Assert.NotEmpty(rows);
                foreach (var row in rows)
                {
                    var title = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.IsEffectivelyVisible && text.Text == row.Label);
                    Assert.Equal(Left(masks, anchorTitle), Left(masks, title), 3);
                    var vector = Assert.Single(row.GetVisualDescendants().OfType<VectorDraftInput>());
                    AssertColumns(masks, referenceInputs, Inputs(vector));
                }
                Assert.Same(source, context.Session.DocumentSnapshot);
            }
        }
        finally
        {
            host.Close();
        }
    }

    private static NumericDraftInput[] Inputs(VectorDraftInput vector) =>
        vector.GetVisualDescendants().OfType<NumericDraftInput>()
            .OrderBy(input => input.TranslatePoint(new(0, 0), vector)!.Value.X).ToArray();

    private static void AssertColumns(Control root, NumericDraftInput[] expected, NumericDraftInput[] actual)
    {
        Assert.Equal(2, expected.Length);
        Assert.Equal(2, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(Left(root, expected[index]), Left(root, actual[index]), 3);
            Assert.Equal(expected[index].Bounds.Width, actual[index].Bounds.Width, 3);
        }
    }

    private static double Left(Control root, Control control) => control.TranslatePoint(new(0, 0), root)!.Value.X;

    private static void Flush(Window host)
    {
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
