#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Duble.App.Commands;
using Duble.Core.Decisions;
using Duble.Core.Projects;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// The application without WPF: the real Core services, a session, a bridge with a fake window and fake
/// dialogs, and every command group registered exactly as MainWindow registers them. Tests drive it through
/// Call, which is what the interface does, so a command that is not wired up fails here too.
///
/// It owns a temporary folder and deletes it when the test is done, so `using var app = new TestApp(...)`
/// replaces the try/finally around every one of these tests.
/// </summary>
public sealed class TestApp : IDisposable
{
    public TestApp(string name = "app", Settings? settings = null)
    {
        Temp = TestPaths.Temp(name);
        Services = new ServiceCollection().AddDubleCore().BuildServiceProvider();
        Session = ActivatorUtilities.CreateInstance<Session>(Services);
        Settings = settings ?? new Settings();
        Bridge = new Bridge(Window, Dialogs, Settings, Sent.Add)
        {
            SettingsFile = Path.Combine(Temp, "settings.json"),
        };
        Jobs = new JobRunner(Bridge.Event);
        Groups = new LiveGroups(Session, Services.GetRequiredService<IResolutionService>());

        Modules = CommandModules.Create(Services, Bridge, Session, Jobs, Updates, Installer);
        foreach (var module in Modules) module.Register();
    }

    public string Temp { get; }
    public ServiceProvider Services { get; }
    public Session Session { get; }
    public Settings Settings { get; }
    public Bridge Bridge { get; }
    public JobRunner Jobs { get; }
    public LiveGroups Groups { get; }
    public FakeWindow Window { get; } = new();
    public FakeDialogs Dialogs { get; } = new();
    public FakeUpdateSource Updates { get; } = new();
    public FakeUpdateInstaller Installer { get; } = new();
    public IReadOnlyList<ICommandModule> Modules { get; }

    /// <summary>The one module of that type, for a test that drives it directly rather than through Call.</summary>
    public T Module<T>() where T : ICommandModule => Modules.OfType<T>().Single();

    /// <summary>Every message the bridge pushed to the interface, as raw JSON.</summary>
    public List<string> Sent { get; } = new();

    public Project NewProject(string name = "P")
    {
        Session.New(name, Path.Combine(Temp, "proj", name + ".duble"));
        return Session.Project!;
    }

    /// <summary>A command that is expected to work; returns its `result`.</summary>
    public async Task<JsonElement> Call(string command, string args = "null")
    {
        var response = await Respond(command, args);
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());

        var result = response.GetProperty("result");
        Contract.CheckFields(command, result);
        return result;
    }

    /// <summary>A command that is expected to fail; returns its `error` (code and message).</summary>
    public async Task<JsonElement> Failing(string command, string args = "null")
    {
        var response = await Respond(command, args);
        Assert.False(response.GetProperty("ok").GetBoolean(), response.ToString());
        return response.GetProperty("error");
    }

    public async Task<JsonElement> Respond(string command, string args = "null")
    {
        var request = $"{{\"id\":\"1\",\"cmd\":\"{command}\",\"args\":{args}}}";
        return JsonDocument.Parse(await Bridge.Handle(request)).RootElement;
    }

    public bool Saw(string name) => Sent.Any(message => message.Contains($"\"event\":\"{name}\""));

    /// <summary>The data of the last event of that name.</summary>
    public JsonElement EventData(string name)
    {
        var message = Sent.LastOrDefault(m => m.Contains($"\"event\":\"{name}\""))
            ?? throw new InvalidOperationException("no event " + name + " was sent");

        var data = JsonDocument.Parse(message).RootElement.GetProperty("data");
        Contract.CheckFields("event " + name, data);
        return data;
    }

    /// <summary>
    /// Waits for a background job to announce itself and then to finish. The budget is generous on purpose:
    /// on a slow or busy build agent the work takes many times longer than on a developer's machine, and a
    /// test that fails there for want of patience says nothing about the code.
    /// </summary>
    public async Task WaitFor(string name, int seconds = 60)
    {
        for (int waited = 0; waited < seconds * 20 && !Saw(name); waited++) await Task.Delay(50);
        Assert.True(Saw(name), $"the event {name} never arrived; sent: {string.Join(", ", Sent.Count > 20 ? Sent.Take(20) : Sent)}");
        for (int waited = 0; waited < seconds * 20 && Jobs.Busy; waited++) await Task.Delay(50);
        Assert.False(Jobs.Busy, "a job was still running after " + name);
    }

    /// <summary>Waits for whatever job is running to finish, when no event marks the end of it.</summary>
    public async Task WaitForIdle(int seconds = 60)
    {
        for (int waited = 0; waited < seconds * 20 && Jobs.Busy; waited++) await Task.Delay(50);
        Assert.False(Jobs.Busy, "a job was still running");
    }

    public void Dispose()
    {
        Jobs.Cancel();
        Services.Dispose();
        // the archives are opened read-only and may still be held; a temporary folder left behind is harmless
        try { Directory.Delete(Temp, true); }
        catch { }
    }
}
