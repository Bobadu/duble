// App.xaml.cs — starting up: Velopack's hooks, the command line, the settings, the services, the window.
using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Velopack;

namespace Duble.App;

public partial class App : Application
{
    /// <summary>
    /// The entry point is written out — WPF would otherwise generate one — because Velopack has to run first:
    /// during an install or an update, Setup.exe starts this executable with special arguments to make the
    /// shortcuts and the like, and Run() answers those and exits before any window exists. In the portable
    /// exe and in development it does nothing at all.
    /// </summary>
    [STAThread]
    static void Main()
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

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
