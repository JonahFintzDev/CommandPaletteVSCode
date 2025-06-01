using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CmdPalVsCode;

/// <summary>
/// Command to open a Visual Studio Code workspace.
/// </summary>
internal sealed partial class ReloadVsCodeCommand : InvokableCommand
{
    private VSCodePage page;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReloadVsCodeCommand"/> class.
    /// </summary>
    /// <param name="page">The VS Code page instance.</param>
    public ReloadVsCodeCommand(VSCodePage page)
    {
        this.page = page;
    }

    /// <summary>
    /// Invokes the command to reload the VS Code workspaces.
    /// </summary>
    /// <returns>The result of the command execution.</returns>
    public override CommandResult Invoke()
    {

        // Reload the workspaces in the VS Code page
        page.InitializeItemList().Wait();

        page.SearchText = "";

        return CommandResult.ShowToast(new ToastArgs()
        {
            Message = "Reloaded VS Code workspaces.",
            Result = CommandResult.KeepOpen()
        });
    }
}
