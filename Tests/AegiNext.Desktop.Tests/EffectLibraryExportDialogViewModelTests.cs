using AegiNext.Application.Presets;
using AegiNext.Desktop.EffectLibrary.Transfer;

namespace AegiNext.Desktop.Tests;

/// <summary>验证保存快照的树选择、搜索和可重试导出边界。</summary>
[Collection("Workspace session")]
public sealed class EffectLibraryExportDialogViewModelTests
{
    /// <summary>每次打开默认不选任何素材并使用包格式，树根展开且仅含快照个人素材。</summary>
    [Fact]
    public void NewDialogStartsWithNoSelectionAndTwoExpandedPersonalRoots()
    {
        var snapshot = EffectLibraryExportDialogTestData.Snapshot();
        using var model = new EffectLibraryExportDialogViewModel(snapshot, (_, _) => Task.FromResult(false));
        Assert.Equal(2, model.Roots.Count);
        Assert.All(model.Roots, root => Assert.True(root.IsExpanded));
        Assert.Equal(4, model.TotalCount);
        Assert.Equal(4, model.VisibleCount);
        Assert.Equal(0, model.SelectedCount);
        Assert.False(model.ExportSelectedCommand.CanExecute(null));
        Assert.True(model.ExportAllCommand.CanExecute(null));
        Assert.Equal(EffectLibraryExportFormat.PACKAGE, model.SelectedFormat);
        Assert.DoesNotContain(model.ExportFormats, choice => choice.Format == EffectLibraryExportFormat.SOURCE);
        Assert.All(model.Roots.SelectMany(root => root.Children), node => Assert.False(node.IsChecked));
        Assert.Equal(snapshot.Styles.Presets.Select(preset => preset.Id), model.StyleRoot.Children.Select(node => node.Style!.Id));
        Assert.Equal(snapshot.Effects.Presets.Select(preset => preset.Id), model.EffectRoot.Children.Select(node => node.Effect!.Id));
        model.StyleRoot.IsChecked = true;
        using var reopened = new EffectLibraryExportDialogViewModel(snapshot, (_, _) => Task.FromResult(false));
        Assert.Equal(0, reopened.SelectedCount);
    }

    /// <summary>样式和特效共享 Guid 时仍是不同类别的选择身份，搜索不匹配源码正文。</summary>
    [Fact]
    public async Task StyleAndEffectWithTheSameGuidRemainIndependentAndSourceBodyIsNotSearched()
    {
        var snapshot = EffectLibraryExportDialogTestData.Snapshot();
        snapshot = snapshot with
        {
            Effects = snapshot.Effects with
            {
                Presets = snapshot.Effects.Presets.SetItem(0, snapshot.Effects.Presets[0] with { Id = snapshot.Styles.Presets[0].Id })
            }
        };
        EffectLibraryExportRequest? exported = null;
        await using var model = new EffectLibraryExportDialogViewModel(snapshot, (request, _) =>
        {
            exported = request;
            return Task.FromResult(false);
        });
        var style = model.StyleRoot.Children[0];
        var effect = model.EffectRoot.Children[0];
        style.IsChecked = true;
        Assert.False(effect.IsChecked);
        effect.IsChecked = true;
        Assert.Equal(2, model.SelectedCount);
        style.IsChecked = false;
        Assert.True(effect.IsChecked);
        model.SelectedFormat = EffectLibraryExportFormat.SOURCE;
        model.SearchText = "opacity";
        Assert.Equal(0, model.VisibleCount);
        Assert.Equal(1, model.HiddenSelectedCount);
        await model.ExportSelectedCommand.ExecuteAsync(null);
        Assert.Empty(exported!.Styles);
        Assert.Same(snapshot.Effects.Presets[0], Assert.Single(exported.Effects));
        Assert.Equal(EffectLibraryExportFormat.SOURCE, exported.Format);
    }

    /// <summary>搜索同时匹配展示名与 DSL 身份，隐藏选择仍计入总数和导出。</summary>
    [Fact]
    public async Task CaseInsensitiveNameAndDslSearchRetainHiddenSelectionAndExportIt()
    {
        var snapshot = EffectLibraryExportDialogTestData.Snapshot();
        EffectLibraryExportRequest? exported = null;
        await using var model = new EffectLibraryExportDialogViewModel(snapshot, (request, _) =>
        {
            exported = request;
            return Task.FromResult(false);
        });
        model.StyleRoot.Children[0].IsChecked = true;
        model.SearchText = "GLOW-SCRIPT";
        var effect = Assert.Single(model.EffectRoot.Children);
        Assert.Empty(model.StyleRoot.Children);
        Assert.Equal("glow-script", effect.DslId);
        Assert.Equal(1, model.HiddenSelectedCount);
        model.SelectVisibleCommand.Execute(null);
        Assert.Equal(2, model.SelectedCount);
        Assert.Equal(1, model.HiddenSelectedCount);
        await model.ExportSelectedCommand.ExecuteAsync(null);
        Assert.Same(snapshot.Styles.Presets[0], Assert.Single(exported!.Styles));
        Assert.Same(snapshot.Effects.Presets[0], Assert.Single(exported.Effects));
        Assert.Equal(EffectLibraryExportFormat.PACKAGE, exported.Format);
        Assert.Equal(2, model.SelectedCount);
        model.SearchText = "sOfT";
        Assert.Equal(snapshot.Effects.Presets[1].Id, Assert.Single(model.EffectRoot.Children).Effect!.Id);
        Assert.Equal(2, model.HiddenSelectedCount);
        model.SearchText = "字幕";
        Assert.Single(model.StyleRoot.Children);
        Assert.Empty(model.EffectRoot.Children);
        Assert.Equal(1, model.HiddenSelectedCount);
    }

