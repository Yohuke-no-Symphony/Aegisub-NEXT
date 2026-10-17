using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Shortcuts;

public sealed class ShortcutGestureFormatterTests
{
    [Theory]
    [InlineData(true, "CmdOrCtrl+Shift+S", "Cmd|Shift|S")]
    [InlineData(false, "CmdOrCtrl+Shift+S", "Ctrl|Shift|S")]
    [InlineData(true, "Control+K", "Ctrl|K")]
    [InlineData(false, "Meta+K", "Win|K")]
    [InlineData(true, "CmdOrCtrl+Control+Alt+Shift+K", "Cmd|Ctrl|Alt|Shift|K")]
    [InlineData(false, "shift+ctrl+s", "Ctrl|Shift|S")]
    [InlineData(true, "CmdOrCtrl+OemComma", "Cmd|,")]
    [InlineData(false, "Control++", "Ctrl|+")]
    [InlineData(false, "D0", "0")]
    [InlineData(true, "D9", "9")]
    [InlineData(true, "Shift+Return", "Shift|Enter")]
    [InlineData(false, "Back", "Backspace")]
    [InlineData(true, "Escape", "Esc")]
    [InlineData(false, "Space", "Space")]
    public void LabelsResolveThePlatformAndPreservePhysicalModifierMeaning(bool mac, string gesture, string expected)
    {
        var labels = ShortcutGestureFormatter.GetKeyLabels(gesture, mac);
        Assert.Equal(expected.Split('|'), labels);
        Assert.Equal(string.Join('+', labels), ShortcutGestureFormatter.Format(gesture, mac));
        Assert.DoesNotContain("CmdOrCtrl", labels);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, " ")]
    public void DisabledBindingsHaveNoKeyLabels(bool mac, string gesture)
    {
        Assert.Empty(ShortcutGestureFormatter.GetKeyLabels(gesture, mac));
        Assert.Equal(string.Empty, ShortcutGestureFormatter.Format(gesture, mac));
    }

    [Fact]
    public void InvalidDraftsRemainTheResponsibilityOfTheDisplayControl()
    {
        Assert.Throws<InvalidDataException>(() => ShortcutGestureFormatter.GetKeyLabels("Control+"));
    }
}
