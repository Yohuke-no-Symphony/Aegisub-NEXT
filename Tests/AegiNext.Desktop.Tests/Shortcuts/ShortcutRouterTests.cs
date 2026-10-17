using AegiNext.Desktop.Shortcuts;
using Avalonia.Input;

namespace AegiNext.Desktop.Tests.Shortcuts;

public sealed class ShortcutRouterTests
{
    [Theory]
    [InlineData(true, KeyModifiers.Meta, KeyModifiers.Control)]
    [InlineData(false, KeyModifiers.Control, KeyModifiers.Meta)]
    public void PlatformPrimaryModifierMatchesExactlyAndSaveWorksInText(bool mac, KeyModifiers primary, KeyModifiers other)
    {
        var router = new ShortcutRouter(ShortcutDefaults.CreateBindings(), mac);
        Assert.True(router.TryResolve(Key.S, primary, true, out var save));
        Assert.Equal(WorkbenchCommand.SAVE_PROJECT, save);
        Assert.False(router.TryResolve(Key.S, other, false, out _));
        Assert.False(router.TryResolve(Key.S, primary | other, false, out _));
        Assert.False(router.TryResolve(Key.S, primary | KeyModifiers.Alt, false, out _));
        Assert.True(router.TryResolve(Key.S, primary | KeyModifiers.Shift, false, out var saveAs));
        Assert.Equal(WorkbenchCommand.SAVE_PROJECT_AS, saveAs);
    }

    [Theory]
    [InlineData(Key.Space, KeyModifiers.None)]
    [InlineData(Key.Left, KeyModifiers.None)]
    [InlineData(Key.Right, KeyModifiers.Control)]
    [InlineData(Key.Delete, KeyModifiers.None)]
    [InlineData(Key.Z, KeyModifiers.Control)]
    [InlineData(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift)]
    [InlineData(Key.Y, KeyModifiers.Control)]
    [InlineData(Key.A, KeyModifiers.Meta)]
    [InlineData(Key.C, KeyModifiers.Control)]
    [InlineData(Key.V, KeyModifiers.Meta)]
    [InlineData(Key.Enter, KeyModifiers.Control)]
    [InlineData(Key.S, KeyModifiers.Alt | KeyModifiers.Control)]
    [InlineData(Key.S, KeyModifiers.None)]
    public void TextEditingKeysAreNeverHijackedEvenWhenCustomBound(Key key, KeyModifiers modifiers)
    {
        var gesture = ShortcutConfiguration.FormatGesture(key, modifiers, false);
        var router = new ShortcutRouter([new(WorkbenchCommand.PLAY_PAUSE, gesture)], false);
        Assert.False(router.TryResolve(key, modifiers, true, out _));
        Assert.True(router.TryResolve(key, modifiers, false, out var command));
        Assert.Equal(WorkbenchCommand.PLAY_PAUSE, command);
    }

    [Theory]
    [InlineData(Key.F8, WorkbenchCommand.TIMING_ENTER)]
    [InlineData(Key.F9, WorkbenchCommand.TIMING_EXIT)]
    public void TimingFunctionKeysWorkInTextAndOtherControls(Key key, WorkbenchCommand expected)
    {
        var router = new ShortcutRouter(ShortcutDefaults.CreateBindings());
        Assert.True(router.TryResolve(key, KeyModifiers.None, true, out var textCommand));
        Assert.True(router.TryResolve(key, KeyModifiers.None, false, out var otherCommand));
        Assert.Equal(expected, textCommand);
        Assert.Equal(expected, otherCommand);
    }

    [Fact]
    public void RouterCopiesConfigurationAndDoesNotRestoreDisabledBindings()
    {
        var bindings = new List<ShortcutBinding> { new(WorkbenchCommand.PLAY_PAUSE, ""), new(WorkbenchCommand.TIMING_ENTER, "F8") };
        var router = new ShortcutRouter(bindings);
        bindings[1] = new(WorkbenchCommand.TIMING_ENTER, "F9");
        Assert.False(router.TryResolve(Key.Space, KeyModifiers.None, false, out _));
        Assert.False(router.TryResolve(Key.F9, KeyModifiers.None, false, out _));
        Assert.True(router.TryResolve(Key.F8, KeyModifiers.None, false, out _));
    }
}
