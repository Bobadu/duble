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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Duble.Core.Fingerprints;
using Duble.Core.Indexing;
using Duble.Core.Model;

namespace Duble.Core.Comparison;

public class Para
{
    public string A { get; set; }
    public string B { get; set; }
    public string Werdykt { get; set; }
    /// <summary>Kod + parametry; tekst PL/EN daje Teksty.Powod(powod, jezyk).</summary>
    public Powod Powod { get; set; }
    public double DistGeo { get; set; }
    public double PokrycieA { get; set; }
    public double PokrycieB { get; set; }
    public int WspolnychTekstur { get; set; }
}

public class Grupa
{
    /// <summary>Stabilny identyfikator grupy: 16 hex z SHA-256 posortowanych id czlonkow — decyzje uzytkownika przezywaja przeliczenie.</summary>
    public string Id { get; set; }
    public List<string> Pozycje { get; set; } = new();
    public string Werdykt { get; set; }
    public string Zwyciezca { get; set; }
    public Powod Powod { get; set; }
    public List<Para> Pary { get; set; } = new();
    public Dictionary<string, double> Punkty { get; set; } = new();
    /// <summary>Skladniki oceny jakosci per pozycja (tekst: Punktacja.Tekst(jezyk)).</summary>
    public Dictionary<string, Punktacja> Rozpiska { get; set; } = new();