    /// <summary>分组只操作当前可见项，partial 点击全选可见项，全局清空包括隐藏项。</summary>
    [Fact]
    public void GroupCheckStateIsDerivedFromVisibleLeavesAndPartialClickSelectsAllMatches()
    {
        using var model = new EffectLibraryExportDialogViewModel(EffectLibraryExportDialogTestData.Snapshot(), (_, _) => Task.FromResult(false));
        model.StyleRoot.Children[0].IsChecked = true;
        Assert.Null(model.StyleRoot.IsChecked);
        model.StyleRoot.IsChecked = false;
        Assert.True(model.StyleRoot.IsChecked);
        Assert.Equal(2, model.SelectedCount);
        model.SearchText = "compact";
        model.StyleRoot.IsChecked = false;
        Assert.False(model.StyleRoot.IsChecked);
        Assert.Equal(1, model.SelectedCount);
        Assert.Equal(1, model.HiddenSelectedCount);
        model.SelectVisibleCommand.Execute(null);
        Assert.Equal(2, model.SelectedCount);
        Assert.Equal(1, model.HiddenSelectedCount);
        model.SearchText = "missing";
        Assert.False(model.StyleRoot.CanSelect);
        Assert.False(model.EffectRoot.CanSelect);
        Assert.False(model.SelectVisibleCommand.CanExecute(null));
        Assert.True(model.ExportSelectedCommand.CanExecute(null));
        model.ClearSelectionCommand.Execute(null);
        Assert.Equal(0, model.SelectedCount);
        Assert.Equal(0, model.HiddenSelectedCount);
        Assert.False(model.ExportSelectedCommand.CanExecute(null));
    }

    /// <summary>源码格式只允许非空且完全由脚本组成的总选择，隐藏样式同样会阻止源码格式。</summary>
    [Fact]
    public void SourceFormatDependsOnAllSelectionsIncludingHiddenStyles()
    {
        using var model = new EffectLibraryExportDialogViewModel(EffectLibraryExportDialogTestData.Snapshot(), (_, _) => Task.FromResult(false));
        model.EffectRoot.Children[0].IsChecked = true;
        model.SelectedFormat = EffectLibraryExportFormat.SOURCE;
        model.SearchText = "glow-script";
        Assert.Equal(EffectLibraryExportFormat.SOURCE, model.SelectedFormat);
        model.SearchText = string.Empty;
        model.StyleRoot.Children[0].IsChecked = true;
        model.SearchText = "glow-script";
        Assert.Equal(EffectLibraryExportFormat.PACKAGE, model.SelectedFormat);
        Assert.DoesNotContain(model.ExportFormats, choice => choice.Format == EffectLibraryExportFormat.SOURCE);
        Assert.Throws<ArgumentException>(() => model.SelectedFormat = EffectLibraryExportFormat.SOURCE);
        model.ClearSelectionCommand.Execute(null);
        Assert.DoesNotContain(model.ExportFormats, choice => choice.Format == EffectLibraryExportFormat.SOURCE);
    }

    /// <summary>导出全部忽略筛选和勾选，始终冻结完整个人快照并使用包格式。</summary>
    [Fact]
    public async Task ExportAllAlwaysUsesTheWholeSavedSnapshotAsAPackage()
    {
        var snapshot = EffectLibraryExportDialogTestData.Snapshot();
        EffectLibraryExportRequest? exported = null;
        await using var model = new EffectLibraryExportDialogViewModel(snapshot, (request, _) =>
        {
            exported = request;
            return Task.FromResult(false);
        });
        model.EffectRoot.Children[0].IsChecked = true;
        model.SelectedFormat = EffectLibraryExportFormat.SOURCE;
        model.SearchText = "does not match";
        await model.ExportAllCommand.ExecuteAsync(null);
        Assert.Equal(snapshot.Styles.Presets, exported!.Styles);
        Assert.Equal(snapshot.Effects.Presets, exported.Effects);
        Assert.Equal(EffectLibraryExportFormat.PACKAGE, exported.Format);
        Assert.Equal(1, model.SelectedCount);
        Assert.Equal(1, model.HiddenSelectedCount);
        Assert.Equal(EffectLibraryExportFormat.SOURCE, model.SelectedFormat);
    }

