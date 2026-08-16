// Porownanie.cs — z odciskow robi werdykty.
//
// ==================== PROGI: SKAD SIE WZIELY ====================
// Kalibracja na 1132 pozycjach i 9437 teksturach (15.08.2026, `duble kalibruj`):
//
//   TEKSTURY, odleglosc Hamminga na 256 bitach
//     pliki identyczne co do bajtu ....... 0        (n=443)
//     warianty koloru tego samego ciucha .. mediana 26, p05 = 2
//     pary losowe ........................ p01 = 92, mediana 128
//   Przy progu 24 na 400 000 losowych par zostaje 29 — i po obejrzeniu WIEKSZOSC
//   z nich to prawdziwe duplikaty (ta sama grafika pod inna nazwa). Prog 20 jest
//   wiec 4,6x ponizej pierwszego percentyla par losowych: bezpieczny z duzym zapasem.
//
//   KOLOR, srednia roznica na kanal (0..255), siatka 8x8
//     pliki identyczne .................... 0
//     warianty koloru ..................... mediana 13,7  (ale p01 = 0,08!)
//     pary losowe ......................... p01 = 3,05
//   Sam kolor NIE wystarcza (dwa czarne ciuchy sa blisko), sam PHash tez NIE
//   (warianty koloru maja p05 = 2). Rozstrzyga KONIUNKCJA obu.
//
//   GEOMETRIA, odleglosc L1 histogramow ksztaltu
//     ten sam mesh ........................ 0
//     najblizszy obcy mesh ................ p05 = 0,112, mediana 0,254
//   ALE: histogram sam w sobie MYLI — `hand_000` (3560 trojkatow) i `hand_025`
//   (2480 trojkatow) maja odleglosc 0,007, bo kazda rekawiczka ma podobny obrys.
//   Dlatego "identyczna geometria" wymaga DODATKOWO rownej liczby trojkatow
//   I wierzcholkow. Bez tego warunku deduplikacja skasowalaby rozne rekawiczki.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Duble;

public static class Progi
{
    public const double GeoIdentyczna = 0.02;   // + rowna liczba trojkatow i wierzcholkow
    public const double GeoPodobna = 0.10;
    public const double GeoPodobnaTri = 0.05;   // dopuszczalna wzgledna roznica trojkatow
    public const double GeoPodobnaBbox = 0.15;

    public const int TexPHash = 20;             // na 256 bitow
    public const double TexKolor = 3.0;         // srednia roznica na kanal
    public const float TexWariancjaMin = 3.0f;  // ponizej tego tekstura jest plaska i PHash to szum
    public const double TexKolorPlaska = 1.0;   // dla plaskich tekstur decyduje sam kolor, ostrzej

    public const double PelnePokrycie = 0.95;
    public const double CzesciowePokrycie = 0.5;
}

public class Para
{
    public string A { get; set; }
    public string B { get; set; }
    public string Werdykt { get; set; }
    public string Powod { get; set; }
    public double DistGeo { get; set; }
    public double PokrycieA { get; set; }
    public double PokrycieB { get; set; }
    public int WspolnychTekstur { get; set; }
}

public class Grupa
{
    public List<string> Pozycje { get; set; } = new();
    public string Werdykt { get; set; }
    public string Zwyciezca { get; set; }
    public string Powod { get; set; }
    public List<Para> Pary { get; set; } = new();
    public Dictionary<string, double> Punkty { get; set; } = new();
    public Dictionary<string, string> Rozpiska { get; set; } = new();
}

public class WynikPorownania
{
    public string Zbudowany { get; set; }
    public List<Grupa> Grupy { get; set; } = new();
    public List<string> Podsumowanie { get; set; } = new();

    static readonly JsonSerializerOptions Opcje = new() { WriteIndented = false };

    public static WynikPorownania Wczytaj(string s)
        => File.Exists(s) ? JsonSerializer.Deserialize<WynikPorownania>(File.ReadAllText(s), Opcje) ?? new() : new();

    public void Zapisz(string s)
    {
        var k = Path.GetDirectoryName(Path.GetFullPath(s));
        if (!string.IsNullOrEmpty(k)) Directory.CreateDirectory(k);
        File.WriteAllText(s, JsonSerializer.Serialize(this, Opcje));
    }
}

