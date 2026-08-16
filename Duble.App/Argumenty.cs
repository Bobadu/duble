// Argumenty.cs — przelaczniki wiersza polecen (tryb dev, zrzut ekranu, projekt na start).
//
//   Duble.exe [plik.duble] [--dev] [--ui-folder <folder>] [--project <plik.duble>] [--view <widok>]
//             [--lang pl|en] [--theme dark|light|system] [--screenshot <plik.png>] [--exec <js>]
using System;
using System.Collections.Generic;

namespace Duble.App;

public sealed class Argumenty
{
    public bool Dev { get; set; }
    public string UiFolder { get; set; }
    public string Projekt { get; set; }
    public string Widok { get; set; }
    public string Jezyk { get; set; }
    public string Motyw { get; set; }
    public string Zrzut { get; set; }
    public string Exec { get; set; }      // --exec <js>: JavaScript do wykonania po ui.ready (testy UI), przed zrzutem

    public static Argumenty Parsuj(string[] args)
    {
        var a = new Argumenty();
        var l = new List<string>(args ?? Array.Empty<string>());
        string Wartosc(string nazwa)
        {
            int i = l.IndexOf(nazwa);
            if (i < 0 || i + 1 >= l.Count) return null;
            var v = l[i + 1]; l.RemoveRange(i, 2); return v;
        }
        a.Dev = l.Remove("--dev");
        a.UiFolder = Wartosc("--ui-folder"); a.Projekt = Wartosc("--project"); a.Widok = Wartosc("--view");
        a.Jezyk = Wartosc("--lang"); a.Motyw = Wartosc("--theme"); a.Zrzut = Wartosc("--screenshot"); a.Exec = Wartosc("--exec");
        foreach (var reszta in l)
            if (reszta.EndsWith(".duble", StringComparison.OrdinalIgnoreCase)) a.Projekt ??= reszta;   // dwuklik na pliku projektu
        return a;
    }
}
