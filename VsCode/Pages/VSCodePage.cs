using CmdPalVsCode.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CmdPalVsCode;

internal sealed partial class VSCodePage : DynamicListPage, IDisposable
{
    private const int PageSize = 50;
    private readonly SettingsManager _settingsManager;
    private readonly List<ListItem> _allItems = new List<ListItem>();
    private readonly object _itemsLock = new object();
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private int _currentPage = 1;

    public VSCodePage(SettingsManager settingsManager)
    {
        Icon = IconHelpers.FromRelativePath("Assets\\VsCodeIcon.png");
        Title = Resource.page_title;
        Name = Resource.page_command_name;
        
        _settingsManager = settingsManager;
        ShowDetails = _settingsManager.ShowDetails;
        _settingsManager.Settings.SettingsChanged += OnSettingsChanged;
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        _currentPage = 1; // Reset to first page on new search
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        lock (_itemsLock)
        {
            if (!IsLoading && _allItems.Count == 0)
            {
                Task.Run(() => LoadWorkspacesAsync(_cancellationTokenSource.Token));
            }
        }

        var filteredItems = GetFilteredItems();

        if (filteredItems.Count == 0 && !IsLoading)
        {
            return [
                new ListItem(new NoOpCommand()) {
                    Title = Resource.no_items_found,
                    Subtitle = Resource.no_items_found_subtitle,
                    Icon = IconHelpers.FromRelativePath("Assets\\VsCodeIcon.png")
                }
            ];
        }

        return filteredItems.Take(PageSize * _currentPage).ToArray();
    }
    
    public override void LoadMore()
    {
        _currentPage++;
        RaiseItemsChanged();
    }

    private async Task LoadWorkspacesAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        RaiseItemsChanged();

        try
        {
            var workspaces = await Task.Run(() => VSCodeHandler.GetWorkspaces(cancellationToken), cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            var newItems = new List<ListItem>();
            foreach (var workspace in workspaces)
            {
                if (cancellationToken.IsCancellationRequested) return;
                var listItem = CreateListItemForWorkspace(workspace);
                newItems.Add(listItem);
            }

            lock (_itemsLock)
            {
                if (cancellationToken.IsCancellationRequested) return;
                _allItems.Clear();
                _allItems.AddRange(newItems);
            }
        }
        catch (OperationCanceledException)
        {
            // Task was cancelled, which is expected on dispose.
        }
        finally
        {
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
            return currentItems.Where(x => x.Subtitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || x.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
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
    }

    private void OnSettingsChanged(object sender, Settings args)
    {
        ShowDetails = _settingsManager.ShowDetails;
    }
    
    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _settingsManager.Settings.SettingsChanged -= OnSettingsChanged;
    }
}