public static class Porownanie
{
    // nazwy werdyktow (uzywane tez w raporcie)
    public const string Duplikat = "DUPLIKAT";
    public const string Nadzbior = "DUPLIKAT-NADZBIOR";
    public const string DoWgladu = "DO WGLADU";
    public const string Przemalowanie = "PRZEMALOWANIE";

    public static WynikPorownania Znajdz(Katalog katalog, Action<string> log)
    {
        var poz = katalog.Pozycje.Where(p => p.Geo?.Hist != null && p.Geo.Wierzcholki > 0).ToList();
        log($"pozycji do porownania: {poz.Count}");

        var pary = new List<Para>();
        int kandydatow = 0;
        for (int i = 0; i < poz.Count; i++)
        {
            for (int j = i + 1; j < poz.Count; j++)
            {
                var a = poz[i]; var b = poz[j];
                var g = WerdyktGeometrii(a, b, out double dist);
                if (g == null) continue;
                kandydatow++;
                var para = Oceń(a, b, g, dist);
                if (para != null) pary.Add(para);
            }
            if ((i + 1) % 200 == 0) log($"  porownane: {i + 1}/{poz.Count}");
        }
        log($"kandydatow po geometrii: {kandydatow}, par z werdyktem: {pary.Count}");

        // ===== grupowanie =====
        // Laczymy TYLKO po werdyktach duplikatu — gdybysmy laczyli po "do wgladu",
        // wszystko zlalo by sie w jedna wielka grupe i raport bylby bezuzyteczny.
        var wgId = poz.ToDictionary(p => p.Id);
        var rodzic = poz.ToDictionary(p => p.Id, p => p.Id);
        string Znajdz2(string x) { while (rodzic[x] != x) { rodzic[x] = rodzic[rodzic[x]]; x = rodzic[x]; } return x; }
        void Polacz(string x, string y) { var rx = Znajdz2(x); var ry = Znajdz2(y); if (rx != ry) rodzic[rx] = ry; }
        foreach (var p in pary.Where(p => p.Werdykt == Duplikat || p.Werdykt == Nadzbior)) Polacz(p.A, p.B);

        var wynik = new WynikPorownania { Zbudowany = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        var grupyDupli = pary.Where(p => p.Werdykt == Duplikat || p.Werdykt == Nadzbior)
                             .GroupBy(p => Znajdz2(p.A));
        foreach (var g in grupyDupli)
        {
            var ids = g.SelectMany(p => new[] { p.A, p.B }).Distinct().ToList();
            var grupa = new Grupa
            {
                Pozycje = ids,
                Pary = g.ToList(),
                Werdykt = g.Any(p => p.Werdykt == Nadzbior) ? Nadzbior : Duplikat
            };
            foreach (var id in ids)
            {
                grupa.Punkty[id] = Jakosc(wgId[id], out string rozpiska);
                grupa.Rozpiska[id] = rozpiska;
            }
            grupa.Zwyciezca = ids.OrderByDescending(x => grupa.Punkty[x])
                                 .ThenByDescending(x => wgId[x].Tekstury.Count)
                                 .ThenBy(x => x, StringComparer.Ordinal).First();
            var przegrani = ids.Where(x => x != grupa.Zwyciezca).ToList();
            grupa.Powod = $"zwyciezca {grupa.Punkty[grupa.Zwyciezca]:F0} pkt, "
                        + $"przegrani {string.Join(", ", przegrani.Select(x => $"{grupa.Punkty[x]:F0}"))} pkt";
            wynik.Grupy.Add(grupa);
        }

        // pary do obejrzenia i przemalowania trafiaja jako grupy jednoparowe
        foreach (var p in pary.Where(p => p.Werdykt == DoWgladu || p.Werdykt == Przemalowanie))
        {
            var grupa = new Grupa { Pozycje = new List<string> { p.A, p.B }, Pary = new List<Para> { p }, Werdykt = p.Werdykt, Powod = p.Powod };
            foreach (var id in grupa.Pozycje) { grupa.Punkty[id] = Jakosc(wgId[id], out string r); grupa.Rozpiska[id] = r; }
            grupa.Zwyciezca = grupa.Pozycje.OrderByDescending(x => grupa.Punkty[x]).First();
            wynik.Grupy.Add(grupa);
        }

        // ===== podsumowanie =====
        foreach (var w in new[] { Duplikat, Nadzbior, DoWgladu, Przemalowanie })
        {
            int n = wynik.Grupy.Count(g => g.Werdykt == w);
            if (n > 0) wynik.Podsumowanie.Add($"{w}: {n}");
        }
        int doUsuniecia = wynik.Grupy.Where(g => g.Werdykt == Duplikat || g.Werdykt == Nadzbior)
                                     .Sum(g => g.Pozycje.Count - 1);
        wynik.Podsumowanie.Add($"pozycji do odrzucenia: {doUsuniecia}");
        foreach (var s in wynik.Podsumowanie) log("  " + s);
        return wynik;
    }

    /// <summary>"identyczna" / "podobna" / null gdy para w ogole nie jest kandydatem.</summary>
    static string WerdyktGeometrii(Pozycja a, Pozycja b, out double dist)
    {
        dist = double.MaxValue;
        // typy musza sie zgadzac tylko co do tego, czy to props — ubranie z jednej paczki
        // bywa w drugiej pod innym typem (u nas: accs_007 == jbib_015), wiec typu NIE
        // porownujemy. Propsy i ubrania mieszamy juz jednak nie.
        if (a.Props != b.Props) return null;

        if (a.Geo.HashPozycji != null && a.Geo.HashPozycji == b.Geo.HashPozycji) { dist = 0; return "identyczna"; }

        dist = Odciski.OdlegloscGeo(a.Geo.Hist, b.Geo.Hist);
        if (dist > Progi.GeoPodobna) return null;

        if (dist <= Progi.GeoIdentyczna
            && a.Geo.Trojkaty == b.Geo.Trojkaty
            && a.Geo.Wierzcholki == b.Geo.Wierzcholki
            && a.Geo.Trojkaty > 0) return "identyczna";

        double maxTri = Math.Max(a.Geo.Trojkaty, b.Geo.Trojkaty);
        if (maxTri < 1) return null;
        double roznicaTri = Math.Abs(a.Geo.Trojkaty - b.Geo.Trojkaty) / maxTri;
        if (roznicaTri > Progi.GeoPodobnaTri) return null;
        if (Odciski.OdlegloscBbox(a.Geo.Bbox, b.Geo.Bbox) > Progi.GeoPodobnaBbox) return null;
        return "podobna";
    }

    /// <summary>Czy dwie tekstury to ta sama grafika (ten sam kolor, nie tylko ten sam wzor).</summary>
    public static bool TaSamaGrafika(Tekstura x, Tekstura y)
    {
        if (x.Sha == y.Sha) return true;
        if (!x.Zdekodowana || !y.Zdekodowana) return false;
        double kol = Odciski.OdlegloscKoloru(x.Kolor, y.Kolor);
        // Plaska tekstura (np. jednolity kolor) daje PHash z szumu — wtedy ufamy samemu
        // kolorowi, ale wymagamy scislejszej zgodnosci.
        if (x.Wariancja < Progi.TexWariancjaMin || y.Wariancja < Progi.TexWariancjaMin)
            return kol <= Progi.TexKolorPlaska;
        int ph = Odciski.Hamming(x.PHash, y.PHash);
        return ph >= 0 && ph <= Progi.TexPHash && kol <= Progi.TexKolor;
    }

    static Para Oceń(Pozycja a, Pozycja b, string geo, double dist)
    {
        var ta = a.Tekstury ?? new List<Tekstura>();
        var tb = b.Tekstury ?? new List<Tekstura>();
        var para = new Para { A = a.Id, B = b.Id, DistGeo = dist };

        if (ta.Count == 0 || tb.Count == 0)
        {
            para.Werdykt = DoWgladu;
            para.Powod = $"geometria {geo}, ale brak tekstur do porownania";
            return para;
        }

        var uzyteB = new bool[tb.Count];
        int dopasowaneA = 0;
        foreach (var x in ta)
        {
            for (int k = 0; k < tb.Count; k++)
            {
                if (uzyteB[k]) continue;
                if (TaSamaGrafika(x, tb[k])) { uzyteB[k] = true; dopasowaneA++; break; }
            }
        }
        int dopasowaneB = uzyteB.Count(v => v);
        para.WspolnychTekstur = dopasowaneA;
        para.PokrycieA = (double)dopasowaneA / ta.Count;
        para.PokrycieB = (double)dopasowaneB / tb.Count;
        double maxPokrycie = Math.Max(para.PokrycieA, para.PokrycieB);
        bool pelneA = para.PokrycieA >= Progi.PelnePokrycie;
        bool pelneB = para.PokrycieB >= Progi.PelnePokrycie;

        string opisTex = $"{dopasowaneA}/{ta.Count} i {dopasowaneB}/{tb.Count} tekstur wspolnych";

        if (geo == "identyczna")
        {
            if (pelneA && pelneB)
            {
                para.Werdykt = Duplikat;
                para.Powod = $"ten sam model, te same tekstury ({opisTex})";
            }
            else if (pelneA || pelneB)
            {
                para.Werdykt = Nadzbior;
                para.Powod = $"ten sam model; jeden zestaw tekstur zawiera sie w drugim ({opisTex})";
            }
            else if (maxPokrycie >= Progi.CzesciowePokrycie)
            {
                para.Werdykt = DoWgladu;
                para.Powod = $"ten sam model, tekstury czesciowo wspolne ({opisTex})";
            }
            else
            {
                // TEN SAM MESH, INNE TEKSTURY = przemalowanie. To NIE jest duplikat —
                // w paczkach do GTA to norma i skasowanie takiej pozycji zabiera ciuch.
                para.Werdykt = Przemalowanie;
                para.Powod = $"ten sam model, ale inne tekstury — to przemalowanie, nie duplikat ({opisTex})";
            }
        }
        else
        {
            if (pelneA && pelneB)
            {
                para.Werdykt = DoWgladu;
                para.Powod = $"model tylko PODOBNY (odleglosc {dist:F3}), ale tekstury te same ({opisTex})";
            }
            else if (maxPokrycie >= Progi.CzesciowePokrycie)
            {
                para.Werdykt = DoWgladu;
                para.Powod = $"model podobny (odleglosc {dist:F3}), tekstury czesciowo wspolne ({opisTex})";
            }
            else return null;    // podobny model + inne tekstury = po prostu inny ciuch
        }
        return para;
    }

    /// <summary>
    /// Punktacja jakosci 0..100. Rozpiska wraca tekstem, zeby w raporcie bylo widac,
    /// DLACZEGO cos wygralo, a nie tylko ze wygralo.
    /// </summary>
    public static double Jakosc(Pozycja p, out string rozpiska)
    {
        var t = p.Tekstury ?? new List<Tekstura>();
        if (t.Count == 0) { rozpiska = "brak tekstur"; return 0; }

        // rozdzielczosc: mediana liczby pikseli, odniesiona do 1024x1024 = komplet punktow
        var piksele = t.Select(x => (double)x.W * x.H).Where(x => x > 0).OrderBy(x => x).ToArray();
        double medPx = piksele.Length > 0 ? piksele[piksele.Length / 2] : 0;
        double pktRozdz = medPx > 0 ? Math.Clamp(Math.Log2(medPx) / Math.Log2(1024.0 * 1024.0), 0, 1.25) * 40 : 0;

        // mipmapy: 28% naszych tekstur ma tylko jeden poziom — bez mipow tekstura migocze
        double udzialMipow = t.Count(x => x.Mipy > 1) / (double)t.Count;
        double pktMipy = udzialMipow * 20;

        // liczba wariantow kolorystycznych — bogatszy wybor w menu
        double pktWarianty = Math.Min(t.Count, 20) / 20.0 * 20;

        // format wobec alfy: BC1 ma alfe 1-bitowa, wiec przy przezroczystosci to strata
        int zlyFormat = t.Count(x => x.Format == "BC1" && x.Alfa > 0.02f);
        double pktFormat = 10 * (1.0 - zlyFormat / (double)t.Count);

        double pktLod = Math.Clamp((p.Geo?.Lody ?? 0) / 3.0, 0, 1) * 10;

        double razem = pktRozdz + pktMipy + pktWarianty + pktFormat + pktLod;
        rozpiska = $"rozdzielczosc {Math.Sqrt(medPx):F0}px:{pktRozdz:F0} | "
                 + $"mipy {udzialMipow:P0}:{pktMipy:F0} | "
                 + $"wariantow {t.Count}:{pktWarianty:F0} | "
                 + $"format:{pktFormat:F0}" + (zlyFormat > 0 ? $" ({zlyFormat} BC1 z alfa)" : "") + " | "
                 + $"LOD {p.Geo?.Lody ?? 0}:{pktLod:F0}";
        return razem;
    }
}
