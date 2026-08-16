// Zrodla.cs — ponowne siegniecie po plik, ktory byl indeksowany.
//
// Katalog trzyma dla kazdej tekstury `Sciezka`. Dla luznych plikow to zwykla sciezka,
// dla wpisu w archiwum: "sciezka\do\archiwum.rpf|sciezka\wewnetrzna". Raport musi umiec
// wydobyc te same bajty drugi raz, zeby zrobic miniature — takze wtedy, gdy zrodlem
// byla paczka .rpf prosto z internetu, a nie rozpakowany folder.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;

namespace Duble;

public static class Zrodla
{
    static readonly Dictionary<string, RpfFile> Otwarte = new(StringComparer.OrdinalIgnoreCase);

    public static byte[] Bajty(string sciezka)
    {
        if (string.IsNullOrEmpty(sciezka)) return null;
        int kreska = sciezka.IndexOf('|');
        if (kreska < 0)
            return File.Exists(sciezka) ? File.ReadAllBytes(sciezka) : null;

        var archiwum = sciezka.Substring(0, kreska);
        var wewnatrz = sciezka.Substring(kreska + 1);
        if (!Otwarte.TryGetValue(archiwum, out var rpf))
        {
            if (!File.Exists(archiwum)) return null;
            rpf = new RpfFile(archiwum, Path.GetFileName(archiwum));
            rpf.ScanStructure(s => { }, m => { });
            Otwarte[archiwum] = rpf;
        }
        var wpis = Wszystkie(rpf).FirstOrDefault(e =>
            string.Equals(e.Path, wewnatrz, StringComparison.OrdinalIgnoreCase));
        if (wpis == null) return null;
        // ExtractFile oddaje zasob bez naglowka RSC7 — doklejamy go, zeby LoadResourceFile czytal jak plik z dysku
        try { return Rsc7.Owin(wpis, wpis.File.ExtractFile(wpis)); } catch { return null; }
    }

    static IEnumerable<RpfFileEntry> Wszystkie(RpfFile f)
    {
        if (f.AllEntries != null)
            foreach (var e in f.AllEntries.OfType<RpfFileEntry>()) yield return e;
        if (f.Children != null)
            foreach (var c in f.Children)
                foreach (var e in Wszystkie(c)) yield return e;
    }
}
