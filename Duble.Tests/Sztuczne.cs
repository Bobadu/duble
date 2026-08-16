using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duble;

namespace Duble.Tests;

/// <summary>Sztuczne pozycje z plikami-atrapami na dysku (bez plikow gry) do testow komend, raportu i zastosowania.
/// Siedem(): 3 grupy — DUPLIKAT a=b (jbib 1/7), PRZEMALOWANIE c=d (lowr 3/9), DUPLIKAT e=f=g (feet 5/6/8).</summary>
public static class Sztuczne
{
    public static float[] Hist(int szczyt) { var h = new float[Geo.Kubelki]; h[szczyt] = 0.7f; h[Math.Min(Geo.Kubelki - 1, szczyt + 1)] = 0.3f; return h; }

    /// <summary>Pozycja w &lt;tmp&gt;\&lt;paczka&gt;\k.rpf\ z plikami .ydd (100 B) i .ytd (50 B); ZrodloId = zrodloId.</summary>
    public static Pozycja Poz(string tmp, string paczka, string typ, int numer, string hashPoz, float[] hist, string zrodloId, params string[] shaTekstur)
    {
        var folder = Path.Combine(tmp, paczka, "k.rpf"); Directory.CreateDirectory(folder);
        var ydd = Path.Combine(folder, $"{typ}_{numer:d3}_u.ydd"); File.WriteAllBytes(ydd, new byte[100]);
        var p = new Pozycja
        {
            Id = $"{paczka}|k.rpf|{typ}|{numer}|u", Paczka = paczka, Kontener = "k.rpf", Typ = typ, Numer = numer, Sufiks = "u", ZrodloId = zrodloId,
            SciezkaYdd = ydd, BajtyYdd = 100,
            Geo = new Geo { HashPozycji = hashPoz, Trojkaty = 1000, Wierzcholki = 600, Hist = hist, Bbox = new[] { 0.5f, 0.3f, 0.6f }, Lody = 3 },
        };
        char litera = 'a';
        foreach (var sha in shaTekstur)
        {
            var plik = Path.Combine(folder, $"{typ}_diff_{numer:d3}_{litera++}_uni.ytd"); File.WriteAllBytes(plik, new byte[50]);
            p.Tekstury.Add(new Tekstura { Plik = Path.GetFileName(plik), Sciezka = plik, Sha = sha + paczka + numer, Bajty = 50, W = 1024, H = 1024, Mipy = 11, Format = "BC3", Zdekodowana = true, Wariancja = 30, PHash = new ulong[] { 1, 2, 3, 4 }, Kolor = Convert.ToBase64String(new byte[192]) });
        }
        return p;
    }

    /// <summary>Projekt sesji z trzema zrodlami-folderami p1/p2/p3 (&lt;tmp&gt;\p1 …; Id "z-p1"…) i siedmioma pozycjami o pasujacych ZrodloId —
    /// tak jak w aplikacji (Paczka == nazwa zrodla), zeby ponowne indeksowanie po Zastosuj podmienialo wlasciwe pozycje.</summary>
    public static List<Pozycja> SiedemZeZrodlami(Duble.App.Sesja s, string tmp)
    {
        var poz = Siedem(tmp);
        foreach (var paczka in new[] { "p1", "p2", "p3" })
        {
            Directory.CreateDirectory(Path.Combine(tmp, paczka));
            s.Projekt.Zrodla.Add(new ZrodloProjektu { Id = "z-" + paczka, Nazwa = paczka, Sciezka = Path.Combine(tmp, paczka), Typ = "folder", Wlaczone = true });
        }
        foreach (var p in poz) p.ZrodloId = "z-" + p.Paczka;
        s.ZmienKatalog(k => k.Wstaw(poz));
        return poz;
    }

    public static List<Pozycja> Siedem(string tmp, string zrodloId = "z1")
    {
        var a = Poz(tmp, "p1", "jbib", 1, "H1", Hist(10), zrodloId, "S1", "S2");
        var b = Poz(tmp, "p2", "jbib", 7, "H1", Hist(10), zrodloId, "S1", "S2");
        b.Tekstury.ForEach(t => t.Mipy = 1);
        var c = Poz(tmp, "p1", "lowr", 3, "H3", Hist(20), zrodloId, "T1");
        var d = Poz(tmp, "p2", "lowr", 9, "H3", Hist(20), zrodloId, "U1");
        d.Tekstury.ForEach(t => { t.PHash = new ulong[] { ulong.MaxValue, 0, ulong.MaxValue, 0 }; t.Kolor = Convert.ToBase64String(Enumerable.Repeat((byte)200, 192).ToArray()); });
        var e = Poz(tmp, "p1", "feet", 5, "H5", Hist(30), zrodloId, "V1");
        var f = Poz(tmp, "p2", "feet", 6, "H5", Hist(30), zrodloId, "V1"); f.Tekstury.ForEach(t => t.Mipy = 1);
        var g = Poz(tmp, "p3", "feet", 8, "H5", Hist(30), zrodloId, "V1"); g.Tekstury.ForEach(t => t.Mipy = 1);
        // te same grafiki: SHA rowne miedzy paczkami (Sha = sha+paczka+numer — ujednolic dla par a/b, e/f/g)
        foreach (var t in b.Tekstury) t.Sha = a.Tekstury[b.Tekstury.IndexOf(t)].Sha;
        foreach (var t in f.Tekstury) t.Sha = e.Tekstury[0].Sha; foreach (var t in g.Tekstury) t.Sha = e.Tekstury[0].Sha;
        return new List<Pozycja> { a, b, c, d, e, f, g };
    }
}
