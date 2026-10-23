using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class WorkspaceSessionTestContext : IAsyncDisposable
{
    private readonly TemporaryWorkbenchDirectory directory = new();
    private readonly CultureInfo previousCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? previousDefaultCulture = CultureInfo.DefaultThreadCurrentUICulture;

    internal WorkspaceSessionTestContext(ProjectDocument? document = null, TimeProvider? persistenceTimeProvider = null,
        IProjectPersistenceStorage? persistenceStorage = null,
        Func<Action<VideoPreviewUpdate>, VideoPreviewController>? controllerFactory = null,
        DesktopApplicationContext? applicationContext = null,
        Func<string, int, MediaTimelineMapping, MediaTime, string, AudioAnalysisOptions, AudioAnalysisWorkerBudget?, AudioAnalysisSession>?
            analysisSessionFactory = null, bool hasAudio = false)
    {
        Editor = new(document);
        Session = new(Dialogs,
            controllerFactory ?? (update => new VideoPreviewController((path, token) => ProbeAsync(path, token, hasAudio), (_, _) => new(_ => Source), () => Converter,
                DispatchImmediately, update)), DispatchImmediately, Editor,
            applicationContext is null ? new WorkbenchPreferencesStore(directory.Path) : null,
            initialPreferences: new() { Projects = new() { WorkspaceRoot = Path.Combine(directory.Path, "workspace") } },
            persistenceTimeProvider: persistenceTimeProvider, persistenceStorage: persistenceStorage,
            applicationContext: applicationContext, analysisSessionFactory: analysisSessionFactory);
    }

    internal string DirectoryPath => directory.Path;
    internal ProjectEditor Editor { get; }
    internal WorkspaceDialogStub Dialogs { get; } = new();
    internal WorkbenchSession Session { get; }
    internal PreviewTestSource Source { get; } = new(7, 0, 100, 200);
    internal PreviewTestConverter Converter { get; } = new();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Session.DisposeAsync();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultCulture;
            directory.Dispose();
        }
    }

    internal Task InitializeAsync()
    {
        return Session.Styles.Completion;
    }

    private static Task<VideoPreviewMedia> ProbeAsync(string path, CancellationToken cancellationToken, bool hasAudio)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(1), hasAudio ? 1 : null));
    }

    private static Task DispatchImmediately(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
