// Bridge.cs — the channel between the interface (React/TypeScript) and C#: a router for "group.action"
// commands and the events that travel the other way.
//
// The wire contract, which BridgeTests pins down:
//   request   {id, cmd, args}       response  {id, ok:true, result} | {id, ok:false, error:{code, message}}
//   event     {event, data}         (no id; C# -> interface)
//
// A handler may throw BridgeException to choose the error code; anything else becomes "internal".
//
// The field names inside result and data are the interface's vocabulary, written out in one place on each
// side: here in Duble.App/Commands, and in web/src/bridge/contract.ts. Every payload names its fields
// explicitly (`name = source.Name`) instead of using the shorthand `new { source.Name }`, which would rename
// the field along with the property. That shorthand is exactly how the project name once vanished from the
// start screen.
using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Duble.App;

/// <summary>The error codes the interface knows. It shows its own message for these and falls back to the text.</summary>
public static class BridgeErrors
{
    public const string UnknownCommand = "unknown_command";
    public const string BadArguments = "bad_args";
    public const string NoProject = "no_project";
    public const string Busy = "busy";
    public const string NotFound = "not_found";
    public const string Io = "io";
    public const string Cancelled = "cancelled";
    public const string Internal = "internal";
}

/// <summary>A failure the interface is meant to understand: the code reaches it, not just the message.</summary>
public sealed class BridgeException : Exception
{
    public BridgeException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}

public sealed class Bridge
{
    /// <summary>How everything crossing the bridge is serialised; the tests compare against exactly this.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    readonly Dictionary<string, Func<JsonElement, Task<object>>> handlers = new(StringComparer.OrdinalIgnoreCase);
    readonly Action<string> send;

    public Bridge(IHostWindow window, IFileDialogs dialogs, Settings settings, Action<string> send)
    {
        Window = window;
        Dialogs = dialogs;
        Settings = settings;
        this.send = send;
    }

    public IHostWindow Window { get; }
    public IFileDialogs Dialogs { get; }
    public Settings Settings { get; }

    /// <summary>Developer mode, which the interface shows in the About screen.</summary>
    public bool Dev { get; init; }

    /// <summary>Where to write the program settings; null means the usual %AppData% file. Tests and the
    /// screenshot runs point this at a temporary file so they leave the user's own settings alone.</summary>
    public string? SettingsFile { get; init; }

    public void Register(string command, Func<JsonElement, Task<object>> handler) => handlers[command] = handler;

    public void Register(string command, Func<JsonElement, object> handler) => handlers[command] = args => Task.FromResult(handler(args));

    /// <summary>The commands registered so far. ContractTests compares them with the interface's contract.</summary>
    public IEnumerable<string> Commands => handlers.Keys;

    /// <summary>Runs one request and returns the response to post back. Never throws.</summary>
    public async Task<string> Handle(string requestJson)
    {
        string? id = null;
        try
        {
            using var document = JsonDocument.Parse(requestJson);
            var root = document.RootElement;
            id = root.TryGetProperty("id", out var requestId) ? requestId.ToString() : null;
            var command = root.TryGetProperty("cmd", out var cmd) ? cmd.GetString() : null;
            var args = root.TryGetProperty("args", out var a) ? a.Clone() : default;

            if (command == null || !handlers.TryGetValue(command, out var handler))
                return Failure(id, BridgeErrors.UnknownCommand, "unknown command: " + command);

            var result = await handler(args).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { id, ok = true, result }, Json);
        }
        catch (BridgeException e) { return Failure(id, e.Code, e.Message); }
        catch (OperationCanceledException) { return Failure(id, BridgeErrors.Cancelled, "cancelled"); }
        catch (Exception e) { return Failure(id, BridgeErrors.Internal, e.GetType().Name + ": " + e.Message); }
    }

    /// <summary>Pushes an event to the interface. Events carry no id: nothing is waiting for an answer.</summary>
    public void Event(string name, object data) => send(JsonSerializer.Serialize(new { @event = name, data }, Json));

    /// <summary>Saves the program settings, swallowing a failure: none of these are worth a failed command.</summary>
    public void SaveSettings()
    {
        try { Settings.Save(SettingsFile); }
        catch { /* read-only profile, roaming folder gone: the setting still holds for this run */ }
    }

    static string Failure(string? id, string code, string message)
        => JsonSerializer.Serialize(new { id, ok = false, error = new { code, message } }, Json);
}
