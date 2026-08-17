// Komendy/Widoki.cs — wspolna serializacja pozycji/czlonka grupy do JSON dla UI (Duplikaty, Katalog, karta pozycji).
//
// podst: id, zrodlo, typ/numer/sufiks, format, punkty, miniatura, liczniki; szczegoly: rozpiska jakosci, sciezki, lista tekstur.
// Bez grupy (Katalog) punkty i rozpiska licza sie z Porownanie.Jakosc(p).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Duble.App.Komendy;

public static class Widoki
{
    public static object Powod(Powod p) => p == null ? null : new { kod = p.Kod, p = p.P };
    public static object Rozstrz(Rozstrzygniecie r) => new { zwyciezca = r.Zwyciezca, odrzucone = r.Odrzucone, ignoruj = r.Ignoruj, domyslna = r.Domyslna, notatka = r.Notatka };
    public static object Punkt(Punktacja p) => p == null ? null : new { razem = p.Razem, rozdz = p.Rozdz, mipy = p.Mipy, warianty = p.Warianty, format = p.Format, lod = p.Lod, rozdzPx = p.RozdzPx, udzialMipow = p.UdzialMipow, liczbaWariantow = p.LiczbaWariantow, zlyFormat = p.ZlyFormat, lody = p.Lody, brakTekstur = p.BrakTekstur };

    public static bool WArchiwum(Garment p) => p.ModelPath != null && p.ModelPath.Contains('|');
    public static string Miniatura(Garment p) => p.Textures.FirstOrDefault(t => t.IsDecoded && t.Sha256 != null)?.Sha256;

    /// <summary>Czlonek grupy (g != null: punkty/rozpiska z grupy) albo samodzielna pozycja (g == null: z Jakosc).</summary>
    public static Dictionary<string, object> Czlonek(Garment p, Grupa g, bool szczegoly, Func<Garment, string> zrodlo)
    {
        double punkty; Punktacja rozpiska = null;
        if (g != null) { punkty = g.Punkty.TryGetValue(p.Id, out var pkt) ? pkt : 0.0; if (szczegoly) rozpiska = g.Rozpiska.TryGetValue(p.Id, out var r) ? r : null; }
        else { var j = Porownanie.Jakosc(p); punkty = j.Razem; rozpiska = j; }
        var podst = new Dictionary<string, object>
        {
            ["id"] = p.Id, ["zrodloId"] = p.SourceId, ["zrodlo"] = zrodlo(p), ["kontener"] = p.Container, ["typ"] = p.Slot, ["numer"] = p.Number, ["sufiks"] = p.Suffix,
            ["gen9"] = p.GameFormat == GameFormat.Enhanced, ["props"] = p.IsProp, ["punkty"] = punkty, ["thumb"] = Miniatura(p),
            ["tekstur"] = p.Textures.Count, ["wierzcholki"] = p.Geometry?.Vertices ?? 0, ["trojkaty"] = p.Geometry?.Triangles ?? 0, ["lody"] = p.Geometry?.LodLevels ?? 0,
            ["bajty"] = p.ModelSize + p.Textures.Sum(t => t.Size), ["wArchiwum"] = WArchiwum(p),
        };
        if (szczegoly)
        {
            podst["rozpiska"] = Punkt(rozpiska);
            podst["sciezkaYdd"] = p.ModelPath;
            podst["bajtyYdd"] = p.ModelSize;
            podst["tekstury"] = p.Textures.Select(t => new { sha = t.Sha256, plik = t.FileName, nazwa = t.Name, w = t.Width, h = t.Height, format = t.Format, mipy = t.MipLevels, alfa = t.AlphaShare, zdekodowana = t.IsDecoded, bajty = t.Size }).ToList();
        }
        return podst;
    }
}
