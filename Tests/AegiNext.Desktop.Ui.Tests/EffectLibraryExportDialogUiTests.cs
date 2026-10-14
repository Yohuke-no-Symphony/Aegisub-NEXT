using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.EffectLibrary.Transfer;
using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证导出模态窗口的真实勾选、格式同步、输入和关闭边界。</summary>
public sealed class EffectLibraryExportDialogUiTests
{
    /// <summary>搜索与方形选择图标同排居中，底部动作等高；清词保留勾选与输入焦点。</summary>
    [AvaloniaTheory]
    [InlineData("en-US", 600, false)]
    [InlineData("zh-CN", 600, false)]
    [InlineData("ja-JP", 600, false)]
    [InlineData("en-US", 900, false)]
    [InlineData("zh-CN", 900, false)]
    [InlineData("ja-JP", 900, false)]
    [InlineData("en-US", 600, true)]
    [InlineData("zh-CN", 600, true)]
    [InlineData("ja-JP", 600, true)]
    [InlineData("en-US", 900, true)]
    [InlineData("zh-CN", 900, true)]
    [InlineData("ja-JP", 900, true)]
    public void SearchAndSelectionIconsAlignAndClearingSearchPreservesSelection(string language, double width, bool dark)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        using var model = new EffectLibraryExportDialogViewModel(Snapshot(), (_, _) => Task.FromResult(false));
        var owner = new Window();
        var dialog = new EffectLibraryExportDialog(model)
        {
            Width = width,
            Height = 680,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            owner.Show();
            _ = dialog.ShowDialog<bool>(owner);
            Pump(dialog);
            var input = UiTestActions.Find<TextBox>(dialog, "EffectLibraryExportSearch");
            var selectAll = UiTestActions.Find<Button>(dialog, "EffectLibraryExportSelectAll");
            var selectNone = UiTestActions.Find<Button>(dialog, "EffectLibraryExportSelectNone");
            var cancel = UiTestActions.Find<Button>(dialog, "EffectLibraryExportCancel");
            var export = UiTestActions.Find<Button>(dialog, "EffectLibraryExportSelected");
            Assert.Null(dialog.FindControl<TextBlock>("EffectLibraryExportSavedHint"));
            Assert.Null(dialog.FindControl<Button>("EffectLibraryExportAll"));
            Assert.Null(dialog.FindControl<Button>("EffectLibraryExportSelectVisible"));
            Assert.Null(dialog.FindControl<Button>("EffectLibraryExportClearSelection"));
            var inputBounds = BoundsIn(input, dialog);
            foreach (var button in new[] { selectAll, selectNone })
            {
                var bounds = BoundsIn(button, dialog);
                Assert.Equal(bounds.Width, bounds.Height, 3);
                Assert.InRange(Math.Abs(inputBounds.Center.Y - bounds.Center.Y), 0, 0.5);
                Assert.True(bounds.Left >= inputBounds.Right + 7);
                var icon = Assert.Single(button.GetVisualDescendants().OfType<MaterialIcon>());
                Assert.Equal(bounds.Center.X, BoundsIn(icon, dialog).Center.X, 3);
                Assert.Equal(bounds.Center.Y, BoundsIn(icon, dialog).Center.Y, 3);
                Assert.False(string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(button)));
                Assert.Equal(Avalonia.Automation.AutomationProperties.GetName(button), ToolTip.GetTip(button));
            }
            Assert.Equal(BoundsIn(selectAll, dialog).Height, BoundsIn(selectNone, dialog).Height, 3);
            Assert.Equal(MaterialIconKind.CheckboxMultipleMarkedOutline, Assert.Single(selectAll.GetVisualDescendants().OfType<MaterialIcon>()).Kind);
            Assert.Equal(MaterialIconKind.CheckboxMultipleBlankOutline, Assert.Single(selectNone.GetVisualDescendants().OfType<MaterialIcon>()).Kind);
            Assert.True(BoundsIn(selectAll, dialog).Right + 7 <= BoundsIn(selectNone, dialog).Left);
            var cancelBounds = BoundsIn(cancel, dialog);
            var exportBounds = BoundsIn(export, dialog);
            Assert.Equal(cancelBounds.Top, exportBounds.Top, 3);
            Assert.Equal(cancelBounds.Height, exportBounds.Height, 3);
            Assert.True(cancelBounds.Right + 7 <= exportBounds.Left);
            Assert.False(export.IsEffectivelyEnabled);
            Search(dialog, "missing");
            Assert.Equal(0, model.VisibleCount);
            UiTestActions.Click(dialog, selectAll);
            Assert.Equal(4, model.SelectedCount);
            Assert.True(export.IsEffectivelyEnabled);
            Assert.Equal(exportBounds, BoundsIn(export, dialog));
            var clear = UiTestActions.Find<Button>(dialog, "EffectLibraryExportClearSearch");
            Assert.True(clear.IsEffectivelyVisible);
            Assert.Equal(MaterialIconKind.Close, Assert.Single(clear.GetVisualDescendants().OfType<MaterialIcon>()).Kind);
            Assert.True(input.Focus());
            var clearBounds = BoundsIn(clear, dialog);
            Assert.True(BoundsIn(input, dialog).Contains(clearBounds));
            Assert.Equal(clearBounds.Width, clearBounds.Height, 3);
            UiTestActions.Click(dialog, clear);
            Assert.Equal(string.Empty, input.Text);
            Assert.Equal(string.Empty, model.SearchText);
            Assert.Same(input, dialog.FocusManager!.GetFocusedElement());
            Assert.Equal(4, model.SelectedCount);
            Assert.Equal(4, model.VisibleCount);
            Assert.False(clear.IsEffectivelyVisible);
            UiTestActions.Click(dialog, selectNone);
            Assert.Equal(0, model.SelectedCount);
            Assert.False(export.IsEffectivelyEnabled);
        }
        finally
        {
            dialog.CloseImmediately();
            owner.Close();
        }
    }

    /// <summary>partial 分组点击只操作可见项，独立全选和全不选包含隐藏素材。</summary>
    [AvaloniaFact]
    public void PointerCheckboxesDerivePartialStateAndSelectOnlyVisibleMatches()
    {
        using var environment = new UiTestEnvironment();
        using var model = new EffectLibraryExportDialogViewModel(Snapshot(), (_, _) => Task.FromResult(false));
        var owner = new Window();
        var dialog = new EffectLibraryExportDialog(model);
        try
        {
            owner.Show();
            _ = dialog.ShowDialog<bool>(owner);
            Pump(dialog);
            Assert.True(model.StyleRoot.IsExpanded);
            Assert.True(model.EffectRoot.IsExpanded);
            Assert.False(UiTestActions.Find<Button>(dialog, "EffectLibraryExportSelected").IsEffectivelyEnabled);
            var bright = model.StyleRoot.Children[0];
            var compact = model.StyleRoot.Children[1];
            UiTestActions.Click(dialog, Checkbox(dialog, bright));
            Assert.True(bright.IsChecked);
            Assert.Null(Checkbox(dialog, model.StyleRoot).IsChecked);
            UiTestActions.Click(dialog, Checkbox(dialog, model.StyleRoot));
            Assert.True(Checkbox(dialog, model.StyleRoot).IsChecked);
            Assert.True(Checkbox(dialog, compact).IsChecked);
            Search(dialog, "COMPACT");
            Assert.Single(model.StyleRoot.Children);
            UiTestActions.Click(dialog, Checkbox(dialog, model.StyleRoot));
            Assert.False(compact.IsChecked);
            Assert.True(bright.IsChecked);
            Assert.Equal(1, model.HiddenSelectedCount);
            UiTestActions.Click(dialog, "EffectLibraryExportSelectAll");
            Assert.Equal(4, model.SelectedCount);
            Assert.Equal(3, model.HiddenSelectedCount);
            Assert.Equal(Localization.Format("EffectLibrary.ExportSelectionSummary", 4),
                UiTestActions.Find<TextBlock>(dialog, "EffectLibraryExportSelectionSummary").Text);
            UiTestActions.Click(dialog, "EffectLibraryExportSelectNone");
            Assert.False(bright.IsChecked);
            Assert.False(compact.IsChecked);
            Assert.Equal(0, model.SelectedCount);
            Assert.All(model.Roots.SelectMany(root => root.AllChildren), node => Assert.False(node.IsChecked));
        }
        finally
        {
            dialog.CloseImmediately();
            owner.Close();
        }
    }

    /// <summary>可见 ComboBox 随混合选择回退包格式，取消样式后能重新选源码，搜索保留格式。</summary>
    [AvaloniaFact]
    public void VisibleFormatChoiceTracksSourceEligibilityAndSurvivesSearchUpdates()
    {
        using var environment = new UiTestEnvironment();
        using var model = new EffectLibraryExportDialogViewModel(Snapshot(), (_, _) => Task.FromResult(false));
        var owner = new Window();
        var dialog = new EffectLibraryExportDialog(model);
        try
        {
            owner.Show();
            _ = dialog.ShowDialog<bool>(owner);
            Pump(dialog);
            var effect = model.EffectRoot.Children[0];
            var style = model.StyleRoot.Children[0];
            UiTestActions.Click(dialog, Checkbox(dialog, effect));
            var combo = UiTestActions.Find<ComboBox>(dialog, "EffectLibraryExportDialogFormat");
            combo.SelectedItem = combo.Items.OfType<EffectLibraryExportFormatChoice>().Single(choice => choice.Format == EffectLibraryExportFormat.SOURCE);
            Pump(dialog);
            var selected = Assert.IsType<EffectLibraryExportFormatChoice>(combo.SelectedItem);
            Assert.Equal(EffectLibraryExportFormat.SOURCE, selected.Format);
            Search(dialog, "GLOW-SCRIPT");
            Assert.Same(selected, combo.SelectedItem);
            Search(dialog, string.Empty);
            UiTestActions.Click(dialog, Checkbox(dialog, style));
            Assert.Equal(EffectLibraryExportFormat.PACKAGE, Assert.IsType<EffectLibraryExportFormatChoice>(combo.SelectedItem).Format);
            Assert.Equal(EffectLibraryExportFormat.PACKAGE, model.SelectedFormat);
            Assert.DoesNotContain(combo.Items.OfType<EffectLibraryExportFormatChoice>(), choice => choice.Format == EffectLibraryExportFormat.SOURCE);
            UiTestActions.Click(dialog, Checkbox(dialog, style));
            combo.SelectedItem = combo.Items.OfType<EffectLibraryExportFormatChoice>().Single(choice => choice.Format == EffectLibraryExportFormat.SOURCE);
            Pump(dialog);
            Assert.Equal(EffectLibraryExportFormat.SOURCE, Assert.IsType<EffectLibraryExportFormatChoice>(combo.SelectedItem).Format);
            Search(dialog, "missing");
            Assert.Equal(EffectLibraryExportFormat.SOURCE, Assert.IsType<EffectLibraryExportFormatChoice>(combo.SelectedItem).Format);
            Assert.Equal(1, model.SelectedCount);
            Assert.Equal(1, model.HiddenSelectedCount);
        }
        finally
        {
            dialog.CloseImmediately();
            owner.Close();
        }
    }

    /// <summary>下拉弹出菜单优先消费 Escape，按住该键不会继续关闭对话框。</summary>
    [AvaloniaFact]
    public async Task EscapeClosesTheActualFormatPopupAndRetainsTheDialogUntilAFreshPress()
    {
        using var environment = new UiTestEnvironment();
        await using var model = new EffectLibraryExportDialogViewModel(Snapshot(), (_, _) => Task.FromResult(false));
        var owner = new Window();
        var dialog = new EffectLibraryExportDialog(model);
        try
        {
            owner.Show();
            var answer = dialog.ShowDialog<bool>(owner);
            Pump(dialog);
            var combo = UiTestActions.Find<ComboBox>(dialog, "EffectLibraryExportDialogFormat");
            var selected = combo.SelectedItem;
            var point = combo.TranslatePoint(new(combo.Bounds.Width / 2, combo.Bounds.Height / 2), dialog)!.Value;
            dialog.MouseDown(point, MouseButton.Left);
            dialog.MouseUp(point, MouseButton.Left);
            Pump(dialog);
            Assert.True(combo.IsDropDownOpen);
            Assert.True(combo.Focus());
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Pump(dialog);
            Assert.False(combo.IsDropDownOpen);
            Assert.True(dialog.IsVisible);
            Assert.False(answer.IsCompleted);
            Assert.Same(selected, combo.SelectedItem);
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.True(dialog.IsVisible);
            dialog.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.False(await answer.WaitAsync(TimeSpan.FromSeconds(5)));
            await dialog.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            dialog.CloseImmediately();
            owner.Close();
        }
    }

    /// <summary>标准搜索 TextBox 保留文本键和 IME 候选确认；Enter 不意外开始导出。</summary>
    [AvaloniaFact]
    public void SearchPreservesTextSpaceAndImeCandidateKeysWithoutExporting()
    {
        using var environment = new UiTestEnvironment();
        var calls = 0;
        using var model = new EffectLibraryExportDialogViewModel(Snapshot(), (_, _) =>
        {
            calls++;
            return Task.FromResult(false);
        });
        var owner = new Window();
        var dialog = new EffectLibraryExportDialog(model);
        try
        {
            owner.Show();
            _ = dialog.ShowDialog<bool>(owner);
            Pump(dialog);
            UiTestActions.Click(dialog, Checkbox(dialog, model.StyleRoot.Children[0]));
            var input = UiTestActions.Find<TextBox>(dialog, "EffectLibraryExportSearch");
            Assert.True(input.Focus());
            dialog.KeyTextInput("字幕");
            dialog.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            dialog.KeyTextInput(" ");
            dialog.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal("字幕 ", input.Text);
            Assert.Equal(input.Text, model.SearchText);
            var presenter = Assert.Single(input.GetVisualDescendants().OfType<TextPresenter>());
            presenter.PreeditText = "候选文字";
            UiTestActions.Press(dialog, Key.Enter);
            UiTestActions.Press(dialog, Key.Escape);
            Assert.True(dialog.IsVisible);
            Assert.Equal(0, calls);
            presenter.PreeditText = null;
            UiTestActions.Press(dialog, Key.Enter);
            Assert.Equal(0, calls);
            Assert.True(dialog.IsVisible);
        }
        finally
        {
            dialog.CloseImmediately();
            owner.Close();
        }
    }

    /// <summary>选择文件取消和写入失败均留在对话框，重试成功才返回 true。</summary>
    [AvaloniaFact]
    public async Task CancelledPickerAndFailedWriteKeepTheModalOpenUntilRetrySucceeds()
    {
        using var environment = new UiTestEnvironment();
        var attempts = 0;
        await using var model = new EffectLibraryExportDialogViewModel(Snapshot(), (_, _) =>
        {
            attempts++;
            return attempts switch
            {
                1 => Task.FromResult(false),
                2 => Task.FromException<bool>(new IOException("Write destination unavailable 中文")),
                _ => Task.FromResult(true)
            };
        });
        var owner = new Window();
        var dialog = new EffectLibraryExportDialog(model);
        try
        {
            owner.Show();
            var answer = dialog.ShowDialog<bool>(owner);
            Pump(dialog);
            UiTestActions.Click(dialog, Checkbox(dialog, model.StyleRoot.Children[0]));
            UiTestActions.Click(dialog, "EffectLibraryExportSelected");
            await dialog.Completion;
            Assert.True(dialog.IsVisible);
            Assert.False(answer.IsCompleted);
            Assert.Equal(1, model.SelectedCount);
            UiTestActions.Click(dialog, "EffectLibraryExportSelected");
            await dialog.Completion;
            Pump(dialog);
            Assert.True(dialog.IsVisible);
            Assert.Contains("Write destination unavailable 中文", UiTestActions.Find<TextBlock>(dialog, "EffectLibraryExportDialogError").Text, StringComparison.Ordinal);
            Assert.Equal(1, model.SelectedCount);
            UiTestActions.Click(dialog, "EffectLibraryExportSelected");
            Assert.True(await answer.WaitAsync(TimeSpan.FromSeconds(5)));
            await dialog.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            await dialog.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(model.WasExported);
            Assert.False(dialog.IsVisible);
            Assert.Empty(owner.OwnedWindows);
            Assert.Equal(3, attempts);
        }
        finally
        {
            dialog.CloseImmediately();
            owner.Close();
        }
    }

    /// <summary>操作中的关闭取消请求，返回 false，并等待不响应取消的回调完成清理。</summary>
    [AvaloniaFact]
    public async Task ClosingCancelsPendingExportAndWaitsForCleanupWithoutAcceptingLateSuccess()
    {
        using var environment = new UiTestEnvironment();
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        await using var model = new EffectLibraryExportDialogViewModel(Snapshot(), (_, token) =>
        {
            observed = token;
            return result.Task;
        });
        var owner = new Window();
        var dialog = new EffectLibraryExportDialog(model);
        try
        {
            owner.Show();
            var answer = dialog.ShowDialog<bool>(owner);
            Pump(dialog);
            UiTestActions.Click(dialog, Checkbox(dialog, model.EffectRoot.Children[0]));
            UiTestActions.Click(dialog, "EffectLibraryExportSelected");
            Assert.True(model.IsBusy);
            Assert.False(UiTestActions.Find<TextBox>(dialog, "EffectLibraryExportSearch").IsEffectivelyEnabled);
            UiTestActions.Click(dialog, "EffectLibraryExportCancel");
            Assert.False(await answer.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(observed.IsCancellationRequested);
            Assert.False(dialog.DisposalCompletion.IsCompleted);
            result.TrySetResult(true);
            await dialog.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            await dialog.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(model.WasExported);
            Assert.False(model.HasError);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            result.TrySetResult(false);
            dialog.CloseImmediately();
            owner.Close();
        }
    }

    /// <summary>三语动态切换保留搜索、勾选与源码格式，独立标题栏和格式标签同步更新。</summary>
    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("ja-JP")]
    public void LanguageChangesPreserveSelectionsAndUpdateStandaloneChromeAndFormatLabels(string language)
    {
        using var environment = new UiTestEnvironment();
        using var model = new EffectLibraryExportDialogViewModel(Snapshot(), (_, _) => Task.FromResult(false));
        var owner = new Window();
        var dialog = new EffectLibraryExportDialog(model);
        try
        {
            owner.Show();
            _ = dialog.ShowDialog<bool>(owner);
            Pump(dialog);
            UiTestActions.Click(dialog, Checkbox(dialog, model.EffectRoot.Children[0]));
            var combo = UiTestActions.Find<ComboBox>(dialog, "EffectLibraryExportDialogFormat");
            combo.SelectedItem = combo.Items.OfType<EffectLibraryExportFormatChoice>().Single(choice => choice.Format == EffectLibraryExportFormat.SOURCE);
            Search(dialog, "glow-script");
            var selected = combo.SelectedItem;
            Localization.SetLanguage(language);
            Pump(dialog);
            Assert.Equal(Localization.Get("EffectLibrary.ExportDialogTitle"), dialog.Title);
            Assert.Equal(dialog.Title, UiTestActions.Find<WindowTitleBar>(dialog, "EffectLibraryExportTitleBar").Title);
            Assert.Equal(Localization.Get("EffectLibrary.ExportSearch"), UiTestActions.Find<TextBox>(dialog, "EffectLibraryExportSearch").PlaceholderText);
            foreach (var (name, key) in new[]
            {
                ("EffectLibraryExportSelectAll", "EffectLibrary.ExportSelectAll"),
                ("EffectLibraryExportSelectNone", "EffectLibrary.ExportSelectNone"),
                ("EffectLibraryExportClearSearch", "EffectLibrary.ExportClearSearch")
            })
            {
                var button = UiTestActions.Find<Button>(dialog, name);
                Assert.Equal(Localization.Get(key), ToolTip.GetTip(button));
                Assert.Equal(Localization.Get(key), Avalonia.Automation.AutomationProperties.GetName(button));
            }
            Assert.Same(selected, combo.SelectedItem);
            Assert.Equal(Localization.Get("EffectLibrary.ExportSource"), Assert.IsType<EffectLibraryExportFormatChoice>(combo.SelectedItem).Text);
            Assert.Equal("glow-script", model.SearchText);
            Assert.Equal(1, model.SelectedCount);
            Assert.True(Checkbox(dialog, model.EffectRoot).IsChecked);
            Assert.Equal(Localization.Format("EffectLibrary.ExportSelectionSummary", 1), UiTestActions.Find<TextBlock>(dialog, "EffectLibraryExportSelectionSummary").Text);
            dialog.CloseImmediately();
            var changed = 0;
            model.PropertyChanged += (_, _) => changed++;
            Localization.SetLanguage("en-US");
            Assert.Equal(0, changed);
        }
        finally
        {
            dialog.CloseImmediately();
            owner.Close();
        }
    }

    /// <summary>最小尺寸、源码格式和长错误下搜索与按钮仍在窗口内，完整错误可滚动。</summary>
    [AvaloniaTheory]
    [InlineData("en-US", 600)]
    [InlineData("zh-CN", 600)]
    [InlineData("ja-JP", 600)]
    [InlineData("en-US", 900)]
    public async Task MinimumSizeKeepsSearchAndActionsAccessibleWithSourceFormatAndALongError(string language, double width)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var reason = string.Join('\n', Enumerable.Repeat("Destination unavailable 字幕测试 ABC 123 " + new string('x', 80), 80));
        await using var model = new EffectLibraryExportDialogViewModel(Snapshot(), (_, _) => Task.FromException<bool>(new IOException(reason)));
        var owner = new Window();
        var dialog = new EffectLibraryExportDialog(model) { Width = width, Height = 680, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            owner.Show();
            _ = dialog.ShowDialog<bool>(owner);
            Pump(dialog);
            Assert.True(dialog.CanResize);
            Assert.Equal(WindowStartupLocation.CenterOwner, dialog.WindowStartupLocation);
            UiTestActions.Click(dialog, Checkbox(dialog, model.EffectRoot.Children[0]));
            var combo = UiTestActions.Find<ComboBox>(dialog, "EffectLibraryExportDialogFormat");
            combo.SelectedItem = combo.Items.OfType<EffectLibraryExportFormatChoice>().Single(choice => choice.Format == EffectLibraryExportFormat.SOURCE);
            UiTestActions.Click(dialog, "EffectLibraryExportSelected");
            await dialog.Completion;
            Pump(dialog);
            Control[] controls =
            [
                UiTestActions.Find<TextBox>(dialog, "EffectLibraryExportSearch"),
                UiTestActions.Find<TreeView>(dialog, "EffectLibraryExportTree"),
                UiTestActions.Find<Button>(dialog, "EffectLibraryExportSelectAll"),
                UiTestActions.Find<Button>(dialog, "EffectLibraryExportSelectNone"),
                combo,
                UiTestActions.Find<ScrollViewer>(dialog, "EffectLibraryExportErrorScroll"),
                UiTestActions.Find<Button>(dialog, "EffectLibraryExportCancel"),
                UiTestActions.Find<Button>(dialog, "EffectLibraryExportSelected")
            ];
            var bounds = controls.Select(control => BoundsIn(control, dialog)).ToArray();
            for (var index = 0; index < controls.Length; index++)
            {
                Assert.True(controls[index].IsEffectivelyVisible);
                Assert.True(bounds[index].Width > 0 && bounds[index].Height > 0);
                Assert.True(new Rect(dialog.ClientSize).Contains(bounds[index]), $"{language}/{width}: {controls[index].Name} escaped {dialog.ClientSize}: {bounds[index]}.");
                for (var previous = 0; previous < index; previous++)
                {
                    Assert.False(bounds[index].Intersects(bounds[previous]), $"{language}/{width}: {controls[index].Name} overlaps {controls[previous].Name}.");
                }
            }
            var scroll = UiTestActions.Find<ScrollViewer>(dialog, "EffectLibraryExportErrorScroll");
            Assert.InRange(scroll.Bounds.Height, 1, 80);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.Contains(reason, UiTestActions.Find<TextBlock>(dialog, "EffectLibraryExportDialogError").Text, StringComparison.Ordinal);
            scroll.Offset = new(0, scroll.Extent.Height);
            Pump(dialog);
            Assert.True(scroll.Offset.Y > 0);
            UiTestActions.Click(dialog, "EffectLibraryExportCancel");
            await dialog.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(dialog.IsVisible);
        }
        finally
        {
            dialog.CloseImmediately();
            owner.Close();
        }
    }

    /// <summary>长中英混合素材名保持树行在可见范围内，完整名称仍可由提示和辅助信息读取。</summary>
    [AvaloniaFact]
    public void LongMaterialNamesFitTheTreeAtMinimumWidthAndRetainTheirFullLabels()
    {
        using var environment = new UiTestEnvironment();
        var snapshot = Snapshot();
        var styleName = "字幕 ABC 123 " + new string('样', 200);
        var effectName = "Script ABC 中文 " + new string('x', 400);
        snapshot = snapshot with
        {
            Styles = snapshot.Styles with { Presets = snapshot.Styles.Presets.SetItem(0, snapshot.Styles.Presets[0] with { Name = styleName }) },
            Effects = snapshot.Effects with { Presets = snapshot.Effects.Presets.SetItem(0, snapshot.Effects.Presets[0] with { Name = effectName }) }
        };
        using var model = new EffectLibraryExportDialogViewModel(snapshot, (_, _) => Task.FromResult(false));
        var owner = new Window();
        var dialog = new EffectLibraryExportDialog(model) { Width = 600, Height = 680 };
        try
        {
            owner.Show();
            _ = dialog.ShowDialog<bool>(owner);
            Pump(dialog);
            var tree = UiTestActions.Find<TreeView>(dialog, "EffectLibraryExportTree");
            foreach (var node in new[] { model.StyleRoot.Children[0], model.EffectRoot.Children[0] })
            {
                var checkbox = Checkbox(dialog, node);
                checkbox.BringIntoView();
                Pump(dialog);
                var bounds = BoundsIn(checkbox, tree);
                Assert.True(bounds.Width > 0 && bounds.Height > 0);
                Assert.InRange(bounds.Height, 1, 60);
                Assert.True(new Rect(tree.Bounds.Size).Contains(bounds), $"Long-name checkbox escaped tree bounds: {bounds} / {tree.Bounds}.");
                Assert.Equal(node.Name, ToolTip.GetTip(checkbox));
                Assert.Equal(node.Name, Avalonia.Automation.AutomationProperties.GetName(checkbox));
                var label = Assert.Single(checkbox.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == node.Name);
                Assert.Single(label.TextLayout.TextLines);
                Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1);
                Assert.True(BoundsIn(checkbox, dialog).Right <= dialog.ClientSize.Width);
            }
        }
        finally
        {
            dialog.CloseImmediately();
            owner.Close();
        }
    }

    /// <summary>空快照无法导出或全选，Escape 返回 false 且关闭全部自有窗口。</summary>
    [AvaloniaFact]
    public async Task EmptySnapshotDisablesExportAndSelectionAndEscapeClosesTheDialog()
    {
        using var environment = new UiTestEnvironment();
        await using var model = new EffectLibraryExportDialogViewModel(new(), (_, _) => Task.FromResult(true));
        var owner = new Window();
        var dialog = new EffectLibraryExportDialog(model);
        try
        {
            owner.Show();
            var answer = dialog.ShowDialog<bool>(owner);
            Pump(dialog);
            Assert.False(UiTestActions.Find<Button>(dialog, "EffectLibraryExportSelected").IsEffectivelyEnabled);
            Assert.False(UiTestActions.Find<Button>(dialog, "EffectLibraryExportSelectAll").IsEffectivelyEnabled);
            Assert.False(UiTestActions.Find<Button>(dialog, "EffectLibraryExportSelectNone").IsEffectivelyEnabled);
            Assert.False(Checkbox(dialog, model.StyleRoot).IsEffectivelyEnabled);
            Assert.False(Checkbox(dialog, model.EffectRoot).IsEffectivelyEnabled);
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.False(await answer.WaitAsync(TimeSpan.FromSeconds(5)));
            await dialog.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            dialog.CloseImmediately();
            owner.Close();
        }
    }

    private static EffectLibrarySnapshot Snapshot()
    {
        return new()
        {
            Revision = Guid.NewGuid(),
            Styles = new() { Presets = [new(Guid.NewGuid(), "字幕 Bright", new()), new(Guid.NewGuid(), "Compact", new())] },
            Effects = new()
            {
                Presets =
                [
                    new(Guid.NewGuid(), "Motion Display", BuiltinEffectScripts.Get("fade-in").Source.Replace("fade-in", "glow-script", StringComparison.Ordinal)),
                    new(Guid.NewGuid(), "Soft Exit", BuiltinEffectScripts.Get("fade-out").Source.Replace("fade-out", "exit-script", StringComparison.Ordinal))
                ]
            }
        };
    }

    private static CheckBox Checkbox(EffectLibraryExportDialog dialog, EffectLibraryExportNode node)
    {
        Pump(dialog);
        return Assert.Single(dialog.GetVisualDescendants().OfType<CheckBox>(), control => ReferenceEquals(control.DataContext, node));
    }

    private static void Search(EffectLibraryExportDialog dialog, string text)
    {
        var input = UiTestActions.Find<TextBox>(dialog, "EffectLibraryExportSearch");
        Assert.True(input.Focus());
        input.SelectAll();
        UiTestActions.Press(dialog, Key.Back);
        if (text.Length > 0)
        {
            dialog.KeyTextInput(text);
        }
        Pump(dialog);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static Rect BoundsIn(Control control, Visual root)
    {
        return new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);
    }
}
