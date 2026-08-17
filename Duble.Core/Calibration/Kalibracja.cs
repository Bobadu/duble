// Kalibracja.cs — dobor progow POMIAREM, nie na wyczucie.
//
// Mamy trzy rodzaje par o znanej odpowiedzi i to wystarczy, zeby ustawic progi:
//
//   POZYTYWY   — pliki identyczne co do bajtu (ten sam SHA). Odleglosc MUSI wyjsc 0;
//                jesli nie wychodzi, odcisk jest zepsuty.
//   TRUDNE NEGATYWY — warianty kolorystyczne TEGO SAMEGO ciucha (litery a/b/c... przy
//                tym samym numerze). W skali szarosci wygladaja identycznie, wiec to
//                one rozstrzygaja, czy sam PHash wystarcza, czy potrzebny jest kolor.
//   NEGATYWY   — losowe pary roznych ciuchow.
//
// Prog ustawiamy PONIZEJ najblizszego negatywu, nie powyzej najdalszego pozytywu —
// falszywy duplikat kasuje ciuch, ktorego nie da sie odzyskac inaczej niz z paczki.
//
// Policz() oddaje rozklady jako DANE (percentyle + histogram) — aplikacja rysuje z nich wykresy slupkowe z zaznaczonymi
// progami i pokazuje propozycje; Uruchom() (CLI) drukuje to samo tekstem.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Duble.Core.Comparison;
using Duble.Core.Fingerprints;
using Duble.Core.Model;

namespace Duble.Core.Calibration;

/// <summary>Rozklad wartosci: percentyle + histogram w zadanym zakresie (ostatni kubelek zbiera wszystko powyzej Do).</summary>
public sealed class Rozklad
{
    public int N { get; set; }
    public double Min { get; set; }
    public double P01 { get; set; }
    public double P05 { get; set; }
    public double P50 { get; set; }
    public double P95 { get; set; }
    public double Max { get; set; }
    public double Od { get; set; }
    public double Do { get; set; }
    public int[] Kubelki { get; set; } = Array.Empty<int>();

    public static Rozklad Z(IEnumerable<double> dane, double od, double @do, int kubelki)
    {
        var s = dane.OrderBy(x => x).ToArray();
        var r = new Rozklad { N = s.Length, Od = od, Do = @do, Kubelki = new int[Math.Max(1, kubelki)] };
        if (s.Length == 0) return r;
        double P(double p) => s[Math.Min(s.Length - 1, Math.Max(0, (int)(p * s.Length)))];
        r.Min = s[0]; r.P01 = P(0.01); r.P05 = P(0.05); r.P50 = P(0.50); r.P95 = P(0.95); r.Max = s[^1];
        double szer = (@do - od) / r.Kubelki.Length;
        foreach (var v in s)
        {
            int k = szer > 0 ? (int)Math.Floor((v - od) / szer + 1e-9) : 0;   // +1e-9: 0.3/0.1 = 2.9999… -> kubelek 3, nie 2
            if (k < 0) k = 0; if (k >= r.Kubelki.Length) k = r.Kubelki.Length - 1;
            r.Kubelki[k]++;
        }
        return r;
    }

    public string Tekst(string format = "F4")
        => N == 0 ? "brak danych"
         : $"n={N,-7} min={Min.ToString(format, CultureInfo.InvariantCulture)} p01={P01.ToString(format, CultureInfo.InvariantCulture)} "
         + $"p05={P05.ToString(format, CultureInfo.InvariantCulture)} p50={P50.ToString(format, CultureInfo.InvariantCulture)} "
         + $"p95={P95.ToString(format, CultureInfo.InvariantCulture)} max={Max.ToString(format, CultureInfo.InvariantCulture)}";
}

public sealed class WynikKalibracji
{
    public string Kiedy { get; set; }
    public int Pozycje { get; set; }
    public int PozycjeZGeometria { get; set; }
    public int Tekstury { get; set; }
    public int TeksturyZdekodowane { get; set; }

