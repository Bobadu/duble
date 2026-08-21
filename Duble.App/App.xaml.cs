// App.xaml.cs — starting up: the command line, the settings, the services, the window.
using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Duble.App;

public partial class App : Application
{
    /// <summary>Everything Duble.Core offers, built once at start-up.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public static StartupOptions Options { get; private set; } = null!;

    public static Settings Settings { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Options = StartupOptions.Parse(e.Args);

        if (!string.IsNullOrEmpty(Options.DevIconFile))
        {
            IconGenerator.Write(Options.DevIconFile);
            Shutdown(0);
            return;
        }

        Settings = Settings.Load();
        Services = new ServiceCollection().AddDubleCore().AddSingleton<Session>().BuildServiceProvider();

        // --lang and --theme do NOT overwrite the saved settings: they are for screenshot runs, and go to the
        // interface as query parameters instead (see MainWindow).
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "Duble — error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // a screenshot run must leave the user's environment exactly as it found it
        try
        {
            if (string.IsNullOrEmpty(Options?.ScreenshotFile)) Settings?.Save();
        }
        catch { /* a settings file that cannot be written is not worth failing the exit over */ }

        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
