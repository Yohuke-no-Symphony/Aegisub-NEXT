using System.Collections.Immutable;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Styles;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleStyleBatchUiTests
{
    [AvaloniaFact]
    public async Task NumericInputPreviewsAndCommitsBothSubtitlesWhilePreservingTheirOwnFieldsWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, document.Layers[1].Id]);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 340, Height = 1000, Content = view };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "FontSizeInput");

            Type(host, input, "88");

            Assert.Same(document, context.Session.DocumentSnapshot);
            AssertSubtitleChanges(context.Session.PreviewDocument, document, 88, [0, 1]);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal(Localization.Format("Workbench.StyleSelection", 2),
                UiTestActions.Find<TextBlock>(view, "StyleSelectionLabel").Text);

            UiTestActions.Press(host, Key.Enter);
            Flush(host);

            var committed = context.Session.DocumentSnapshot;
            AssertSubtitleChanges(committed, document, 88, [0, 1]);
            Assert.True(context.Session.Editor.CanUndo);
            Assert.False(context.ViewModel.Styles.HasDrafts);
            Assert.True(context.Session.Editor.Undo());
            Flush(host);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal(40m, input.Value);
            Assert.True(context.Session.Editor.Redo());
            Flush(host);
            Assert.Same(committed, context.Session.DocumentSnapshot);
            Assert.Equal(88m, input.Value);
        }
        finally
        {
            context.ViewModel.Styles.Subtitle.RestoreNumberField("FontSizeInput");
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingTheSelectionWithTheSamePrimaryCommitsValidTextToThePreviousTargets(bool expand)
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        var originalTargets = new[] { document.Layers[0].Id, document.Layers[1].Id };
        var requested = expand ? document.Layers.Select(layer => layer.Id).ToArray() : [document.Layers[0].Id];
        context.Session.SelectLayer(document.Layers[0].Id, originalTargets);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 340, Height = 1000, Content = view };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "FontSizeInput");
            Type(host, input, "88");

            context.Session.SelectLayer(document.Layers[0].Id, requested);
            Flush(host);

            Assert.Equal(document.Layers[0].Id, context.Session.SelectedLayerId);
            Assert.Equal(requested.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
            AssertSubtitleChanges(context.Session.DocumentSnapshot, document, 88, [0, 1]);
            Assert.False(context.ViewModel.Styles.HasDrafts);
            Assert.True(context.Session.Editor.Undo());
            Flush(host);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            context.ViewModel.Styles.Subtitle.RestoreNumberField("FontSizeInput");
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidNumericTextBlocksSelectionExpansionOrReductionAndKeepsTheRawDraft(bool expand)
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        var originalTargets = new[] { document.Layers[0].Id, document.Layers[1].Id };
        var requested = expand ? document.Layers.Select(layer => layer.Id).ToArray() : [document.Layers[0].Id];
        context.Session.SelectLayer(document.Layers[0].Id, originalTargets);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 340, Height = 1000, Content = view };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "FontSizeInput");
            Type(host, input, "7e-");

            context.Session.SelectLayer(document.Layers[0].Id, requested);
            Flush(host);

            Assert.Equal(originalTargets.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
            Assert.Equal(document.Layers[0].Id, context.Session.SelectedLayerId);
            Assert.Equal("7e-", input.RawText);
            Assert.Equal("7e-", Assert.Single(input.GetVisualDescendants().OfType<TextBox>()).Text);
            Assert.Equal("styles", context.ViewModel.InvalidPanelId);
            Assert.Equal("FontSizeInput", context.ViewModel.InvalidFieldKey);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            context.ViewModel.Styles.Subtitle.RestoreNumberField("FontSizeInput");
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelingOrReturningTheNumericDragDoesNotCopyItsRestoredPrimaryValueOnTheNextEdit(bool cancel)
    {
        await using var context = new MainWindowTestContext();
        var document = PrepareGestureSelection(context);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 650, Height = 1100, Content = view };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "FontSizeInput");
            Assert.True(input.FocusInput());
            var title = Assert.Single(host.GetVisualDescendants().OfType<NumericDragLabel>(), label => label.Input == input);
            title.BringIntoView();
            Flush(host);
            var start = title.TranslatePoint(new(title.Bounds.Width / 2, title.Bounds.Height / 2), host)!.Value;
            var end = start + new Vector(12, 0);
            host.MouseDown(start, MouseButton.Left);
            host.MouseMove(end);
            Flush(host);
            Assert.True(input.IsTitleDragging);
            Assert.Equal(52, context.Session.PreviewDocument.Subtitles[0].Style.FontSize);

            if (cancel)
            {
                UiTestActions.Press(host, Key.Escape);
                host.MouseUp(end, MouseButton.Left);
            }
            else
            {
                host.MouseMove(start);
                Flush(host);
                host.MouseUp(start, MouseButton.Left);
            }
            Flush(host);

            Assert.False(input.IsTitleDragging);
            Assert.Equal(40m, input.Value);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            CommitShadowAndAssertOnlyShadowChanges(context, host, document);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelingOrReturningTheScalingDragPreservesEachClipsMatchOnTheNextEdit(bool cancel)
    {
        await using var context = new MainWindowTestContext();
        var document = PrepareGestureSelection(context);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 650, Height = 1100, Content = view };
        try
        {
            host.Show();
            var slider = UiTestActions.Find<Slider>(host, "MatchWidthOrHeightInput");
            slider.BringIntoView();
            Flush(host);
            var thumb = Assert.Single(slider.GetVisualDescendants().OfType<Thumb>());
            var start = thumb.TranslatePoint(new(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), host)!.Value;
            var end = start + new Vector((slider.Bounds.Width - thumb.Bounds.Width) * 0.3, 0);
            host.MouseDown(start, MouseButton.Left);
            host.MouseMove(end);
            Flush(host);
            Assert.InRange(context.Session.PreviewDocument.Subtitles[0].Style.Scaling.MatchWidthOrHeight, 0.7, 0.9);

            if (cancel)
            {
                UiTestActions.Press(host, Key.Escape);
                host.MouseUp(end, MouseButton.Left);
            }
            else
            {
                host.MouseMove(start);
                Flush(host);
                host.MouseUp(start, MouseButton.Left);
            }
            Flush(host);

            Assert.Equal(0.5, slider.Value, 8);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            CommitShadowAndAssertOnlyShadowChanges(context, host, document);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task RestoringAutomaticPositionSurvivesTheQueuedUiPreviewForEverySelectedSubtitle()
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        context.Session.Editor.Reset(document with
        {
            Subtitles = document.Subtitles.Select((cue, index) => index < 2 ? cue with
            {
                Style = cue.Style with
                {
                    Position = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5), Offset = new(40 + index * 60, 80) }
                }
            } : cue).ToImmutableArray()
        });
        var original = context.Session.DocumentSnapshot;
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, document.Layers[1].Id]);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 340, Height = 1000, Content = view };
        try
        {
            host.Show();
            Flush(host);
            Assert.True(context.ViewModel.Styles.Subtitle.Position.IsExplicit);

            context.ViewModel.Styles.Subtitle.Position.IsExplicit = false;
            Flush(host);

            Assert.False(context.ViewModel.Styles.Subtitle.Position.IsExplicit);
            Assert.All(context.Session.PreviewDocument.Subtitles.Take(2), cue => Assert.Null(cue.Style.Position));
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.True(context.Session.TryCommitDrafts(false));
            Flush(host);
            Assert.All(context.Session.DocumentSnapshot.Subtitles.Take(2), cue => Assert.Null(cue.Style.Position));
            Assert.Same(original.Subtitles[2], context.Session.DocumentSnapshot.Subtitles[2]);
            Assert.True(context.Session.Editor.Undo());
            Flush(host);
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.True(context.ViewModel.Styles.Subtitle.Position.IsExplicit);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task MixedSelectionRemovesTheEditorAndCannotApplyAnAvailablePreset()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        var document = Prepare(context);
        var shapeTrack = new ProjectTrack { Name = "Shapes" };
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, TrackId = shapeTrack.Id, Shape = new(ShapeKind.RECTANGLE, 100, 60), End = new(4)
        };
        context.Session.Editor.Reset(document with
        {
            Tracks = document.Tracks.Add(shapeTrack), Layers = document.Layers.Add(shape)
        });
        var original = context.Session.DocumentSnapshot;
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Batch preset", new() { FontFamily = "sans-serif", FontSize = 96 });
        await context.Session.Styles.UpsertAsync(preset);
        context.Session.SelectCue(document.Subtitles[0].Id);
        Assert.True(context.ViewModel.Styles.Subtitle.CanApplyPreset);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 340, Height = 1000, Content = view };
        try
        {
            host.Show();
            Assert.NotNull(UiTestActions.Find<NumericDraftInput>(view, "FontSizeInput"));

            context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, shape.Id]);
            Flush(host);

            Assert.Null(context.ViewModel.Styles.ActiveEditor);
            Assert.Null(UiTestActions.Find<ContentControl>(view, "StyleEditorHost").Content);
            Assert.DoesNotContain(view.GetVisualDescendants(), control => control is NumericDraftInput);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(), control => control.Name == "ApplyStyleButton");
            Assert.Equal(Localization.Get("Workbench.StyleMixedSelection"),
                UiTestActions.Find<TextBlock>(view, "StyleSelectionLabel").Text);
            Assert.False(context.ViewModel.Styles.Subtitle.CanApplyPreset);

            await context.Session.ApplySelectedStyleAsync();

            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    private static ProjectDocument Prepare(MainWindowTestContext context)
    {
        var first = new SubtitleLine
        {
            Text = "First ABC", Start = MediaTime.Zero, End = new(4),
            Style = new() { FontFamily = "sans-serif", FontSize = 40, Fill = new(1, 0.2, 0.1), StrokeWidth = 1 },
            InlineSpans = [new(0, 5, new() { Bold = true })],
            Karaoke = [new(0, 5, MediaTime.Zero, new(2), SceneColor.White)]
        };
        var second = new SubtitleLine
        {
            Text = "Second ABC", Start = new(5), End = new(9),
            Style = new() { FontFamily = "sans-serif", FontSize = 72, Fill = new(0.1, 0.2, 1), StrokeWidth = 5, LineHeight = 1.8 },
            InlineSpans = [new(0, 6, new() { Italic = true })]
        };
        var third = new SubtitleLine
        {
            Text = "Unselected ABC", Start = new(10), End = new(14),
            Style = new() { FontFamily = "sans-serif", FontSize = 56 },
            InlineSpans = [new(0, 10, new() { Underline = true })]
        };
        context.Session.Editor.Reset(context.Session.DocumentSnapshot with
        {
            Subtitles = [first, second, third], Layers = [Layer(first), Layer(second), Layer(third)]
        });
        return context.Session.DocumentSnapshot;
    }

    private static ProjectDocument PrepareGestureSelection(MainWindowTestContext context)
    {
        var document = Prepare(context);
        context.Session.Editor.Reset(document with
        {
            Subtitles = document.Subtitles.SetItem(1, document.Subtitles[1] with
            {
                Style = document.Subtitles[1].Style with
                {
                    FontSize = 52, Scaling = new() { MatchWidthOrHeight = 1 }
                }
            })
        });
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, document.Layers[1].Id]);
        return context.Session.DocumentSnapshot;
    }

    private static void CommitShadowAndAssertOnlyShadowChanges(MainWindowTestContext context, Window host, ProjectDocument document)
    {
        Type(host, UiTestActions.Find<NumericDraftInput>(host, "ShadowXInput"), "9");
        AssertOnlyShadowChanges(context.Session.PreviewDocument, document);
        UiTestActions.Press(host, Key.Enter);
        Flush(host);
        AssertOnlyShadowChanges(context.Session.DocumentSnapshot, document);
        Assert.True(context.Session.Editor.Undo());
        Flush(host);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static void AssertOnlyShadowChanges(ProjectDocument actual, ProjectDocument document)
    {
        foreach (var original in document.Subtitles.Take(2))
        {
            var expected = original with
            {
                Style = original.Style with { ShadowOffset = original.Style.ShadowOffset with { X = 9 } },
                InlineSpans = []
            };
            Assert.Equal(expected, actual.Subtitles.Single(cue => cue.Id == original.Id));
        }
        Assert.Same(document.Subtitles[2], actual.Subtitles[2]);
        Assert.Equal(document.Layers, actual.Layers);
    }

    private static ProjectLayer Layer(SubtitleLine cue)
    {
        return new() { Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End };
    }

    private static void AssertSubtitleChanges(ProjectDocument actual, ProjectDocument original, double fontSize, int[] changedIndices)
    {
        for (var index = 0; index < original.Subtitles.Length; index++)
        {
            var before = original.Subtitles[index];
            var after = actual.Subtitles.Single(cue => cue.Id == before.Id);
            if (changedIndices.Contains(index))
            {
                Assert.Equal(before with { Style = before.Style with { FontSize = fontSize }, InlineSpans = [] }, after);
            }
            else
            {
                Assert.Same(before, after);
            }
        }
        Assert.Equal(original.Layers, actual.Layers);
    }

    private static void Type(Window window, NumericDraftInput input, string text)
    {
        input.BringIntoView();
        Flush(window);
        var editor = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(editor.Focus());
        editor.SelectAll();
        window.KeyTextInput(text);
        Flush(window);
        Assert.Equal(text, editor.Text);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