    // geometria (odleglosc histogramow 0..)
    public Rozklad GeoIdentyczneSha { get; set; }
    public Rozklad GeoTenSamHash { get; set; }
    public Rozklad GeoNajblizszyObcy { get; set; }
    public int GeoParMiedzyPaczkami { get; set; }
    public int GeoPodejrzane { get; set; }
    /// <summary>Najblizsze pary o roznym meshu (d &lt; 0,05): do obejrzenia — duplikaty, ktorych hash nie zlapal, albo kolizje histogramu.</summary>
    public List<PodejrzanaPara> Podejrzane { get; set; } = new();

    // tekstury: PHash (hamming 0..256) i kolor (0..)
    public Rozklad PHashIdentyczne { get; set; }
    public Rozklad KolorIdentyczne { get; set; }
    public Rozklad PHashWarianty { get; set; }
    public Rozklad KolorWarianty { get; set; }
    public Rozklad Wariancja { get; set; }
    public Rozklad PHashLosowe { get; set; }
    public Rozklad KolorLosowe { get; set; }
    public List<BliskaLosowa> BliskieLosowe { get; set; } = new();

    /// <summary>Progi uzyte przy liczeniu (do zaznaczenia na wykresach).</summary>
    public Progi Progi { get; set; }
    /// <summary>Propozycja: GeoIdentyczna, GeoPodobna (4x), TexPHash, TexKolor — reszta jak Progi.</summary>
    public Progi Propozycja { get; set; }
}

public sealed class PodejrzanaPara { public double D { get; set; } public double Bbox { get; set; } public string A { get; set; } public string B { get; set; } public int TriA { get; set; } public int TriB { get; set; } }
public sealed class BliskaLosowa { public int PHash { get; set; } public double Kolor { get; set; } public string A { get; set; } public string B { get; set; } }

public static class Kalibracja
{
    public const double GeoZakres = 0.5; public const int GeoKubelki = 25;
    public const double PHashZakres = 128; public const int PHashKubelki = 32;
    public const double KolorZakres = 40; public const int KolorKubelki = 20;
    public const double WariancjaZakres = 80; public const int WariancjaKubelki = 20;

