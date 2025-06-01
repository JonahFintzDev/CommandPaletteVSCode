using Microsoft.CommandPalette.Extensions.Toolkit;
using System;

namespace CmdPalVsCode;

internal sealed class CopyPathCommand : InvokableCommand
{
    private readonly string _path;

    public override string Name => "Copy Path";

    public CopyPathCommand(string path)
    {
        _path = path;
    }

    public override CommandResult Invoke()
    {
        try
        {
            // Unescape the URI and copy to clipboard
            var unescapedPath = Uri.UnescapeDataString(_path);
            ClipboardHelper.SetText(unescapedPath);

            return CommandResult.ShowToast(new ToastArgs()
            {
                Message = $"Copied path: {unescapedPath}",
                Result = CommandResult.KeepOpen()
            });
        }
        catch
        {
            return CommandResult.ShowToast(new ToastArgs()
            {
                Message = "Failed to copy path.",
                Result = CommandResult.KeepOpen()
            });
        }
    }
}