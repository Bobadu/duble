// App.xaml.cs — start aplikacji: argumenty, ustawienia, okno glowne.
using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Duble.App;

public partial class App : Application
{
    /// <summary>Everything Duble.Core offers, built once at start-up.</summary>
    public static IServiceProvider Services { get; private set; }

    public static Argumenty Argumenty { get; private set; }
    public static Ustawienia Ustawienia { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Argumenty = Argumenty.Parsuj(e.Args);
        if (!string.IsNullOrEmpty(Argumenty.DevIcon)) { Komendy.Ikona.Zapisz(Argumenty.DevIcon); Shutdown(0); return; }
        Ustawienia = Ustawienia.Wczytaj();
        Services = new ServiceCollection().AddDubleCore().BuildServiceProvider();
        // --lang/--theme NIE nadpisuja ustawien uzytkownika (tryb kontrolny) — ida do UI jako parametry adresu (MainWindow)
        DispatcherUnhandledException += (s, ex) =>
        {
            MessageBox.Show(ex.Exception.ToString(), "Duble — blad", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        var okno = new MainWindow();
        MainWindow = okno;
        okno.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // przy --screenshot nie nadpisujemy ustawien (tryb kontrolny nie ma zmieniac srodowiska uzytkownika)
        try { if (string.IsNullOrEmpty(Argumenty?.Zrzut)) Ustawienia?.Zapisz(); } catch { }
        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