    /// <summary>文件选择取消不清空选择；异常本地显示后同一请求可重试成功。</summary>
    [Fact]
    public async Task PickerCancellationAndExceptionsPreserveSelectionAndAllowRetry()
    {
        var attempts = 0;
        var completed = 0;
        await using var model = new EffectLibraryExportDialogViewModel(EffectLibraryExportDialogTestData.Snapshot(), (_, _) =>
        {
            attempts++;
            return attempts switch
            {
                1 => Task.FromResult(false),
                2 => Task.FromException<bool>(new IOException("Destination unavailable")),
                _ => Task.FromResult(true)
            };
        });
        model.Exported += (_, _) => completed++;
        model.StyleRoot.Children[0].IsChecked = true;
        await model.ExportSelectedCommand.ExecuteAsync(null);
        Assert.False(model.HasError);
        Assert.Equal(0, completed);
        await model.ExportSelectedCommand.ExecuteAsync(null);
        Assert.Contains("Destination unavailable", model.Error, StringComparison.Ordinal);
        Assert.Equal(1, model.SelectedCount);
        Assert.Equal(0, completed);
        Assert.True(model.ExportSelectedCommand.CanExecute(null));
        await model.ExportSelectedCommand.ExecuteAsync(null);
        Assert.Equal(3, attempts);
        Assert.Equal(1, completed);
        Assert.False(model.HasError);
        Assert.True(model.WasExported);
    }

    /// <summary>操作过程中输入和命令锁定，关闭取消操作并等待晚到回调清理，不交付成功。</summary>
    [Fact]
    public async Task ClosingCancelsAndWaitsForTheInFlightExportWithoutAcceptingALateSuccess()
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exported = 0;
        CancellationToken observed = default;
        await using var model = new EffectLibraryExportDialogViewModel(EffectLibraryExportDialogTestData.Snapshot(), (_, token) =>
        {
            observed = token;
            entered.TrySetResult();
            return result.Task;
        });
        model.Exported += (_, _) => exported++;
        try
        {
            var leaf = model.StyleRoot.Children[0];
            leaf.IsChecked = true;
            var operation = model.ExportSelectedCommand.ExecuteAsync(null);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(model.IsBusy);
            Assert.False(model.CanInteract);
            Assert.False(model.ExportAllCommand.CanExecute(null));
            model.SearchText = "changed";
            leaf.IsChecked = false;
            Assert.Equal(string.Empty, model.SearchText);
            Assert.True(leaf.IsChecked);
            var completion = model.Completion;
            model.Dispose();
            var disposal = model.DisposalCompletion;
            Assert.True(observed.IsCancellationRequested);
            Assert.False(completion.IsCompleted);
            Assert.False(disposal.IsCompleted);
            result.TrySetResult(true);
            await Task.WhenAll(operation, completion, disposal).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, exported);
            Assert.False(model.WasExported);
            Assert.False(model.HasError);
            model.Dispose();
            Assert.Same(disposal, model.DisposalCompletion);
        }
        finally
        {
            result.TrySetResult(false);
        }
    }

    /// <summary>空库和预取消请求不能启动导出，语言刷新不改变勾选、搜索或格式。</summary>
    [Fact]
    public void EmptyOrCancelledModelsDisableExportAndLanguageRefreshKeepsBusinessState()
    {
        using var empty = new EffectLibraryExportDialogViewModel(new(), (_, _) => Task.FromResult(true));
        Assert.False(empty.ExportSelectedCommand.CanExecute(null));
        Assert.False(empty.ExportAllCommand.CanExecute(null));
        Assert.True(empty.IsEmpty);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var inactive = new EffectLibraryExportDialogViewModel(EffectLibraryExportDialogTestData.Snapshot(), (_, _) => Task.FromResult(true), cancelled.Token);
        Assert.False(inactive.CanInteract);
        Assert.False(inactive.ExportAllCommand.CanExecute(null));
        using var model = new EffectLibraryExportDialogViewModel(EffectLibraryExportDialogTestData.Snapshot(), (_, _) => Task.FromResult(false));
        model.EffectRoot.Children[0].IsChecked = true;
        model.SelectedFormat = EffectLibraryExportFormat.SOURCE;
        model.SearchText = "glow-script";
        var selected = Assert.Single(model.EffectRoot.Children);
        model.RefreshLanguage();
        Assert.Same(selected, Assert.Single(model.EffectRoot.Children));
        Assert.True(selected.IsChecked);
        Assert.Equal("glow-script", model.SearchText);
        Assert.Equal(EffectLibraryExportFormat.SOURCE, model.SelectedFormat);
    }
}
