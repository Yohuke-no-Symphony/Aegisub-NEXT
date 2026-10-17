using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class NativeMenuLifecycleUiTests
{
    private static readonly string[] builtinLayoutIds = ["standard", "aegisub-classic", "timing", "effects", "encode"];

    [AvaloniaFact]
    public async Task LanguageRefreshReplacesOnlyBranchesWhileOtherUpdatesPreserveEntireMenuTree()
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        var mainRoot = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(main));
        AssertInitialLayoutChoices(mainRoot);
        var identities = CaptureTree(mainRoot);
        var firstHeader = mainRoot.Items.OfType<NativeMenuItem>().First().Header;
        var timingCommand = main.GetCommand(WorkbenchCommand.TIMING_ENTER);
        var timingLeaf = identities.OfType<NativeMenuItem>().Single(item => ReferenceEquals(item.Command, timingCommand));

        main.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(main.OwnedWindows.OfType<SettingsWindow>());
        AssertStableTree(main, identities);
        UiTestActions.Find<ComboBox>(settings, "ThemeCombo").SelectedIndex = 2;
        AssertStableTree(main, identities);
        settings.SelectPage(SettingsPage.COLORS);
        UiTestActions.Find<ColorDraftInput>(settings, "AccentPicker").FindControl<ColorView>("Picker")!.Color = Color.Parse("#C54885");
        AssertStableTree(main, identities);
        settings.SelectPage(SettingsPage.APPEARANCE);
        var branchHeaders = CaptureBranchHeaders(identities);
        UiTestActions.SelectLanguage(settings, "zh-CN");
        identities = AssertLanguageRefresh(main, identities, branchHeaders);
        Assert.NotEqual(firstHeader, mainRoot.Items.OfType<NativeMenuItem>().First().Header);

        ChangeTimingBinding(settings, "F6");
        AssertStableTree(main, identities);
        Assert.Same(timingCommand, timingLeaf.Command);
        Assert.Contains("F6", Assert.IsType<string>(timingLeaf.Header), StringComparison.Ordinal);
        Assert.DoesNotContain("F8", timingLeaf.Header, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task SettingsOwnsIndependentStableTreeAndReopeningCreatesANewTreeWithoutChangingMain()
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        var mainRoot = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(main));
        AssertInitialLayoutChoices(mainRoot);
        var mainIdentities = CaptureTree(mainRoot);
        var openSettings = main.GetCommand(WorkbenchCommand.OPEN_SETTINGS);
        openSettings.Execute(null);
        var first = Assert.Single(main.OwnedWindows.OfType<SettingsWindow>());
        var firstIdentities = CaptureTree(Assert.IsType<NativeMenu>(NativeMenu.GetMenu(first)));
        AssertDisjointTrees(mainIdentities, firstIdentities);

        UiTestActions.Find<ComboBox>(first, "ThemeCombo").SelectedIndex = 1;
        AssertStableTree(main, mainIdentities);
        AssertStableTree(first, firstIdentities);
        first.SelectPage(SettingsPage.COLORS);
        UiTestActions.Find<ColorDraftInput>(first, "AccentPicker").FindControl<ColorView>("Picker")!.Color = Color.Parse("#C54885");
        AssertStableTree(main, mainIdentities);
        AssertStableTree(first, firstIdentities);
        first.SelectPage(SettingsPage.APPEARANCE);
        var mainHeaders = CaptureBranchHeaders(mainIdentities);
        var firstHeaders = CaptureBranchHeaders(firstIdentities);
        UiTestActions.SelectLanguage(first, "zh-CN");
        mainIdentities = AssertLanguageRefresh(main, mainIdentities, mainHeaders);
        firstIdentities = AssertLanguageRefresh(first, firstIdentities, firstHeaders);
        AssertDisjointTrees(mainIdentities, firstIdentities);
        ChangeTimingBinding(first, "F6");
        AssertStableTree(main, mainIdentities);
        AssertStableTree(first, firstIdentities);
        var timingCommand = main.GetCommand(WorkbenchCommand.TIMING_ENTER);
        var firstTimingLeaf = firstIdentities.OfType<NativeMenuItem>().Single(item => ReferenceEquals(item.Command, timingCommand));
        Assert.Contains("F6", Assert.IsType<string>(firstTimingLeaf.Header), StringComparison.Ordinal);

        first.Close();
        Assert.False(first.IsVisible);
        Assert.Empty(main.OwnedWindows.OfType<SettingsWindow>());
        AssertStableTree(main, mainIdentities);
        openSettings.Execute(null);
        var reopened = Assert.Single(main.OwnedWindows.OfType<SettingsWindow>());
        Assert.NotSame(first, reopened);
        var reopenedIdentities = CaptureTree(Assert.IsType<NativeMenu>(NativeMenu.GetMenu(reopened)));
        AssertDisjointTrees(firstIdentities, reopenedIdentities);
        AssertDisjointTrees(mainIdentities, reopenedIdentities);
        AssertStableTree(main, mainIdentities);

        mainHeaders = CaptureBranchHeaders(mainIdentities);
        var reopenedHeaders = CaptureBranchHeaders(reopenedIdentities);
        UiTestActions.SelectLanguage(reopened, "en-US");
        mainIdentities = AssertLanguageRefresh(main, mainIdentities, mainHeaders);
        reopenedIdentities = AssertLanguageRefresh(reopened, reopenedIdentities, reopenedHeaders);
        AssertDisjointTrees(firstIdentities, reopenedIdentities);
        AssertDisjointTrees(mainIdentities, reopenedIdentities);
        UiTestActions.Find<ComboBox>(reopened, "ThemeCombo").SelectedIndex = 2;
        AssertStableTree(main, mainIdentities);
        AssertStableTree(reopened, reopenedIdentities);
        ChangeTimingBinding(reopened, "F7");
        AssertStableTree(main, mainIdentities);
        AssertStableTree(reopened, reopenedIdentities);
        var reopenedTimingLeaf = reopenedIdentities.OfType<NativeMenuItem>().Single(item => ReferenceEquals(item.Command, timingCommand));
        Assert.Contains("F7", Assert.IsType<string>(reopenedTimingLeaf.Header), StringComparison.Ordinal);
    }

    private static void AssertInitialLayoutChoices(NativeMenu root)
    {
        var layouts = root.Items.OfType<NativeMenuItem>().Single(item =>
            item.Header == Localization.Get("Workbench.Layouts"));
        var branch = Assert.IsType<NativeMenu>(layouts.Menu);
        var choices = branch.Items.OfType<NativeMenuItem>()
            .Where(item => item.ToggleType == MenuItemToggleType.Radio).ToArray();
        Assert.Equal(builtinLayoutIds.Length, choices.Length);
        Assert.Equal(builtinLayoutIds
            .Select(id => Localization.Get("Layout." + id)), choices.Select(item => item.Header));
        Assert.Single(choices, item => item.IsChecked);
    }

    private static void ChangeTimingBinding(SettingsWindow settings, string gesture)
    {
        settings.SelectPage(SettingsPage.SHORTCUTS);
        UiTestActions.SelectShortcut(settings, WorkbenchCommand.TIMING_ENTER);
        UiTestActions.Click(settings, "RecordShortcutButton");
        UiTestActions.Find<TextBox>(settings, "GestureInput").Focus();
        UiTestActions.Press(settings, KeyGesture.Parse(gesture).Key);
    }

    private static object[] CaptureTree(NativeMenu root)
    {
        var identities = new List<object> { root };
        foreach (var item in root.Items)
        {
            identities.Add(item);
            if (item is NativeMenuItem { Menu: { } child })
            {
                identities.AddRange(CaptureTree(child));
            }
        }

        return identities.ToArray();
    }

    private static void AssertStableTree(Window window, object[] expected)
    {
        var actual = CaptureTree(Assert.IsType<NativeMenu>(NativeMenu.GetMenu(window)));
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Same(expected[index], actual[index]);
        }
    }

    private static Dictionary<NativeMenuItem, string?> CaptureBranchHeaders(object[] tree)
    {
        return tree.OfType<NativeMenuItem>().Where(item => item.Menu is not null)
            .ToDictionary(item => item, item => item.Header);
    }

    private static object[] AssertLanguageRefresh(Window window, object[] previous,
        Dictionary<NativeMenuItem, string?> previousHeaders)
    {
        var current = CaptureTree(Assert.IsType<NativeMenu>(NativeMenu.GetMenu(window)));
        Assert.Equal(previous.Length, current.Length);
        Assert.Same(previous[0], current[0]);
        if (!OperatingSystem.IsMacOS())
        {
            AssertStableTree(window, previous);
            return current;
        }

        var replacedBranches = 0;
        for (var index = 1; index < previous.Length; index++)
        {
            if (index + 1 < previous.Length && previous[index + 1] is NativeMenu oldSubmenu)
            {
                var oldHolder = Assert.IsType<NativeMenuItem>(previous[index]);
                var newHolder = Assert.IsType<NativeMenuItem>(current[index]);
                var newSubmenu = Assert.IsType<NativeMenu>(current[index + 1]);
                if (previousHeaders[oldHolder] == newHolder.Header)
                {
                    Assert.Same(oldHolder, newHolder);
                    Assert.Same(oldSubmenu, newSubmenu);
                    Assert.NotEmpty(newSubmenu.Items);
                }
                else
                {
                    Assert.NotSame(oldHolder, newHolder);
                    Assert.NotSame(oldSubmenu, newSubmenu);
                    Assert.Same(oldSubmenu, oldHolder.Menu);
                    Assert.Empty(oldSubmenu.Items);
                    Assert.Same(newSubmenu, newHolder.Menu);
                    replacedBranches++;
                }
                index++;
            }
            else
            {
                Assert.Same(previous[index], current[index]);
            }
        }

        Assert.True(replacedBranches > 0);
        return current;
    }

    private static void AssertDisjointTrees(object[] first, object[] second)
    {
        Assert.Empty(first.Intersect(second, ReferenceEqualityComparer.Instance));
    }
}
