using CmdPalVsCode.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace CmdPalVsCode;

internal sealed partial class VSCodePage : DynamicListPage
{
    private readonly SettingsManager _settingsManager;
    private List<ListItem> _allItems = new List<ListItem>();
    private Dictionary<ListItem, string> _lowerSearchTextCache = new Dictionary<ListItem, string>();
    public static bool LoadItems = true;


    public VSCodePage(SettingsManager settingsManager)
    {
        Icon = IconHelpers.FromRelativePath("Assets\\VsCodeIcon.png");
        Title = Resource.page_title;
        Name = Resource.page_command_name;
        ShowDetails = settingsManager.ShowDetails;

        _settingsManager = settingsManager;
        _settingsManager.Settings.SettingsChanged += (s, a) =>
        {
            ShowDetails = settingsManager.ShowDetails;
        };
    }


    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        RaiseItemsChanged();
    }

    public async Task InitializeItemList()
    {
        IsLoading = true;
        _allItems = new List<ListItem>();
        var workspaces = await VSCodeHandler.GetWorkspaces();

        foreach (var workspace in workspaces)
        {
            // add instance to the list
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

            var listItem = new ListItem(command)
            {
                Title = details.Title,
                Subtitle = Uri.UnescapeDataString(workspace.Path),
                Details = details,
                Icon = workspace.Instance.Icon,
                Tags = tags.ToArray(),
                MoreCommands = new IContextItem[]
                {
                    new CommandContextItem(new CopyPathCommand(workspace.Path))
                }
            };

            _allItems.Add(listItem);
            _lowerSearchTextCache[listItem] = listItem.Subtitle.ToLower(CultureInfo.CurrentUICulture);
        }

        LoadItems = false;
        IsLoading = false;

        // set LoadItems to true in 10s
        System.Threading.Tasks.Task.Delay(10000).ContinueWith(_ => Interlocked.Exchange(ref LoadItems, true));
    }

    public override IListItem[] GetItems()
    {
        var items = new List<ListItem>();
        var lowerSearchString = SearchText.ToLower(CultureInfo.CurrentUICulture);

        if (LoadItems == true)
        {
            InitializeItemList().GetAwaiter().GetResult();
        }

        // filter items based on search text
        if (_settingsManager.UseStrichtSearch)
        {
            // strict search contains all characters in order of search value, with no random characters between
            items = _allItems.FindAll(x => x.Subtitle.ToLower(CultureInfo.CurrentUICulture).Contains(lowerSearchString, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // string search contains characters in order of search value, with optional random characters between    
            // e.g. "abc" matches "a1b2c3", "ab", "a b c", etc.
            items = _allItems.FindAll(item =>
            {
                int charIndex = 0;
                foreach (var character in lowerSearchString)
                {
                    charIndex = _lowerSearchTextCache[item].IndexOf(character, charIndex);
                    if (charIndex == -1)
                    {
                        return false;
                    }
                    else
                    {
                        charIndex++;
                    }
                }

                return true;
            });
        }


        if (lowerSearchString == "reload")
        {
            // add reload option at the top
            items.Insert(0, new ListItem(new ReloadVsCodeCommand(this))
            {
                Title = Resource.reload_items,
                Icon = IconHelpers.FromRelativePath("Assets\\ReloadIcon.png"),
            });
        }


        if (items.Count == 0)
        {
            return [
                new ListItem(new NoOpCommand()) {
                    Title = Resource.no_items_found,
                    Subtitle = Resource.no_items_found_subtitle,
                    Icon = IconHelpers.FromRelativePath("Assets\\VsCodeIcon.png")
                }
            ];
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

        return items.ToArray();
    }
}
