// Komendy/Widoki.cs — wspolna serializacja pozycji/czlonka grupy do JSON dla UI (Duplikaty, Katalog, karta pozycji).
//
// podst: id, zrodlo, typ/numer/sufiks, format, punkty, miniatura, liczniki; szczegoly: rozpiska jakosci, sciezki, lista tekstur.
// Bez grupy (Katalog) punkty i rozpiska licza sie z Porownanie.Jakosc(p).
using System;
using System.Collections.Generic;
using System.Linq;
using Duble;

namespace Duble.App.Komendy;

public static class Widoki
{
    public static object Powod(Powod p) => p == null ? null : new { kod = p.Kod, p = p.P };
    public static object Rozstrz(Rozstrzygniecie r) => new { zwyciezca = r.Zwyciezca, odrzucone = r.Odrzucone, ignoruj = r.Ignoruj, domyslna = r.Domyslna, notatka = r.Notatka };
    public static object Punkt(Punktacja p) => p == null ? null : new { razem = p.Razem, rozdz = p.Rozdz, mipy = p.Mipy, warianty = p.Warianty, format = p.Format, lod = p.Lod, rozdzPx = p.RozdzPx, udzialMipow = p.UdzialMipow, liczbaWariantow = p.LiczbaWariantow, zlyFormat = p.ZlyFormat, lody = p.Lody, brakTekstur = p.BrakTekstur };

    public static bool WArchiwum(Pozycja p) => p.SciezkaYdd != null && p.SciezkaYdd.Contains('|');
    public static string Miniatura(Pozycja p) => p.Tekstury.FirstOrDefault(t => t.Zdekodowana && t.Sha != null)?.Sha;

    /// <summary>Czlonek grupy (g != null: punkty/rozpiska z grupy) albo samodzielna pozycja (g == null: z Jakosc).</summary>
    public static Dictionary<string, object> Czlonek(Pozycja p, Grupa g, bool szczegoly, Func<Pozycja, string> zrodlo)
    {
        double punkty; Punktacja rozpiska = null;
        if (g != null) { punkty = g.Punkty.TryGetValue(p.Id, out var pkt) ? pkt : 0.0; if (szczegoly) rozpiska = g.Rozpiska.TryGetValue(p.Id, out var r) ? r : null; }
        else { var j = Porownanie.Jakosc(p); punkty = j.Razem; rozpiska = j; }
        var podst = new Dictionary<string, object>
        {
            ["id"] = p.Id, ["zrodloId"] = p.ZrodloId, ["zrodlo"] = zrodlo(p), ["kontener"] = p.Kontener, ["typ"] = p.Typ, ["numer"] = p.Numer, ["sufiks"] = p.Sufiks,
            ["gen9"] = p.Gen9, ["props"] = p.Props, ["punkty"] = punkty, ["thumb"] = Miniatura(p),
            ["tekstur"] = p.Tekstury.Count, ["wierzcholki"] = p.Geo?.Wierzcholki ?? 0, ["trojkaty"] = p.Geo?.Trojkaty ?? 0, ["lody"] = p.Geo?.Lody ?? 0,
            ["bajty"] = p.BajtyYdd + p.Tekstury.Sum(t => t.Bajty), ["wArchiwum"] = WArchiwum(p),
        };
        if (szczegoly)
        {
            podst["rozpiska"] = Punkt(rozpiska);
            podst["sciezkaYdd"] = p.SciezkaYdd;
            podst["bajtyYdd"] = p.BajtyYdd;
            podst["tekstury"] = p.Tekstury.Select(t => new { sha = t.Sha, plik = t.Plik, nazwa = t.Nazwa, w = t.W, h = t.H, format = t.Format, mipy = t.Mipy, alfa = t.Alfa, zdekodowana = t.Zdekodowana, bajty = t.Bajty }).ToList();
        }
        return podst;
    }
}
