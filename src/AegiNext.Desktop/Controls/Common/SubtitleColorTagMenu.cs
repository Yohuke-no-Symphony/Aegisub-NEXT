using System.Windows.Input;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Controls.Common;

internal sealed class SubtitleColorTagMenu : IDisposable
{
    private readonly List<IDisposable> bindings = [];
    private readonly Avalonia.Data.BindingExpressionBase titleBinding;

    internal SubtitleColorTagMenu(string name)
    {
        Item = new() { Name = name, Icon = WorkbenchIcon.Create("ColorTag") };
        titleBinding = Item.Bind(MenuItem.HeaderProperty, Localization.Observe("Workbench.ColorTag.Label").ToBinding());
    }

    internal MenuItem Item { get; }

    internal void Refresh(IReadOnlyList<SubtitleColorTag> projectTags, IReadOnlyList<SubtitleColorTag> personalTags,
        Guid? currentTagId, bool uniform, Func<bool> canExecute, Func<Guid?, Task> applyProject,
        Func<SubtitleColorTag, Task> applyPersonal, Func<Task> manage)
    {
        ReleaseItems();
        Item.IsEnabled = canExecute();
        var clear = CreateLocalizedItem("Workbench.ColorTag.Clear", new AsyncRelayCommand(() => applyProject(null), canExecute));
        clear.Name = "ClearSubtitleColorTagMenuItem";
        clear.Icon = WorkbenchIcon.Create("ClearColorTag");
        clear.ToggleType = MenuItemToggleType.CheckBox;
        clear.IsChecked = uniform && currentTagId is null;
        Item.Items.Add(clear);
        var projectDefinitions = new Dictionary<string, SubtitleColorTag>(StringComparer.Ordinal);
        foreach (var tag in projectTags)
        {
            if (tag.Id == currentTagId)
            {
                projectDefinitions[tag.Name] = tag;
            }
            else
            {
                projectDefinitions.TryAdd(tag.Name, tag);
            }
        }
        var displayed = new HashSet<string>(StringComparer.Ordinal);
        if (projectTags.Count > 0 || personalTags.Count > 0)
        {
            Item.Items.Add(new Separator());
        }
        foreach (var tag in personalTags)
        {
            if (!displayed.Add(tag.Name))
            {
                continue;
            }
            if (projectDefinitions.TryGetValue(tag.Name, out var project))
            {
                AddTag(project, new(() => applyProject(project.Id), canExecute), uniform && currentTagId == project.Id, "Project");
            }
            else
            {
                AddTag(tag, new(() => applyPersonal(tag), canExecute), false, "Personal");
            }
        }
        var personalCount = displayed.Count;
        foreach (var tag in projectTags)
        {
            if (!displayed.Add(tag.Name))
            {
                continue;
            }
            if (personalCount > 0 && displayed.Count == personalCount + 1)
            {
                Item.Items.Add(new Separator());
            }
            var project = projectDefinitions[tag.Name];
            AddTag(project, new(() => applyProject(project.Id), canExecute), uniform && currentTagId == project.Id, "Project");
        }
        Item.Items.Add(new Separator());
        var manager = CreateLocalizedItem("Workbench.ColorTag.Manage", new AsyncRelayCommand(manage));
        manager.Name = "ManageSubtitleColorTagsMenuItem";
        manager.Icon = WorkbenchIcon.Create("ManageColorTags");
        Item.Items.Add(manager);
    }

    private void AddTag(SubtitleColorTag tag, AsyncRelayCommand command, bool isChecked, string source)
    {
        Item.Items.Add(new MenuItem
        {
            Name = "SubtitleColorTag" + source + tag.Id.ToString("N"),
            Header = tag.Name,
            Icon = new Ellipse { Width = 12, Height = 12, Fill = SubtitleColorTagPalette.ResolveSwatch(tag.ColorHex) },
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = isChecked,
            Command = command
        });
    }

    private MenuItem CreateLocalizedItem(string key, ICommand? command = null)
    {
        var item = new MenuItem { Command = command };
        bindings.Add(item.Bind(MenuItem.HeaderProperty, Localization.Observe(key).ToBinding()));
        return item;
    }

    private void ReleaseItems()
    {
        Item.Items.Clear();
        foreach (var binding in bindings)
        {
            binding.Dispose();
        }
        bindings.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ReleaseItems();
        titleBinding.Dispose();
    }
}