    public static WynikKalibracji Policz(Catalog katalog, Progi progi = null, CancellationToken ct = default)
    {
        progi ??= Progi.Domyslne;
        var w = new WynikKalibracji { Kiedy = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Progi = progi.Kopia(), Pozycje = katalog.Garments.Count };
        var poz = katalog.Garments.Where(p => p.Geometry?.ShapeHistogram != null && p.Geometry.Vertices > 0).ToList();
        w.PozycjeZGeometria = poz.Count;

        // ================= GEOMETRIA =================
        var pozytywySha = new List<double>();
        var pozytywyHash = new List<double>();
        var najblizszyObcy = new List<double>();
        var podejrzane = new List<(double d, Garment a, Garment b)>();
        for (int i = 0; i < poz.Count; i++)
        {
            if ((i & 15) == 0) ct.ThrowIfCancellationRequested();
            double min = double.MaxValue;
            for (int j = 0; j < poz.Count; j++)
            {
                if (i == j) continue;
                var a = poz[i]; var b = poz[j];
                double d = Odciski.OdlegloscGeo(a.Geometry.ShapeHistogram, b.Geometry.ShapeHistogram);
                bool tenSamMesh = a.Geometry.PositionHash != null && a.Geometry.PositionHash == b.Geometry.PositionHash;
                if (j > i)
                {
                    if (a.ModelSha256 == b.ModelSha256) pozytywySha.Add(d);
                    else if (tenSamMesh) pozytywyHash.Add(d);
                    if (tenSamMesh && a.PackName != b.PackName) w.GeoParMiedzyPaczkami++;
                    if (!tenSamMesh && d < 0.05) podejrzane.Add((d, a, b));
                }
                if (!tenSamMesh && d < min) min = d;
            }
            if (min < double.MaxValue) najblizszyObcy.Add(min);
        }
        w.GeoIdentyczneSha = Rozklad.Z(pozytywySha, 0, GeoZakres, GeoKubelki);
        w.GeoTenSamHash = Rozklad.Z(pozytywyHash, 0, GeoZakres, GeoKubelki);
        w.GeoNajblizszyObcy = Rozklad.Z(najblizszyObcy, 0, GeoZakres, GeoKubelki);
        w.GeoPodejrzane = podejrzane.Count;
        w.Podejrzane = podejrzane.OrderBy(x => x.d).Take(25)
            .Select(x => new PodejrzanaPara { D = x.d, Bbox = Odciski.OdlegloscBbox(x.a.Geometry.BoundingBox, x.b.Geometry.BoundingBox), A = x.a.Label + x.a.Suffix, B = x.b.Label + x.b.Suffix, TriA = x.a.Geometry.Triangles, TriB = x.b.Geometry.Triangles }).ToList();

        // ================= TEKSTURY =================
        ct.ThrowIfCancellationRequested();
        var wszystkie = katalog.Garments.SelectMany(p => p.Textures.Where(t => t.IsDecoded).Select(t => (poz: p, tex: t))).ToList();
        w.Tekstury = katalog.Garments.Sum(p => p.Textures.Count);
        w.TeksturyZdekodowane = wszystkie.Count;

        // pozytywy: ten sam SHA
        var phSha = new List<double>(); var kolSha = new List<double>();
        foreach (var g in wszystkie.GroupBy(x => x.tex.Sha256).Where(g => g.Count() > 1))
        {
            var l = g.ToList();
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                {
                    phSha.Add(Odciski.Hamming(l[i].tex.PerceptualHash, l[j].tex.PerceptualHash));
                    kolSha.Add(Odciski.OdlegloscKoloru(l[i].tex.ColorSignature, l[j].tex.ColorSignature));
                }
        }
        // trudne negatywy: warianty koloru tego samego ciucha
        var phWar = new List<double>(); var kolWar = new List<double>();
        foreach (var p in katalog.Garments)
        {
            var l = p.Textures.Where(t => t.IsDecoded).ToList();
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                {
                    if (l[i].Sha256 == l[j].Sha256) continue;
                    phWar.Add(Odciski.Hamming(l[i].PerceptualHash, l[j].PerceptualHash));
                    kolWar.Add(Odciski.OdlegloscKoloru(l[i].ColorSignature, l[j].ColorSignature));
                }
        }
        ct.ThrowIfCancellationRequested();
        // negatywy losowe (400k prob, ziarno stale -> powtarzalne)
        var rnd = new Random(12345);
        var phLos = new List<double>(); var kolLos = new List<double>();
        var bliskie = new List<BliskaLosowa>();
        for (int k = 0; k < 400_000 && wszystkie.Count > 1; k++)
        {
            if ((k & 4095) == 0) ct.ThrowIfCancellationRequested();
            var a = wszystkie[rnd.Next(wszystkie.Count)];
            var b = wszystkie[rnd.Next(wszystkie.Count)];
            if (ReferenceEquals(a.poz, b.poz) || a.tex.Sha256 == b.tex.Sha256) continue;
            int ph = Odciski.Hamming(a.tex.PerceptualHash, b.tex.PerceptualHash);
            double kol = Odciski.OdlegloscKoloru(a.tex.ColorSignature, b.tex.ColorSignature);
            phLos.Add(ph); kolLos.Add(kol);
            if (ph <= 24) bliskie.Add(new BliskaLosowa { PHash = ph, Kolor = kol, A = $"{a.poz.Label}/{a.tex.FileName}", B = $"{b.poz.Label}/{b.tex.FileName}" });
        }
        w.PHashIdentyczne = Rozklad.Z(phSha, 0, PHashZakres, PHashKubelki);
        w.KolorIdentyczne = Rozklad.Z(kolSha, 0, KolorZakres, KolorKubelki);
        w.PHashWarianty = Rozklad.Z(phWar, 0, PHashZakres, PHashKubelki);
        w.KolorWarianty = Rozklad.Z(kolWar, 0, KolorZakres, KolorKubelki);
        w.Wariancja = Rozklad.Z(wszystkie.Select(x => (double)x.tex.Variance), 0, WariancjaZakres, WariancjaKubelki);
        w.PHashLosowe = Rozklad.Z(phLos, 0, PHashZakres, PHashKubelki);
        w.KolorLosowe = Rozklad.Z(kolLos, 0, KolorZakres, KolorKubelki);
        w.BliskieLosowe = bliskie.OrderBy(v => v.PHash).ThenBy(v => v.Kolor).Take(20).ToList();

        // ================= PROPOZYCJA =================
        var prop = progi.Kopia();
        double progGeo = najblizszyObcy.Count > 0
            ? najblizszyObcy.OrderBy(x => x).ElementAt(Math.Max(0, (int)(0.001 * najblizszyObcy.Count))) / 3.0
            : progi.GeoIdentyczna;
        // przyciete do zakresow Progi.Sprawdz (na sztucznych/dziwnych katalogach propozycja moglaby wyjsc poza)
        prop.GeoIdentyczna = Math.Min(1, Math.Round(progGeo, 4));
        prop.GeoPodobna = Math.Min(1, Math.Round(progGeo * 4, 4));
        prop.TexPHash = phWar.Count > 0 ? (int)Math.Min(256, Math.Max(4, phWar.OrderBy(x => x).First() / 2)) : progi.TexPHash;
        prop.TexKolor = kolWar.Count > 0 ? Math.Min(100, Math.Round(kolWar.OrderBy(x => x).First() / 2, 2)) : progi.TexKolor;
        w.Propozycja = prop;
        return w;
    }

