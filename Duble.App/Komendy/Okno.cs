// Komendy/Okno.cs — okno, powloka Windows, dialogi systemowe, ustawienia programu, info o aplikacji.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Duble.App.Komendy;

public static class Okno
{
    /// <summary>UI zglosilo gotowosc (po zaladowaniu i18n i pierwszym renderze) — MainWindow podpina zrzut ekranu / otwarcie projektu z argumentow.</summary>
    public static event Action UiGotowe;

    public static void Zarejestruj(Mostek m)
    {
        m.Rejestruj("app.info", _ => new { nazwa = "Duble", by = "Bobadu", wersja = Wersja(), dev = m.Dev });
        m.Rejestruj("ui.ready", _ => { UiGotowe?.Invoke(); return new { }; });
        m.Rejestruj("window.minimize", _ => { m.Okno.Uruchom(m.Okno.Minimalizuj); return new { }; });
        m.Rejestruj("window.maximize", _ => { m.Okno.Uruchom(m.Okno.MaksymalizujAlboPrzywroc); return new { maks = m.Okno.Zmaksymalizowane }; });
        m.Rejestruj("window.close", _ => { m.Okno.Uruchom(m.Okno.Zamknij); return new { }; });
        m.Rejestruj("window.state", _ => new { maks = m.Okno.Zmaksymalizowane });
        m.Rejestruj("window.dragStart", _ => { m.Okno.Uruchom(m.Okno.RozpocznijPrzeciaganie); return new { }; });
        m.Rejestruj("settings.get", _ => new { jezyk = m.Ustawienia.JezykEfektywny, jezykUstawiony = m.Ustawienia.Jezyk, motyw = m.Ustawienia.Motyw, ostatnie = m.Ustawienia.Ostatnie });
        m.Rejestruj("settings.set", a =>
        {
            var j = Mostek.Tekst(a, "jezyk"); var t = Mostek.Tekst(a, "motyw");
            if (j != null) m.Ustawienia.Jezyk = j == "" || j == "system" ? null : j;
            if (t != null) m.Ustawienia.Motyw = t;
            try { m.Ustawienia.Zapisz(m.PlikUstawien); } catch (Exception e) { throw new BladMostka("io", e.Message); }
            return new { jezyk = m.Ustawienia.JezykEfektywny, jezykUstawiony = m.Ustawienia.Jezyk, motyw = m.Ustawienia.Motyw };
        });
        m.Rejestruj("shell.openFolder", a =>
        {
            var s = Mostek.Tekst(a, "sciezka", true);
            if (!Directory.Exists(s) && !File.Exists(s)) throw new BladMostka("not_found", s);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{s}\"") { UseShellExecute = true });
            return new { };
        });
        m.Rejestruj("shell.showInExplorer", a =>
        {
            var s = Mostek.Tekst(a, "sciezka", true);
            if (!File.Exists(s) && !Directory.Exists(s)) throw new BladMostka("not_found", s);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{s}\"") { UseShellExecute = true });
            return new { };
        });
        m.Rejestruj("shell.openUrl", a =>
        {
            var u = Mostek.Tekst(a, "url", true);
            if (!u.StartsWith("http://") && !u.StartsWith("https://")) throw new BladMostka("bad_args", "tylko http(s)");
            Process.Start(new ProcessStartInfo(u) { UseShellExecute = true });
            return new { };
        });
        m.Rejestruj("dialogs.pickFolder", a => new { sciezka = m.Dialogi.WybierzFolder(Mostek.Tekst(a, "tytul"), Mostek.Tekst(a, "start")) });
        m.Rejestruj("dialogs.pickFiles", a => new { sciezki = m.Dialogi.WybierzPliki(Mostek.Tekst(a, "tytul"), Mostek.Tekst(a, "filtr"), Mostek.Flaga(a, "wiele", true), Mostek.Tekst(a, "start")) ?? Array.Empty<string>() });
        m.Rejestruj("dialogs.saveFile", a => new { sciezka = m.Dialogi.ZapiszPlik(Mostek.Tekst(a, "tytul"), Mostek.Tekst(a, "filtr"), Mostek.Tekst(a, "nazwa"), Mostek.Tekst(a, "start")) });
    }

    public static string Wersja()
    {
        var asm = Assembly.GetExecutingAssembly();
        var inf = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(inf)) return inf.Split('+')[0];
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
