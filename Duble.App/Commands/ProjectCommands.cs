// Commands/ProjectCommands.cs — project.recent / new / open / get / save / close / pickOpen / pickFolder / forget.
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Duble.App.Commands;

public sealed class ProjectCommands : CommandModule
{
    /// <summary>What Windows will not have in a file name; a project may still be called anything.</summary>
    static readonly Regex NotInFileNames = new(@"[\\/:*?""<>|]+", RegexOptions.Compiled);

    public ProjectCommands(Bridge bridge, Session session) : base(bridge, session) { }

    public override void Register()
    {
        Bridge.Register("project.recent", _ => Recent());
        Bridge.Register("project.get", _ => new { project = Session.Summary() });
        Bridge.Register("project.new", Create);
        Bridge.Register("project.open", Open);
        Bridge.Register("project.pickOpen", _ => PickAndOpen());
        Bridge.Register("project.pickFolder", _ => new { path = Bridge.Dialogs.PickFolder(null, Settings.ProjectsFolder) });
        // saving with nothing open is a no_project answer, not a quiet success
        Bridge.Register("project.save", _ => { RequireProject(); Session.Save(); return new { }; });
        Bridge.Register("project.close", _ => { Session.Close(); Bridge.Event("project.closed", new { }); return new { }; });
        Bridge.Register("project.forget", Forget);
    }

    object Recent() => new
    {
        recent = Bridge.Settings.Recent.Select(entry => new
        {
            path = entry.Path,
            name = entry.Name,
            lastOpened = entry.LastOpened,
            exists = File.Exists(entry.Path),
        }).ToList(),
        defaultFolder = Settings.ProjectsFolder,
    };

    object Create(JsonElement args)
    {
        var name = args.Required("name").Trim();
        if (name.Length == 0) throw new BridgeException(BridgeErrors.BadArguments, "the name is empty");

        var folder = args.Text("folder");
        if (string.IsNullOrWhiteSpace(folder)) folder = Settings.ProjectsFolder;

        var fileName = Regex.Replace(NotInFileNames.Replace(name, " "), @"\s+", " ").Trim();
        if (fileName.Length == 0) fileName = "Project";
        var file = Path.Combine(folder, fileName + ".duble");
        if (File.Exists(file)) throw new BridgeException(BridgeErrors.Io, "a project file already exists: " + file);

        try
        {
            Directory.CreateDirectory(folder);
            Session.New(name, file);
        }
        catch (BridgeException) { throw; }
        catch (Exception e) { throw new BridgeException(BridgeErrors.Io, e.Message); }

        Opened();
        return new { project = Session.Summary() };
    }

    object Open(JsonElement args)
    {
        var file = args.Required("path");
        if (!File.Exists(file)) throw new BridgeException(BridgeErrors.NotFound, file);
        OpenFile(file);
        return new { project = Session.Summary() };
    }

    object PickAndOpen()
    {
        var files = Bridge.Dialogs.PickFiles(null, "duble", false, Settings.ProjectsFolder);
        if (files.Length == 0) return new { project = (object?)null };
        OpenFile(files[0]);
        return new { project = Session.Summary() };
    }

    object Forget(JsonElement args)
    {
        var file = args.Required("path");
        Bridge.Settings.Recent.RemoveAll(entry => string.Equals(entry.Path, file, StringComparison.OrdinalIgnoreCase));
        Bridge.SaveSettings();
        return new { };
    }

    void OpenFile(string file)
    {
        try { Session.Open(file); }
        catch (Exception e) { throw new BridgeException(BridgeErrors.Io, e.Message); }
        Opened();
    }

    /// <summary>A project was opened or created: remember it and let the interface switch to it.</summary>
    void Opened()
    {
        var project = Project;
        Bridge.Settings.Remember(project.Path ?? "", project.Name ?? "");
        Bridge.SaveSettings();
        Bridge.Event("project.opened", new { project = Session.Summary() });
    }
}