    /// <summary>Wydruk dla CLI (`duble kalibruj`).</summary>
    public static int Uruchom(Catalog katalog, Action<string> log)
    {
        var poz = katalog.Garments.Count(p => p.Geometry?.ShapeHistogram != null && p.Geometry.Vertices > 0);
        if (poz < 2) { log("[blad] za malo pozycji w katalogu"); return 1; }
        var w = Policz(katalog);
        var inv = CultureInfo.InvariantCulture;
        log($"pozycji z geometria: {w.PozycjeZGeometria} / {w.Pozycje}");
        log(""); log("=== GEOMETRIA ===");
        log($"  pary identyczne co do bajtu       : {w.GeoIdentyczneSha.Tekst()}");
        log($"  pary o tym samym hashu pozycji    : {w.GeoTenSamHash.Tekst()}");
        log($"  NAJBLIZSZY OBCY MESH (na pozycje) : {w.GeoNajblizszyObcy.Tekst()}");
        log($"  par 'ten sam mesh' miedzy paczkami: {w.GeoParMiedzyPaczkami}");
        log(""); log($"  --- pary o ROZNYM meshu, a odlegloscia < 0,05: {w.GeoPodejrzane} ---");
        foreach (var p in w.Podejrzane) log($"    d={p.D.ToString("F4", inv)} bbox={p.Bbox.ToString("F3", inv)}  {p.A} (tri {p.TriA})  vs  {p.B} (tri {p.TriB})");
        log(""); log("=== TEKSTURY ===");
        log($"tekstur zdekodowanych: {w.TeksturyZdekodowane} / {w.Tekstury}");
        log($"  identyczne co do bajtu — PHash    : {w.PHashIdentyczne.Tekst("F1")}");
        log($"  identyczne co do bajtu — kolor    : {w.KolorIdentyczne.Tekst("F2")}");
        log($"  WARIANTY KOLORU tego samego ciucha — PHash : {w.PHashWarianty.Tekst("F1")}");
        log($"  WARIANTY KOLORU tego samego ciucha — kolor : {w.KolorWarianty.Tekst("F2")}");
        log($"  WARIANCJA jasnosci (wszystkie)             : {w.Wariancja.Tekst("F1")}");
        log($"  losowe pary — PHash               : {w.PHashLosowe.Tekst("F1")}");
        log($"  losowe pary — kolor               : {w.KolorLosowe.Tekst("F2")}");
        log(""); log($"  --- losowe pary z PHash <= 24 (kandydaci na falszywy duplikat): {w.BliskieLosowe.Count} ---");
        foreach (var x in w.BliskieLosowe) log($"    ph={x.PHash,-4} kol={x.Kolor.ToString("F2", inv),7}  {x.A}  vs  {x.B}");
        log(""); log("=== PROPOZYCJA PROGOW ===");
        log($"  geometria — identyczna : dist <= {w.Propozycja.GeoIdentyczna.ToString("F4", inv)}   (1/3 najblizszego obcego mesha)");
        log($"  geometria — podobna    : dist <= {w.Propozycja.GeoPodobna.ToString("F4", inv)}");
        log($"  tekstura  — PHash      : hamming <= {w.Propozycja.TexPHash}");
        log($"  tekstura  — kolor      : dist <= {w.Propozycja.TexKolor.ToString("F2", inv)}   (polowa najmniejszej roznicy miedzy wariantami)");
        return 0;
    }
}
