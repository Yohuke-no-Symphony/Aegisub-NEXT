using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests;

public sealed class OpenAudioCommandTests
{
    [Fact]
    public void OpenAudioFollowsOpenMediaInTheFileMenu()
    {
        var file = WorkbenchMenuCatalog.Groups.Single(group => group.Key == "File");
        var commands = file.Commands.ToList();
        var media = commands.IndexOf(WorkbenchCommand.OPEN_MEDIA);

        Assert.True(media >= 0);
        Assert.Equal(WorkbenchCommand.OPEN_AUDIO, commands[media + 1]);
    }

    [Fact]
    public void OpenAudioHasNoDefaultShortcutAndIsAppendedAfterExistingCommands()
    {
        var bindings = ShortcutDefaults.CreateBindings();
        var gesture = bindings.Single(binding => binding.Command == WorkbenchCommand.OPEN_AUDIO).Gesture;

        Assert.Equal(string.Empty, gesture);
        Assert.True(WorkbenchCommand.OPEN_AUDIO > WorkbenchCommand.AUTO_TIMING);
    }
}
