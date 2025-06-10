using CmdPalVsCode.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CmdPalVsCode;

internal sealed partial class VSCodePage : DynamicListPage
{
    private readonly SettingsManager _settingsManager;
    private readonly List<ListItem> _allItems = new List<ListItem>();
    private readonly object _itemsLock = new object();
    private bool _isLoading = false;

    public VSCodePage(SettingsManager settingsManager)
    {
        Icon = IconHelpers.FromRelativePath("Assets\\VsCodeIcon.png");
        Title = Resource.page_title;
        Name = Resource.page_command_name;
        
        _settingsManager = settingsManager;
        ShowDetails = _settingsManager.ShowDetails;
        _settingsManager.Settings.SettingsChanged += (s, a) =>
        {
            ShowDetails = _settingsManager.ShowDetails;
        };
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        lock (_itemsLock)
        {
            if (!_isLoading && _allItems.Count == 0)
            {
                // First time loading, or after a manual clear.
                Task.Run(LoadWorkspacesAsync);
            }
        }

        var filteredItems = GetFilteredItems();

        bool currentIsLoading;
        lock (_itemsLock)
        {
            currentIsLoading = _isLoading;
        }

        if (filteredItems.Count == 0 && !currentIsLoading)
        {
            return [
                new ListItem(new NoOpCommand()) {
                    Title = Resource.no_items_found,
                    Subtitle = Resource.no_items_found_subtitle,
                    Icon = IconHelpers.FromRelativePath("Assets\\VsCodeIcon.png")
                }
            ];
        }

        return filteredItems.ToArray();
    }

    private async Task LoadWorkspacesAsync()
    {
        lock (_itemsLock)
        {
            if (_isLoading)
            {
                return; // Already loading
            }
            _isLoading = true;
            _allItems.Clear();
        }

        IsLoading = true;
        RaiseItemsChanged();

        try
        {
            var workspaces = VSCodeHandler.GetWorkspaces();

            foreach (var workspace in workspaces)
            {
                var listItem = CreateListItemForWorkspace(workspace);
                lock (_itemsLock)
                {
                    _allItems.Add(listItem);
                }
                RaiseItemsChanged();
            }
        }
        finally
        {
            lock (_itemsLock)
            {
                _isLoading = false;
            }
            IsLoading = false;
            RaiseItemsChanged();
        }
    }
    
    private ListItem CreateListItemForWorkspace(VSCodeWorkspace workspace)
    {
        var command = new OpenVSCodeCommand(workspace.Instance.ExecutablePath, workspace.Path, this, _settingsManager.CommandResult);

        Details details = new Details()
        {
            Title = workspace.WorkspaceName,
            HeroImage = workspace.Instance.Icon,
            Metadata = workspace.Details,
        };

        var tags = new List<Tag>();
        switch (_settingsManager.TagType)
        {
            case "None":
                break;
            case "Type":
                tags.Add(new Tag(workspace.WorkspaceTypeString));
                if (workspace.VSTypeString != "")
                {
                    tags.Add(new Tag(workspace.VSTypeString));
                }
                break;
            case "Target":
                tags.Add(new Tag(workspace.Instance.Name));
                break;
            case "TypeAndTarget":
                tags.Add(new Tag(workspace.WorkspaceTypeString));
                if (workspace.VSTypeString != "")
                {
                    tags.Add(new Tag(workspace.VSTypeString));
                }
                tags.Add(new Tag(workspace.Instance.Name));
                break;
        }

        return new ListItem(command)
        {
            Title = details.Title,
            Subtitle = Uri.UnescapeDataString(workspace.Path),
            Details = details,
            Icon = workspace.Instance.Icon,
            Tags = tags.ToArray()
        };
    }

    private List<ListItem> GetFilteredItems()
    {
        List<ListItem> currentItems;
        lock (_itemsLock)
        {
            currentItems = new List<ListItem>(_allItems);
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return currentItems;
        }

        if (_settingsManager.UseStrichtSearch)
        {
            return currentItems.Where(x => x.Subtitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            var lowerSearchString = SearchText.ToLower(CultureInfo.CurrentUICulture);
            // fuzzy search on title
            return currentItems.Where(item =>
            {
                int charIndex = 0;
                var itemTitleLower = item.Title.ToLower(CultureInfo.CurrentUICulture);
                foreach (var character in lowerSearchString)
                {
                    charIndex = itemTitleLower.IndexOf(character, charIndex);
                    if (charIndex == -1)
                    {
                        return false;
                    }
                    charIndex++;
                }
                return true;
            }).ToList();
        }
        // Debug

        /* 
        var debugItem = new ListItem(new NoOpCommand())
        {
            Title = "Debug",
            Details = new Details()
            {
                Title = "Debug Information",
                Metadata = [
                new DetailsElement() { Key = "Timestamp", Data = new DetailsTags() { Tags = [new Tag(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))] } },
                new DetailsElement() { Key = "Timestamp", Data = new DetailsTags() { Tags = [new Tag(Debug)] } },
                ]
            },
        };
        items.Insert(0, debugItem);
        */
    }
}
