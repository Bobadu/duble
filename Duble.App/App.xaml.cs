// App.xaml.cs — start aplikacji: argumenty, ustawienia, okno glowne.
using System;
using System.Windows;

namespace Duble.App;

public partial class App : Application
{
    public static Argumenty Argumenty { get; private set; }
    public static Ustawienia Ustawienia { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Argumenty = Argumenty.Parsuj(e.Args);
        if (!string.IsNullOrEmpty(Argumenty.DevIcon)) { Komendy.Ikona.Zapisz(Argumenty.DevIcon); Shutdown(0); return; }
        Ustawienia = Ustawienia.Wczytaj();
        if (!string.IsNullOrEmpty(Argumenty.Jezyk)) Ustawienia.Jezyk = Argumenty.Jezyk;
        if (!string.IsNullOrEmpty(Argumenty.Motyw)) Ustawienia.Motyw = Argumenty.Motyw;
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
        base.OnExit(e);
    }
}
