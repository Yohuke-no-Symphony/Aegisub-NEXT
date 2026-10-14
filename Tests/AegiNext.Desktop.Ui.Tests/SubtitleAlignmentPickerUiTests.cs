using AegiNext.Desktop.EffectLibrary;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using ProjectTextAlignment = AegiNext.Core.Projects.TextAlignment;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleAlignmentPickerUiTests
{
    private static readonly string[] horizontalNames =
        ["HorizontalLeftButton", "HorizontalCenterButton", "HorizontalRightButton"];
    private static readonly string[] verticalNames =
        ["VerticalTopButton", "VerticalCenterButton", "VerticalBottomButton"];
    private static readonly string[] localizationKeys =
        ["AlignLeft", "AlignCenter", "AlignRight", "AlignTop", "AlignMiddle", "AlignBottom"];

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void MouseCommitsPreserveTheOtherAxis(int initial)
    {
        using var environment = new UiTestEnvironment();
        var picker = new SubtitleAlignmentPicker { AlignmentIndex = initial };
        var window = new Window { Width = 320, Height = 120, Content = picker };
        var committed = new List<ProjectTextAlignment>();
        picker.AlignmentCommitted += (_, value) => committed.Add(value.Alignment);
        try
        {
            window.Show();
            for (var column = 0; column < 3; column++)
            {
                picker.AlignmentIndex = initial;
                UiTestActions.Click(window, horizontalNames[column]);
                Assert.Equal(initial / 3 * 3 + column, picker.AlignmentIndex);
                Assert.Equal((ProjectTextAlignment)picker.AlignmentIndex, committed[^1]);
                AssertSelection(picker);
            }
            for (var row = 0; row < 3; row++)
            {
                picker.AlignmentIndex = initial;
                UiTestActions.Click(window, verticalNames[row]);
                Assert.Equal(row * 3 + initial % 3, picker.AlignmentIndex);
                Assert.Equal((ProjectTextAlignment)picker.AlignmentIndex, committed[^1]);
                AssertSelection(picker);
            }
            Assert.Equal(6, committed.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ProgrammaticSynchronizationDoesNotCommitAndRepeatedMouseClickStaysSelected()
    {
        using var environment = new UiTestEnvironment();
        var picker = new SubtitleAlignmentPicker();
        Assert.Equal((int)ProjectTextAlignment.BOTTOM_CENTER, picker.AlignmentIndex);
        var window = new Window { Width = 320, Height = 120, Content = picker };
        var committed = new List<ProjectTextAlignment>();
        picker.AlignmentCommitted += (_, value) => committed.Add(value.Alignment);
        try
        {
            window.Show();
            foreach (var alignment in Enum.GetValues<ProjectTextAlignment>())
            {
                picker.AlignmentIndex = (int)alignment;
                AssertSelection(picker);
            }
            Assert.Empty(committed);
            UiTestActions.Click(window, "HorizontalRightButton");
            UiTestActions.Click(window, "VerticalBottomButton");
            Assert.Equal([ProjectTextAlignment.BOTTOM_RIGHT, ProjectTextAlignment.BOTTOM_RIGHT], committed);
            Assert.Equal((int)ProjectTextAlignment.BOTTOM_RIGHT, picker.AlignmentIndex);
            AssertSelection(picker);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ButtonGroupsWrapAndLabelsFollowLanguageAcrossReattachment(bool narrow, bool dark)
    {
        using var environment = new UiTestEnvironment();
        var picker = new SubtitleAlignmentPicker { AlignmentIndex = (int)ProjectTextAlignment.MIDDLE_LEFT };
        var window = new Window
        {
            Width = narrow ? 160 : 320, Height = 160, Content = picker,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        var commits = 0;
        picker.AlignmentCommitted += (_, _) => commits++;
        try
        {
            window.Show();
            AssertLabels(picker);
            var horizontal = UiTestActions.Find<StackPanel>(picker, "HorizontalAlignmentGroup");
            var vertical = UiTestActions.Find<StackPanel>(picker, "VerticalAlignmentGroup");
            window.UpdateLayout();
            if (narrow)
            {
                Assert.True(vertical.Bounds.Top >= horizontal.Bounds.Bottom);
            }
            else
            {
                Assert.Equal(horizontal.Bounds.Top, vertical.Bounds.Top);
            }
            foreach (var name in horizontalNames.Concat(verticalNames))
            {
                var button = UiTestActions.Find<ToolbarToggleButton>(picker, name);
                Assert.Equal(32, button.Bounds.Width);
                Assert.Equal(32, button.Bounds.Height);
            }
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            AssertLabels(picker);
            window.Content = null;
            Localization.SetLanguage("en-US");
            window.Content = picker;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AssertLabels(picker);
            AssertSelection(picker);
            Assert.Equal((int)ProjectTextAlignment.MIDDLE_LEFT, picker.AlignmentIndex);
            Assert.Equal(0, commits);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("HorizontalCenterButton", ProjectTextAlignment.BOTTOM_CENTER)]
    [InlineData("VerticalBottomButton", ProjectTextAlignment.BOTTOM_CENTER)]
    [InlineData("HorizontalRightButton", ProjectTextAlignment.BOTTOM_RIGHT)]
    [InlineData("VerticalTopButton", ProjectTextAlignment.TOP_CENTER)]
    public void StyleLibraryUserCommitClearsLegacyTextAlignmentAndPreservesOtherStyleData(
        string buttonName, ProjectTextAlignment expected)
    {
        using var environment = new UiTestEnvironment();
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Legacy", new()
        {
            TextAlign = SubtitleTextAlignment.LEFT, Fill = new(2, 1.5, 0.25, 0.9),
            Position = new() { Anchor = new(0.2, 0.3), Pivot = new(0.5, 1), Offset = new(12, -8) }
        });
        var window = new EffectLibraryWindow(new());
        var saved = new List<SubtitleStylePreset>();
        window.UpsertStyleRequested += (_, value) => saved.Add(value.Preset);
        try
        {
            window.Show();
            window.UpdateStyles([original]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var picker = UiTestActions.Find<SubtitleAlignmentPicker>(window, "AlignmentPicker");
            Assert.Equal((int)ProjectTextAlignment.BOTTOM_CENTER, picker.AlignmentIndex);
            Assert.False(window.ViewModel.Styles.IsDirty);
            Localization.SetLanguage("zh-CN");
            window.ViewModel.Styles.RefreshLanguage();
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.ViewModel.Styles.IsDirty);
            Assert.Equal(SubtitleTextAlignment.LEFT, window.ViewModel.Styles.Draft!.Style.TextAlign);
            UiTestActions.Click(window, buttonName);
            Assert.Equal((int)expected, picker.AlignmentIndex);
            Assert.Equal(expected, window.ViewModel.Styles.Draft.Style.Alignment);
            Assert.Null(window.ViewModel.Styles.Draft.Style.TextAlign);
            Assert.True(window.ViewModel.Styles.IsDirty);
            AssertSelection(picker);
            UiTestActions.Click(window, "SaveStyleButton");
            var changed = Assert.Single(saved);
            Assert.Equal(original.Style with { Alignment = expected, TextAlign = null }, changed.Style);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertSelection(SubtitleAlignmentPicker picker)
    {
        for (var index = 0; index < 3; index++)
        {
            Assert.Equal(index == picker.AlignmentIndex % 3,
                UiTestActions.Find<ToolbarToggleButton>(picker, horizontalNames[index]).IsChecked);
            Assert.Equal(index == picker.AlignmentIndex / 3,
                UiTestActions.Find<ToolbarToggleButton>(picker, verticalNames[index]).IsChecked);
        }
    }

    private static void AssertLabels(SubtitleAlignmentPicker picker)
    {
        var names = horizontalNames.Concat(verticalNames).ToArray();
        for (var index = 0; index < names.Length; index++)
        {
            var button = UiTestActions.Find<ToolbarToggleButton>(picker, names[index]);
            var expected = Localization.Get("Workbench." + localizationKeys[index]);
            Assert.DoesNotContain("Workbench.", expected);
            Assert.Equal(expected, ToolTip.GetTip(button));
            Assert.Equal(expected, AutomationProperties.GetName(button));
        }
    }
}
