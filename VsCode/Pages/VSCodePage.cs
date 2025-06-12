using CmdPalVsCode.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CmdPalVsCode;

internal sealed partial class VSCodePage : DynamicListPage, IDisposable
{
    private readonly SettingsManager _settingsManager;
    private readonly List<ListItem> _allItems = new List<ListItem>();
    private readonly List<ListItem> _filteredWorkspaces = new List<ListItem>();
    private readonly object _itemsLock = new object();

    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

    public VSCodePage(SettingsManager settingsManager)
    {
        Title = Resource.page_title;
        Icon = IconHelpers.FromRelativePath("Assets\\VsCodeIcon.svg");
        Name = Resource.page_command_name;

        _settingsManager = settingsManager;
        ShowDetails = _settingsManager.ShowDetails;
        _settingsManager.Settings.SettingsChanged += OnSettingsChanged;
    }

    public override IListItem[] GetItems()
    {
        lock (_itemsLock)
        {
            if (_allItems.Count == 0 && !IsLoading)
            {
                Task.Run(() => LoadWorkspacesAsync(_cancellationTokenSource.Token));
            }

            if (_filteredWorkspaces.Count == 0 && !IsLoading && !string.IsNullOrWhiteSpace(SearchText))
            {
                return new IListItem[]
                {
                    new ListItem(new NoOpCommand())
                    {
                        Title = Resource.no_items_found,
                        Subtitle = Resource.no_items_found_subtitle,
                        Icon = IconHelpers.FromRelativePath("Assets\\VsCodeIcon.svg"),
                    },
                };
            }

            return _filteredWorkspaces.ToArray();
        }
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _cancellationTokenSource.Token;

        Task.Delay(_settingsManager.SearchDelay, cancellationToken).ContinueWith(
            t =>
            {
                if (t.IsCanceled)
                {
                    return;
                }

                FilterWorkspaces(newSearch);
                RaiseItemsChanged();
            },
            cancellationToken);
    }

    public override void LoadMore()
    {
        IsLoading = true;
        RaiseItemsChanged();

        var allFilteredItems = GetFilteredItems();
        var currentCount = _filteredWorkspaces.Count;
        var moreItems = allFilteredItems.Skip(currentCount).Take(_settingsManager.PageSize).ToList();

        if (moreItems.Any())
        {
            _filteredWorkspaces.AddRange(moreItems);
        }

        HasMoreItems = _filteredWorkspaces.Count < allFilteredItems.Count;

        IsLoading = false;
        RaiseItemsChanged();
    }

    private void FilterWorkspaces(string searchText)
    {
        lock (_itemsLock)
        {
            var filtered = GetFilteredItems();
            _filteredWorkspaces.Clear();
            _filteredWorkspaces.AddRange(filtered.Take(_settingsManager.PageSize));
            HasMoreItems = filtered.Count > _settingsManager.PageSize;
        }
    }

    private async Task LoadWorkspacesAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        RaiseItemsChanged();

        try
        {
            var workspaces = await VSCodeHandler.GetWorkspacesAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var newItems = new List<ListItem>();
            foreach (var workspace in workspaces)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var listItem = CreateListItemForWorkspace(workspace);
                newItems.Add(listItem);
            }

            lock (_itemsLock)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                _allItems.Clear();
                _allItems.AddRange(newItems);
                FilterWorkspaces(SearchText);
            }
        }
        catch (OperationCanceledException)
        {
            // Task was canceled, which is expected.
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoading = false;
                RaiseItemsChanged();
            }
        }
    }

    private ListItem CreateListItemForWorkspace(VSCodeWorkspace workspace)
    {
        var command = new OpenVSCodeCommand(workspace.Instance.ExecutablePath, workspace.Path, this, _settingsManager.CommandResult);
        var details = new Details
        {
            Title = workspace.WorkspaceName,
            HeroImage = workspace.Instance.Icon,
            Metadata = workspace.Details,
        };

        var tags = new List<Tag>();
        if (_settingsManager.TagType.Contains("Type", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(new Tag(workspace.WorkspaceTypeString));
            if (!string.IsNullOrEmpty(workspace.VSTypeString))
            {
                tags.Add(new Tag(workspace.VSTypeString));
            }
        }

        if (_settingsManager.TagType.Contains("Target", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(new Tag(workspace.Instance.Name));
        }

        return new ListItem(command)
        {
            Title = details.Title,
            Subtitle = Uri.UnescapeDataString(workspace.Path),
            Details = details,
            Icon = workspace.Instance.Icon,
            Tags = tags.ToArray(),
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

        var matcher = StringMatcher.Instance;
        matcher.UserSettingSearchPrecision = SearchPrecisionScore.Regular;

        if (_settingsManager.UseStrichtSearch)
        {
            return currentItems.Where(x => x.Subtitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || x.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            return currentItems
                .Select(item => new { item, match = matcher.FuzzyMatch(SearchText, item.Title) })
                .Where(x => x.match.Success)
                .OrderByDescending(x => x.match.Score)
                .Select(x => x.item)
                .ToList();
        }
    }

    private void OnSettingsChanged(object sender, Settings e)
    {
        ShowDetails = _settingsManager.ShowDetails;
        if (SearchText is not null)
        {
            FilterWorkspaces(SearchText);
        }
        RaiseItemsChanged();
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _settingsManager.Settings.SettingsChanged -= OnSettingsChanged;
    }
}
