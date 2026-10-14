using AegiNext.Application.Presets;
using AegiNext.Desktop.Settings.Effects;
using AegiNext.Desktop.Workspace.Diagnostics;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class EffectScriptFailureWorkflowTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("delete")]
    [InlineData("import")]
    [InlineData("export")]
    public async Task OperationFailureReturnsItsSingleRecordedEntryAfterPersistenceRejectsTheOperation(string operation)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        await session.EffectScripts.Completion;
        var model = new EffectSettingsViewModel();
        await model.AddCommand.ExecuteAsync(null);
        var preset = Assert.IsType<EffectScriptPreset>(model.Draft);
        if (operation == "delete")
        {
            await session.EffectScripts.UpsertAsync(preset);
        }
        var original = session.EffectScriptLibrary.Snapshot;
        if (operation is "save" or "delete")
        {
            var path = session.ApplicationContext.EffectLibrary.ManifestPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Delete(path);
            Directory.CreateDirectory(path);
        }
        else if (operation == "import")
        {
            context.Dialogs.OpenPath = Path.Combine(context.DirectoryPath, "invalid.aegifx");
            await File.WriteAllBytesAsync(context.Dialogs.OpenPath, new byte[] { 0xFF, 0xFF });
        }
        else
        {
            context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "blocked.aegifx");
            Directory.CreateDirectory(context.Dialogs.SavePath);
        }
        session.Journal.Clear();
        var failures = new List<WorkbenchLogEntry>();
        Func<Task> action = operation switch
        {
            "save" => () => session.EffectScripts.UpsertAsync(preset),
            "delete" => () => session.EffectScripts.DeleteAsync(preset.Id),
            "import" => session.EffectScripts.ImportAsync,
            _ => () => session.EffectScripts.ExportAsync(preset)
        };
        session.EffectScripts.Queue(action, entry =>
        {
            Assert.Contains(entry, session.Journal.Entries);
            failures.Add(entry);
        });
        await session.EffectScripts.Completion;
        Assert.False(session.EffectScripts.IsBusy);
        Assert.Same(original, session.EffectScriptLibrary.Snapshot);
        var entry = Assert.Single(session.Journal.Entries);
        Assert.Same(entry, Assert.Single(failures));
        Assert.Equal(WorkbenchLogLevel.ERROR, entry.Level);
        Assert.Equal(session.LastError!.ToString(), entry.Details);
        if (operation == "import")
        {
            Assert.Contains("DecoderFallbackException", entry.Details);
        }
    }

    [Fact]
    public async Task SuccessCancellationAndOrdinaryQueuedFailureDoNotInvokeAnotherOperationsFailureCallback()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        await session.EffectScripts.Completion;
        session.Journal.Clear();
        var failures = new List<WorkbenchLogEntry>();
        session.EffectScripts.Queue(() => Task.CompletedTask, failures.Add);
        session.EffectScripts.Queue(() => Task.FromException(new OperationCanceledException()), failures.Add);
        session.EffectScripts.Queue(() => Task.FromException(new IOException("background operation")));
        await session.EffectScripts.Completion;
        Assert.Empty(failures);
        Assert.Single(session.Journal.Entries, entry => entry.Level == WorkbenchLogLevel.ERROR);
        Assert.False(session.EffectScripts.IsBusy);
    }

    [Fact]
    public async Task FailureCallbackCarriesTheOriginalEntryEvenWhenAnotherErrorOverwritesLastError()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        await session.EffectScripts.Completion;
        session.Journal.Clear();
        var failure = new InvalidOperationException("script operation", new IOException("nested original cause"));
        WorkbenchLogEntry? observed = null;
        session.EffectScripts.Queue(() => Task.FromException(failure), entry =>
        {
            session.ShowError(new IOException("another workflow"));
            observed = entry;
        });
        await session.EffectScripts.Completion;
        Assert.Equal(2, session.Journal.Entries.Count);
        Assert.Same(session.Journal.Entries[0], observed);
        Assert.Equal(failure.ToString(), observed!.Details);
        Assert.Equal("another workflow", session.LastError!.Message);
    }
}
