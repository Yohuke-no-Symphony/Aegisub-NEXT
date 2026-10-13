using AegiNext.Desktop.Panels.EffectPresets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class EffectPresetPanelTestHost : IAsyncDisposable
{
    private readonly MainWindowTestContext context;

    private EffectPresetPanelTestHost(MainWindowTestContext context, EffectPresetPanelTestRenderer renderer, Action<Action>? postDrain)
    {
        this.context = context;
        Renderer = renderer;
        View = new(context.ViewModel.EffectPresets, context.Session, () => Renderer, postDrain);
        Window = new() { Width = 680, Height = 520, Content = View };
        Window.Show();
        Flush();
    }

    internal static async Task<EffectPresetPanelTestHost> CreateAsync(EffectPresetPanelTestRenderer? renderer = null, Action<Action>? postDrain = null)
    {
        var context = new MainWindowTestContext();
        try
        {
            await context.Session.ApplicationContext.Initialization;
            await context.Session.EffectScripts.Completion;
            await context.Session.Fonts.EnsureLoadedAsync();
            return new(context, renderer ?? new(), postDrain);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    internal Window Window { get; }
    internal EffectPresetsPanelView View { get; }
    internal EffectPresetPanelTestRenderer Renderer { get; }
    internal AegiNext.Desktop.Workspace.WorkbenchSession Session => context.Session;
    internal EffectPresetsPanelViewModel ViewModel => context.ViewModel.EffectPresets;
    internal ListBox List => View.FindControl<ListBox>("EffectPresetList")!;
    internal EffectPresetCardView Card(string id) => View.GetVisualDescendants().OfType<EffectPresetCardView>().Single(card => card.ViewModel.Id == id);
    internal Point Center(Control control) => control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)!.Value;

    internal void Flush()
    {
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = Window.CaptureRenderedFrame();
    }

    internal async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Preview timed out: Active={View.ActivePreviewId}; Pending={View.PendingPreviewCount}; Rendered={Renderer.Rendered.Count}.");
            Flush();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Flush();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            Window.Close();
            await View.DisposeAsync();
            Assert.Equal(1, Renderer.DisposeCount);
        }
        finally
        {
            await context.DisposeAsync();
        }
    }
}
