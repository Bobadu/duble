// Commands/WindowCommands.cs — the window (its title bar is drawn by the interface, so the buttons come back
// here), the Windows shell, and the system file dialogs.
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Duble.App.Commands;

public sealed class WindowCommands : ICommandModule
{
    readonly Bridge bridge;

    public WindowCommands(Bridge bridge) => this.bridge = bridge;

    public void Register()
    {
        bridge.Register("window.minimize", _ => OnUiThread(bridge.Window.Minimize));
        bridge.Register("window.maximize", _ => { bridge.Window.Invoke(bridge.Window.MaximizeOrRestore); return State(); });
        bridge.Register("window.close", _ => OnUiThread(bridge.Window.Close));
        bridge.Register("window.state", _ => State());
        bridge.Register("window.dragStart", _ => OnUiThread(bridge.Window.StartDrag));

        bridge.Register("shell.openFolder", OpenFolder);
        bridge.Register("shell.showInExplorer", ShowInExplorer);
        bridge.Register("shell.openUrl", OpenUrl);

        bridge.Register("dialogs.pickFolder", args => new
        {
            path = bridge.Dialogs.PickFolder(args.Text("title"), args.Text("start")),
        });
        bridge.Register("dialogs.pickFiles", args => new
        {
            paths = bridge.Dialogs.PickFiles(args.Text("title"), args.Text("filter"), args.Flag("multiple", true), args.Text("start")),
        });
        bridge.Register("dialogs.saveFile", args => new
        {
            path = bridge.Dialogs.SaveFile(args.Text("title"), args.Text("filter"), args.Text("name"), args.Text("start")),
        });
    }

    object State() => new { maximized = bridge.Window.IsMaximized };

    object OnUiThread(Action action)
    {
        bridge.Window.Invoke(action);
        return new { };
    }

    object OpenFolder(JsonElement args) => Start(Existing(args), path => new ProcessStartInfo("explorer.exe", $"\"{path}\""));

    object ShowInExplorer(JsonElement args) => Start(Existing(args), path => new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));

    object OpenUrl(JsonElement args)
    {
        var url = args.Required("url");
        // Explorer would happily start anything at all, so only the two schemes the interface links with
        if (!url.StartsWith("http://", StringComparison.Ordinal) && !url.StartsWith("https://", StringComparison.Ordinal))
            throw new BridgeException(BridgeErrors.BadArguments, "only http and https");
        return Start(url, address => new ProcessStartInfo(address));
    }

    static string Existing(JsonElement args)
    {
        var path = args.Required("path");
        if (!Directory.Exists(path) && !File.Exists(path)) throw new BridgeException(BridgeErrors.NotFound, path);
        return path;
    }

    static object Start(string argument, Func<string, ProcessStartInfo> start)
    {
        var info = start(argument);
        info.UseShellExecute = true;
        Process.Start(info);
        return new { };
    }
}