    public static string PoliczId(IEnumerable<string> pozycje)
    {
        var s = string.Join("\n", pozycje.OrderBy(x => x, StringComparer.Ordinal));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s))).Substring(0, 16);
    }
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

    public static WynikPorownania Znajdz(Catalog katalog, Action<string> log, Thresholds progi = null)
        => Znajdz(katalog, log, progi, null, default);

    /// <summary>Jak wyzej, z postepem (Etap "porownaj") i anulowaniem — dla aplikacji okienkowej.</summary>
    public static WynikPorownania Znajdz(Catalog katalog, Action<string> log, Thresholds progi, Action<Postep> postep, System.Threading.CancellationToken anuluj)
    {
        progi ??= Thresholds.Default;
        log ??= _ => { };
        var poz = katalog.Garments.Where(p => p.Geometry?.ShapeHistogram != null && p.Geometry.Vertices > 0).ToList();
        log($"pozycji do porownania: {poz.Count}");

        var pary = new List<Para>();
        int kandydatow = 0;
        for (int i = 0; i < poz.Count; i++)
        {
            anuluj.ThrowIfCancellationRequested();
            if ((i + 1) % 50 == 0 || i + 1 == poz.Count) postep?.Invoke(new Postep("porownaj", i + 1, poz.Count, null));
            for (int j = i + 1; j < poz.Count; j++)
            {
                var a = poz[i]; var b = poz[j];
                var g = WerdyktGeometrii(a, b, progi, out double dist);
                if (g == null) continue;
                kandydatow++;
                var para = Oceń(a, b, g, dist, progi);
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
            grupa.Id = Grupa.PoliczId(ids);
            foreach (var id in ids)
            {
                var pkt = Jakosc(wgId[id]);
                grupa.Punkty[id] = pkt.Razem;
                grupa.Rozpiska[id] = pkt;
            }
            grupa.Zwyciezca = ids.OrderByDescending(x => grupa.Punkty[x])
                                 .ThenByDescending(x => wgId[x].Textures.Count)
                                 .ThenBy(x => x, StringComparer.Ordinal).First();
            var przegrani = ids.Where(x => x != grupa.Zwyciezca).ToList();
            grupa.Powod = new Powod("WINNER",
                ("zw", grupa.Punkty[grupa.Zwyciezca].ToString("F0", CultureInfo.InvariantCulture)),
                ("przegrani", string.Join(", ", przegrani.Select(x => grupa.Punkty[x].ToString("F0", CultureInfo.InvariantCulture)))));
            wynik.Grupy.Add(grupa);
        }

        // pary do obejrzenia i przemalowania trafiaja jako grupy jednoparowe
        foreach (var p in pary.Where(p => p.Werdykt == DoWgladu || p.Werdykt == Przemalowanie))
        {
            var grupa = new Grupa { Pozycje = new List<string> { p.A, p.B }, Pary = new List<Para> { p }, Werdykt = p.Werdykt, Powod = p.Powod };
            grupa.Id = Grupa.PoliczId(grupa.Pozycje);
            foreach (var id in grupa.Pozycje) { var pkt = Jakosc(wgId[id]); grupa.Punkty[id] = pkt.Razem; grupa.Rozpiska[id] = pkt; }
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
    static string WerdyktGeometrii(Garment a, Garment b, Thresholds progi, out double dist)
    {
        dist = double.MaxValue;
        // typy musza sie zgadzac tylko co do tego, czy to props — ubranie z jednej paczki
        // bywa w drugiej pod innym typem (u nas: accs_007 == jbib_015), wiec typu NIE
        // porownujemy. Propsy i ubrania mieszamy juz jednak nie.
        if (a.IsProp != b.IsProp) return null;

        if (a.Geometry.PositionHash != null && a.Geometry.PositionHash == b.Geometry.PositionHash) { dist = 0; return "identyczna"; }

        dist = Odciski.OdlegloscGeo(a.Geometry.ShapeHistogram, b.Geometry.ShapeHistogram);
        if (dist > progi.GeometrySimilar) return null;

        if (dist <= progi.GeometryIdentical
            && a.Geometry.Triangles == b.Geometry.Triangles
            && a.Geometry.Vertices == b.Geometry.Vertices
            && a.Geometry.Triangles > 0) return "identyczna";

        double maxTri = Math.Max(a.Geometry.Triangles, b.Geometry.Triangles);
        if (maxTri < 1) return null;
        double roznicaTri = Math.Abs(a.Geometry.Triangles - b.Geometry.Triangles) / maxTri;
        if (roznicaTri > progi.GeometryTriangleTolerance) return null;
        if (Odciski.OdlegloscBbox(a.Geometry.BoundingBox, b.Geometry.BoundingBox) > progi.GeometryBoundsTolerance) return null;
        return "podobna";
    }

    /// <summary>Czy dwie tekstury to ta sama grafika (ten sam kolor, nie tylko ten sam wzor).</summary>
    public static bool TaSamaGrafika(TextureInfo x, TextureInfo y, Thresholds progi = null)
    {
        progi ??= Thresholds.Default;
        if (x.Sha256 == y.Sha256) return true;
        if (!x.IsDecoded || !y.IsDecoded) return false;
        double kol = Odciski.OdlegloscKoloru(x.ColorSignature, y.ColorSignature);
        // Plaska tekstura (np. jednolity kolor) daje PHash z szumu — wtedy ufamy samemu
        // kolorowi, ale wymagamy scislejszej zgodnosci.
        if (x.Variance < progi.FlatTextureVariance || y.Variance < progi.FlatTextureVariance)
            return kol <= progi.FlatTextureColorDistance;
        int ph = Odciski.Hamming(x.PerceptualHash, y.PerceptualHash);
        return ph >= 0 && ph <= progi.TextureHashDistance && kol <= progi.TextureColorDistance;
    }

    static Para Oceń(Garment a, Garment b, string geo, double dist, Thresholds progi)
    {
        var ta = a.Textures ?? new List<TextureInfo>();
        var tb = b.Textures ?? new List<TextureInfo>();
        var para = new Para { A = a.Id, B = b.Id, DistGeo = dist };

        if (ta.Count == 0 || tb.Count == 0)
        {
            para.Werdykt = DoWgladu;
            para.Powod = new Powod("NO_TEXTURES", ("geo", "@geo." + geo));
            return para;
        }

        var uzyteB = new bool[tb.Count];
        int dopasowaneA = 0;
        foreach (var x in ta)
        {
            for (int k = 0; k < tb.Count; k++)
            {
                if (uzyteB[k]) continue;
                if (TaSamaGrafika(x, tb[k], progi)) { uzyteB[k] = true; dopasowaneA++; break; }
            }
        }
        int dopasowaneB = uzyteB.Count(v => v);
        para.WspolnychTekstur = dopasowaneA;
        para.PokrycieA = (double)dopasowaneA / ta.Count;
        para.PokrycieB = (double)dopasowaneB / tb.Count;
        double maxPokrycie = Math.Max(para.PokrycieA, para.PokrycieB);
        bool pelneA = para.PokrycieA >= progi.FullCoverage;
        bool pelneB = para.PokrycieB >= progi.FullCoverage;

        // parametry wspolne wszystkich powodow: ile tekstur wspolnych po obu stronach ({a}/{na} i {b}/{nb})
        (string, object)[] Tex(params (string, object)[] extra)
        {
            var w = new List<(string, object)> { ("a", dopasowaneA), ("na", ta.Count), ("b", dopasowaneB), ("nb", tb.Count) };
            w.AddRange(extra);
            return w.ToArray();
        }
        string distTekst = dist.ToString("F3", CultureInfo.InvariantCulture);

        if (geo == "identyczna")
        {
            if (pelneA && pelneB)
            {
                para.Werdykt = Duplikat;
                para.Powod = new Powod("SAME_MODEL_SAME_TEX", Tex());
            }
            else if (pelneA || pelneB)
            {
                para.Werdykt = Nadzbior;
                para.Powod = new Powod("SAME_MODEL_SUBSET", Tex());
            }
            else if (maxPokrycie >= progi.PartialCoverage)
            {
                para.Werdykt = DoWgladu;
                para.Powod = new Powod("SAME_MODEL_PARTIAL", Tex());
            }
            else
            {
                // TEN SAM MESH, INNE TEKSTURY = przemalowanie. To NIE jest duplikat —
                // w paczkach do GTA to norma i skasowanie takiej pozycji zabiera ciuch.
                para.Werdykt = Przemalowanie;
                para.Powod = new Powod("SAME_MODEL_OTHER_TEX", Tex());
            }
        }
        else
        {
            if (pelneA && pelneB)
            {
                para.Werdykt = DoWgladu;
                para.Powod = new Powod("SIMILAR_MODEL_SAME_TEX", Tex(("dist", distTekst)));
            }
            else if (maxPokrycie >= progi.PartialCoverage)
            {
                para.Werdykt = DoWgladu;
                para.Powod = new Powod("SIMILAR_MODEL_PARTIAL", Tex(("dist", distTekst)));
            }
            else return null;    // podobny model + inne tekstury = po prostu inny ciuch
        }
        return para;
    }

    /// <summary>
    /// Punktacja jakosci 0..100 ze skladnikami — zeby w raporcie i aplikacji bylo widac,
    /// DLACZEGO cos wygralo, a nie tylko ze wygralo.
    /// </summary>
    public static Punktacja Jakosc(Garment p)
    {
        var t = p.Textures ?? new List<TextureInfo>();
        if (t.Count == 0) return new Punktacja { BrakTekstur = true, Razem = 0 };

        // rozdzielczosc: mediana liczby pikseli, odniesiona do 1024x1024 = komplet punktow
        var piksele = t.Select(x => (double)x.Width * x.Height).Where(x => x > 0).OrderBy(x => x).ToArray();
        double medPx = piksele.Length > 0 ? piksele[piksele.Length / 2] : 0;
        double pktRozdz = medPx > 0 ? Math.Clamp(Math.Log2(medPx) / Math.Log2(1024.0 * 1024.0), 0, 1.25) * 40 : 0;

        // mipmapy: 28% naszych tekstur ma tylko jeden poziom — bez mipow tekstura migocze
        double udzialMipow = t.Count(x => x.MipLevels > 1) / (double)t.Count;
        double pktMipy = udzialMipow * 20;

        // liczba wariantow kolorystycznych — bogatszy wybor w menu
        double pktWarianty = Math.Min(t.Count, 20) / 20.0 * 20;

        // format wobec alfy: BC1 ma alfe 1-bitowa, wiec przy przezroczystosci to strata
        int zlyFormat = t.Count(x => x.Format == "BC1" && x.AlphaShare > 0.02f);
        double pktFormat = 10 * (1.0 - zlyFormat / (double)t.Count);

        double pktLod = Math.Clamp((p.Geometry?.LodLevels ?? 0) / 3.0, 0, 1) * 10;

        return new Punktacja
        {
            Razem = pktRozdz + pktMipy + pktWarianty + pktFormat + pktLod,
            Rozdz = pktRozdz, Mipy = pktMipy, Warianty = pktWarianty, Format = pktFormat, Lod = pktLod,
            RozdzPx = Math.Sqrt(medPx), UdzialMipow = udzialMipow, LiczbaWariantow = t.Count, ZlyFormat = zlyFormat,
            Lody = p.Geometry?.LodLevels ?? 0
        };
    }

    /// <summary>Zgodnosc wstecz: suma punktow + rozpiska po polsku.</summary>
    public static double Jakosc(Garment p, out string rozpiska)
    {
        var pkt = Jakosc(p);
        rozpiska = pkt.Tekst("pl");
        return pkt.Razem;
    }
}
