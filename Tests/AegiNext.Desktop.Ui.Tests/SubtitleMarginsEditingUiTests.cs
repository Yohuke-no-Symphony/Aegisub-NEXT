using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleMarginsEditingUiTests
{
    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("zh-CN", true)]
    public async Task ThreeMarginInputsPreviewThenCommitTogetherAndUndoRedoReloadsTheMountedDraft(string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(preferences => preferences with { Language = language });
        Assert.Equal(language, Localization.CurrentLanguageID);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Asymmetric margins\nSecond line");
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var sink = new TextBox();
        var content = new Grid { RowDefinitions = new("*,Auto") };
        content.Children.Add(view);
        Grid.SetRow(sink, 1);
        content.Children.Add(sink);
        var host = new Window
        {
            Width = 320, Height = 1100, Content = content,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            host.Show();
            var editor = UiTestActions.Find<SubtitleMarginsEditor>(host, "MarginsEditor");
            var left = UiTestActions.Find<NumericDraftInput>(editor, "MarginLeftInput");
            var right = UiTestActions.Find<NumericDraftInput>(editor, "MarginRightInput");
            var vertical = UiTestActions.Find<NumericDraftInput>(editor, "MarginVerticalInput");
            Type(host, left, "7e-");
            UiTestActions.Press(host, Key.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Text(right).IsFocused);
            Type(host, right, "39");
            UiTestActions.Press(host, Key.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Text(vertical).IsFocused);
            Type(host, vertical, "13");
            Assert.True(sink.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal("7e-", left.RawText);
            Assert.Equal("MarginLeftInput", context.ViewModel.InvalidFieldKey);

            Type(host, left, "17");

            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.Equal(new SubtitleMargins(17, 39, 13), context.Session.PreviewDocument.Subtitles[0].Style.Margins);
            UiTestActions.Press(host, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new SubtitleMargins(17, 39, 13), context.Session.SelectedCue!.Style.Margins);
            Assert.Null(context.Session.SelectedCue.Style.Position);
            Assert.True(context.Session.Editor.Undo());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal((decimal)original.Subtitles[0].Style.Margins.Left, left.Value);
            Assert.Equal((decimal)original.Subtitles[0].Style.Margins.Right, right.Value);
            Assert.Equal((decimal)original.Subtitles[0].Style.Margins.Vertical, vertical.Value);
            Assert.True(context.Session.Editor.Redo());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("17", left.RawText);
            Assert.Equal("39", right.RawText);
            Assert.Equal("13", vertical.RawText);
            Assert.Equal(context.ViewModel.Styles.Subtitle.Margins.Left.RawText, left.RawText);
            Assert.Equal(context.ViewModel.Styles.Subtitle.Margins.Right.RawText, right.RawText);
            Assert.Equal(context.ViewModel.Styles.Subtitle.Margins.Vertical.RawText, vertical.RawText);
            Assert.Equal(language, Localization.CurrentLanguageID);
            Assert.Equal(language, context.Session.Preferences.Language);
            AssertInputsFit(host, editor, left, right, vertical);
            Capture(host, UiTestActions.Find<SubtitlePositionDiagram>(host, "PositionDiagram"),
                $"panel-margins-{language}-{(dark ? "dark" : "light")}.png");
        }
        finally
        {
            RestoreMargins(context);
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task EscapeRestoresOnlyTheFocusedMarginAndKeepsOtherEdgesPending()
    {
        await using var context = new MainWindowTestContext();
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Margin Escape");
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1100, Content = view };
        try
        {
            host.Show();
            var editor = UiTestActions.Find<SubtitleMarginsEditor>(host, "MarginsEditor");
            var left = UiTestActions.Find<NumericDraftInput>(editor, "MarginLeftInput");
            var right = UiTestActions.Find<NumericDraftInput>(editor, "MarginRightInput");
            var vertical = UiTestActions.Find<NumericDraftInput>(editor, "MarginVerticalInput");
            Type(host, right, "7e-");
            Type(host, left, "17");
            Type(host, vertical, "13");
            Assert.True(Text(right).Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.False(context.Session.TryCommitDrafts(false));

            UiTestActions.Press(host, Key.Escape);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal((decimal)original.Subtitles[0].Style.Margins.Right, right.Value);
            Assert.Equal("17", left.RawText);
            Assert.Equal("13", vertical.RawText);
            Assert.Equal(new SubtitleMargins(17, original.Subtitles[0].Style.Margins.Right, 13),
                context.Session.PreviewDocument.Subtitles[0].Style.Margins);
            UiTestActions.Press(host, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new SubtitleMargins(17, original.Subtitles[0].Style.Margins.Right, 13),
                context.Session.SelectedCue!.Style.Margins);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            RestoreMargins(context);
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData("en-US", "zh-CN", false)]
    [InlineData("zh-CN", "en-US", true)]
    public async Task LiveLanguageSwitchPreservesInvalidRawMarginsAndNarrowPanelGeometry(string initialLanguage,
        string targetLanguage, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        Localization.SetLanguage(initialLanguage);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Margin localization");
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window
        {
            Width = 320, Height = 1100, Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            host.Show();
            var editor = UiTestActions.Find<SubtitleMarginsEditor>(host, "MarginsEditor");
            var left = UiTestActions.Find<NumericDraftInput>(editor, "MarginLeftInput");
            var right = UiTestActions.Find<NumericDraftInput>(editor, "MarginRightInput");
            var vertical = UiTestActions.Find<NumericDraftInput>(editor, "MarginVerticalInput");
            Type(host, right, "32769");
            Type(host, left, "17");
            Assert.False(context.Session.TryCommitDrafts(false));

            Localization.SetLanguage(targetLanguage);
            Flush(host);

            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal("32769", right.RawText);
            Assert.Equal("17", left.RawText);
            Assert.Equal("MarginRightInput", context.ViewModel.InvalidFieldKey);
            Assert.Equal(targetLanguage, Localization.CurrentLanguageID);
            Assert.False(context.Session.TryCommitDrafts(false));
            AssertInputsFit(host, editor, left, right, vertical);
            Assert.Equal(0m, left.Minimum);
            Assert.Equal(32768m, left.Maximum);
            Assert.Equal(left.Minimum, right.Minimum);
            Assert.Equal(left.Maximum, vertical.Maximum);
        }
        finally
        {
            RestoreMargins(context);
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task MarginValidationFocusesTheNestedInputAndPreventsSelectionReplacement()
    {
        await using var context = new MainWindowTestContext();
        var first = context.Session.Editor.AddSubtitle(new(0), new(4), "First");
        var second = context.Session.Editor.AddSubtitle(new(5), new(9), "Second");
        context.Session.Editor.UpdateSubtitle(second, cue => cue with { Style = cue.Style with { Margins = new(0, 23, 0) } });
        context.Session.SelectCue(first);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var window = context.Window;
        var vertical = UiTestActions.Find<NumericDraftInput>(window, "MarginVerticalInput");
        try
        {
            Type(window, vertical, "7e-");

            context.Session.SelectCue(second);
            Flush(window);

            Assert.Equal(first, context.Session.SelectedCueId);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal("styles", context.ViewModel.InvalidPanelId);
            Assert.Equal("MarginVerticalInput", context.ViewModel.InvalidFieldKey);
            Assert.True(Text(vertical).IsFocused);
            UiTestActions.Press(window, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            context.Session.SelectCue(second);
            Flush(window);
            Assert.Equal(second, context.Session.SelectedCueId);
            Assert.Equal("0", UiTestActions.Find<NumericDraftInput>(window, "MarginLeftInput").RawText);
            Assert.Equal("23", UiTestActions.Find<NumericDraftInput>(window, "MarginRightInput").RawText);
            Assert.Equal("0", vertical.RawText);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            RestoreMargins(context);
        }
    }

    [AvaloniaFact]
    public async Task ShapeSelectionHidesAllSubtitleMarginInputs()
    {
        await using var context = new MainWindowTestContext();
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 100), End = new(4)
        };
        context.Session.Editor.AddLayer(shape);
        context.Session.SelectLayer(shape.Id, [shape.Id]);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1100, Content = view };
        try
        {
            host.Show();
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<Control>(),
                control => control.Name is "MarginsEditor" or "MarginLeftInput" or "MarginRightInput" or "MarginVerticalInput");
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ExplicitPositionDisablesOnlyVerticalMarginAndUndoRedoRestoresTheInputState()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(preferences => preferences with { Language = "en-US" });
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Explicit margin input state");
        context.Session.Editor.UpdateSubtitle(cueId, line => line with
        {
            Style = line.Style with { Position = new(), Margins = new(17, 39, 13) }
        });
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1100, Content = view };
        try
        {
            host.Show();
            var editor = UiTestActions.Find<SubtitleMarginsEditor>(host, "MarginsEditor");
            var left = UiTestActions.Find<NumericDraftInput>(editor, "MarginLeftInput");
            var right = UiTestActions.Find<NumericDraftInput>(editor, "MarginRightInput");
            var vertical = UiTestActions.Find<NumericDraftInput>(editor, "MarginVerticalInput");
            Assert.True(editor.IsExplicit);
            Assert.True(left.IsEffectivelyEnabled);
            Assert.True(right.IsEffectivelyEnabled);
            Assert.False(vertical.IsEffectivelyEnabled);
            Assert.Equal("13", vertical.RawText);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);

            var automatic = UiTestActions.Find<RadioButton>(host, "AutomaticPositionMode");
            var custom = UiTestActions.Find<RadioButton>(host, "CustomPositionMode");
            Assert.True(custom.IsChecked);
            automatic.BringIntoView();
            Flush(host);
            AssertPointerHits(host, automatic);
            UiTestActions.Click(host, "AutomaticPositionMode");
            Flush(host);

            Assert.False(editor.IsExplicit);
            Assert.True(automatic.IsChecked);
            Assert.False(custom.IsChecked);
            Assert.True(vertical.IsEffectivelyEnabled);
            Assert.Equal("13", vertical.RawText);
            var automaticSnapshot = context.Session.Editor.Snapshot;
            Assert.NotSame(original, automaticSnapshot);
            Assert.True(context.Session.TryCommitDrafts(false));
            Assert.Same(automaticSnapshot, context.Session.Editor.Snapshot);
            Assert.Null(context.Session.SelectedCue!.Style.Position);
            Assert.Equal(new SubtitleMargins(17, 39, 13), context.Session.SelectedCue.Style.Margins);
            Assert.True(context.Session.Editor.Undo());
            Flush(host);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.True(editor.IsExplicit);
            Assert.True(custom.IsChecked);
            Assert.False(automatic.IsChecked);
            Assert.False(vertical.IsEffectivelyEnabled);
            Assert.Equal("13", vertical.RawText);
            Assert.True(context.Session.Editor.Redo());
            Flush(host);
            Assert.False(editor.IsExplicit);
            Assert.True(automatic.IsChecked);
            Assert.False(custom.IsChecked);
            Assert.True(vertical.IsEffectivelyEnabled);
            Assert.Equal("13", vertical.RawText);
            Assert.True(left.IsEffectivelyEnabled);
            Assert.True(right.IsEffectivelyEnabled);
        }
        finally
        {
            RestoreMargins(context);
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task EnablingExplicitPositionAfterMarginPreviewKeepsTheMeasuredGeometryAndPixels()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        await context.OpenMediaAsync();
        await context.Window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        context.Session.Editor.Apply("Margin position fixture", document => document with
        {
            Width = 256, Height = 160,
            Subtitles = [Assert.Single(document.Subtitles) with
            {
                Text = "ABC\nD", Style = Assert.Single(document.Subtitles).Style with
                {
                    FontFamily = "sans-serif", FontAssetId = null, FontSize = 22, Position = null,
                    Margins = new(8, 8, 8), StrokeWidth = 0, ShadowColor = SceneColor.Transparent
                }
            }]
        });
        var cue = Assert.Single(context.Session.Editor.Snapshot.Subtitles);
        context.Session.SelectCue(cue.Id);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var styles = context.ViewModel.Styles.Subtitle;
        styles.Margins.Left.RawText = "17";
        styles.Margins.Right.RawText = "39";
        styles.Margins.Vertical.RawText = "13";
        Dispatcher.UIThread.RunJobs();
        var preview = context.Session.PreviewDocument;
        var layerId = Assert.Single(preview.Layers).Id;
        Assert.Equal(new SubtitleMargins(17, 39, 13), preview.Subtitles[0].Style.Margins);
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(context.Session.ProjectDirectory));
        var measured = renderer.MeasureSubtitlePlacement(preview, preview.Subtitles[0]);
        var geometry = renderer.GetLayerGeometry(preview, MediaTime.Zero, layerId);
        var pixels = Pixels(renderer, preview);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.Null(preview.Subtitles[0].Style.Position);
        Assert.Equal((decimal)measured.Position.Offset.X, styles.Position.OffsetX.Value);
        Assert.Equal((decimal)measured.Position.Offset.Y, styles.Position.OffsetY.Value);
        var check = UiTestActions.Find<RadioButton>(context.Window, "CustomPositionMode");
        check.BringIntoView();
        Flush(context.Window);
        AssertPointerHits(context.Window, check);
        var point = check.TranslatePoint(new(check.Bounds.Width / 2, check.Bounds.Height / 2), context.Window);
        Assert.NotNull(point);

        context.Window.MouseDown(point.Value, MouseButton.Left);
        context.Window.MouseUp(point.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var changed = context.Session.Editor.Snapshot;
        Assert.Equal(measured.Position, changed.Subtitles[0].Style.Position);
        Assert.Equal(new SubtitleMargins(17, 39, 13), changed.Subtitles[0].Style.Margins);
        Assert.Equal(geometry, renderer.GetLayerGeometry(changed, MediaTime.Zero, layerId));
        Assert.Equal(pixels, Pixels(renderer, changed));
        Assert.Equal("17", styles.Margins.Left.RawText);
        Assert.Equal("39", styles.Margins.Right.RawText);
        Assert.Equal("13", styles.Margins.Vertical.RawText);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task ExplicitHorizontalMarginPreviewRefreshesTheMergedGeometryWithoutReloadingPositionRawText()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(preferences => preferences with { Language = "en-US" });
        await context.OpenMediaAsync();
        await context.Window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        context.Session.Editor.Apply("Explicit wrapping fixture", document => document with
        {
            Width = 256, Height = 160,
            Subtitles = [Assert.Single(document.Subtitles) with
            {
                Text = "AAAAAAAAAAAAAA", Style = Assert.Single(document.Subtitles).Style with
                {
                    FontFamily = "sans-serif", FontAssetId = null, FontSize = 22,
                    Position = new() { Offset = new(0, -20) },
                    Margins = new(8, 8, 13), StrokeWidth = 0, ShadowColor = SceneColor.Transparent
                }
            }]
        });
        context.Session.SelectCue(Assert.Single(context.Session.Editor.Snapshot.Subtitles).Id);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1100, Content = view };
        try
        {
            host.Show();
            var position = context.ViewModel.Styles.Subtitle.Position;
            var diagram = UiTestActions.Find<SubtitlePositionDiagram>(host, "PositionDiagram");
            var originalGeometry = Assert.IsType<AegiNext.Desktop.Editing.SubtitlePositionGeometry>(diagram.Geometry);
            var texts = new[] { "0.50", "1.00", ".50", "1.000", "12.50", "-20.00" };
            var fields = new[] { position.AnchorX, position.AnchorY, position.PivotX, position.PivotY, position.OffsetX, position.OffsetY };
            for (var index = 0; index < fields.Length; index++)
            {
                fields[index].RawText = texts[index];
            }
            context.ViewModel.Styles.Subtitle.Margins.Left.RawText = "100";
            context.ViewModel.Styles.Subtitle.Margins.Right.RawText = "100";

            Flush(host);

            var preview = context.Session.PreviewDocument;
            using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(context.Session.ProjectDirectory));
            var measurement = renderer.MeasureSubtitlePlacement(preview, preview.Subtitles[0]);
            var geometry = Assert.IsType<AegiNext.Desktop.Editing.SubtitlePositionGeometry>(diagram.Geometry);
            Assert.True(geometry.GlyphSize.Y > originalGeometry.GlyphSize.Y);
            Assert.Equal(new ScenePoint(measurement.Bounds.Left, measurement.Bounds.Top), geometry.GlyphOrigin);
            Assert.Equal(new ScenePoint(measurement.Bounds.Width, measurement.Bounds.Height), geometry.GlyphSize);
            Assert.Equal(position.Geometry, diagram.Geometry);
            Assert.Equal(measurement.Position, diagram.Position);
            Assert.Equal(new SubtitleMargins(100, 100, 13), diagram.Margins);
            Assert.Equal(texts, fields.Select(field => field.RawText).ToArray());
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.True(position.IsExplicit);
            Assert.False(UiTestActions.Find<NumericDraftInput>(host, "MarginVerticalInput").IsEffectivelyEnabled);
        }
        finally
        {
            foreach (var key in new[] { "AnchorXInput", "AnchorYInput", "PivotXInput", "PivotYInput", "OffsetXInput", "OffsetYInput" })
            {
                context.ViewModel.Styles.Subtitle.Position.RestoreField(key);
            }
            RestoreMargins(context);
            host.Close();
            view.Dispose();
        }
    }

    private static TextBox Text(NumericDraftInput input)
    {
        return Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
    }

    private static void Type(Window window, NumericDraftInput input, string text)
    {
        input.BringIntoView();
        Flush(window);
        var editor = Text(input);
        Assert.True(editor.Focus());
        editor.SelectAll();
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(text, editor.Text);
        Assert.Equal(text, input.RawText);
    }

    private static void AssertInputsFit(Window window, SubtitleMarginsEditor editor,
        NumericDraftInput left, NumericDraftInput right, NumericDraftInput vertical)
    {
        editor.BringIntoView();
        Flush(window);
        var leftBounds = BoundsIn(left, editor);
        var rightBounds = BoundsIn(right, editor);
        var verticalBounds = BoundsIn(vertical, editor);
        Assert.True(leftBounds.Width >= 64, $"Left margin input must remain editable: {leftBounds}.");
        Assert.True(rightBounds.Width >= 64, $"Right margin input must remain editable: {rightBounds}.");
        Assert.True(verticalBounds.Width >= 64, $"Vertical margin input must remain editable: {verticalBounds}.");
        Assert.True(leftBounds.Right <= rightBounds.Left);
        Assert.True(rightBounds.Right <= verticalBounds.Left);
        Assert.InRange(Math.Abs(leftBounds.Center.Y - verticalBounds.Center.Y), 0, 1);
        Assert.InRange(leftBounds.Left, 0, editor.Bounds.Width);
        Assert.InRange(verticalBounds.Right, 0, editor.Bounds.Width + 1);
        Assert.All(new[] { left, right, vertical }, input => AssertPointerHits(window, Text(input)));
        foreach (var (name, key) in new[]
        {
            ("MarginLeftLabel", "Workbench.MarginLeft"),
            ("MarginRightLabel", "Workbench.MarginRight"),
            ("MarginVerticalLabel", "Workbench.MarginVertical")
        })
        {
            var label = UiTestActions.Find<TextBlock>(editor, name);
            Assert.Equal(Localization.Get(key), label.Text);
            Assert.True(label.DesiredSize.Width <= label.Bounds.Width + 1,
                $"The localized margin label must fit its input column: {name} ({label.Text}).");
        }
        var diagram = UiTestActions.Find<SubtitleMarginsDiagram>(editor, "MarginsDiagram");
        Assert.False(editor.ShowDiagram);
        Assert.False(diagram.IsEffectivelyVisible);
        var positionDiagram = UiTestActions.Find<SubtitlePositionDiagram>(window, "PositionDiagram");
        Assert.True(positionDiagram.IsEffectivelyVisible);
        Assert.NotNull(positionDiagram.Geometry);
        Assert.NotNull(positionDiagram.Position);
    }

    private static Rect BoundsIn(Visual control, Visual relativeTo)
    {
        var origin = control.TranslatePoint(default, relativeTo);
        Assert.NotNull(origin);
        return new(origin.Value, control.Bounds.Size);
    }

    private static void AssertPointerHits(Window window, Control control)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(point);
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point.Value));
        Assert.True(ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control));
    }

    private static void RestoreMargins(MainWindowTestContext context)
    {
        foreach (var field in new[] { "MarginLeftInput", "MarginRightInput", "MarginVerticalInput" })
        {
            context.ViewModel.Styles.Subtitle.RestoreNumberField(field);
        }
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Capture(Window window, Control editor, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_MARGIN_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }
        editor.BringIntoView();
        Flush(window);
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }

    private static Half[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document)
    {
        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        return pixels;
    }
}
