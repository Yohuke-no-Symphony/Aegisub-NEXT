using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Tests.Startup;

[Collection("Workspace session")]
public sealed class EffectLibraryApplicationContextTests
{
    [Fact]
    public async Task SharedLibraryResourcesAreSeparateFromSettingsAndCoveredByInitialization()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;

        var styles = context.GetLibraryResource(PersonalLibraryKind.STYLE);
        var effects = context.GetLibraryResource(PersonalLibraryKind.EFFECT);
        Assert.Equal(styles, effects);
        Assert.DoesNotContain(styles, context.SettingsResources);
        Assert.Contains(styles, context.InitializationResources);
        Assert.Contains(styles, context.EffectLibraryResources);
    }

    [Fact]
    public async Task MixedCommitPublishesBothLibrariesBeforeEitherNotification()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Shared", new());
        var effect = new EffectScriptPreset(Guid.NewGuid(), "Shared effect",
            AegiNext.Core.Effects.BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", "\"shared-effect\"", StringComparison.Ordinal));
        var notifications = 0;
        context.StylesChanged += Observe;
        context.EffectsChanged += Observe;

        await context.RunEffectLibraryOperationAsync(() => context.EffectLibrary.UpdateAsync(
            context.EffectLibrary.Snapshot.Revision, snapshot => snapshot with
            {
                Styles = snapshot.Styles with { Presets = [style] },
                Effects = snapshot.Effects with { Presets = [effect] }
            }));

        Assert.Equal(2, notifications);
        Assert.False(context.StylesBusy);
        Assert.False(context.EffectsBusy);

        void Observe(object? sender, EventArgs e)
        {
            Assert.Equal(style, Assert.Single(context.StyleLibrary.Snapshot.Presets));
            Assert.Equal(effect, Assert.Single(context.EffectScriptLibrary.Snapshot.Presets));
            notifications++;
        }
    }

    [Fact]
    public async Task CorruptEffectLibraryDoesNotPreventSettingsExport()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "effect-scripts.json"), "{broken");
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;

        Assert.NotNull(context.EffectLibraryLoadError);
        Assert.Null(context.SettingsLoadError);
        var output = Path.Combine(directory.Path, "settings.aegisettings");
        await context.Tasks.Submit(new AegiNext.Desktop.Settings.Transfer.UserSettingsExportTask(context, new(), output)).Completion;
        Assert.True(File.Exists(output));
    }
}
