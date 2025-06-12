using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CmdPalVsCode;

public partial class CmdPalVsCodeCommandsProvider : CommandProvider
{
    private readonly SettingsManager _settingsManager = new();
    public CmdPalVsCodeCommandsProvider()
    {
        DisplayName = "VS Code";
        Icon = IconHelpers.FromRelativePath("Assets\\VsCodeIcon.svg");

        Settings = _settingsManager.Settings;

        VSCodeHandler.LoadInstances(_settingsManager.PreferredEdition);
    }


    public override ICommandItem[] TopLevelCommands()
    {
        return [
            new CommandItem(new VSCodePage(_settingsManager)) {
                Title = DisplayName,
                MoreCommands = [
                    new CommandContextItem(Settings.SettingsPage),
                ],
            },
        ];
    }
}
