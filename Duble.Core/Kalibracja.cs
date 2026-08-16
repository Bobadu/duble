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
using System;
using System.Collections.Generic;
using System.Linq;

namespace Duble;

public static class Kalibracja
{
    static string Percentyle(IReadOnlyList<double> dane, string format = "F4")
    {
        if (dane.Count == 0) return "brak danych";
        var s = dane.OrderBy(x => x).ToArray();
        double P(double p) => s[Math.Min(s.Length - 1, Math.Max(0, (int)(p * s.Length)))];
        return $"n={s.Length,-7} min={s[0].ToString(format)} p01={P(0.01).ToString(format)} "
             + $"p05={P(0.05).ToString(format)} p50={P(0.50).ToString(format)} "
             + $"p95={P(0.95).ToString(format)} max={s[^1].ToString(format)}";
    }

    public static int Uruchom(Katalog katalog, Action<string> log)
    {
        var poz = katalog.Pozycje.Where(p => p.Geo?.Hist != null && p.Geo.Wierzcholki > 0).ToList();
        if (poz.Count < 2) { log("[blad] za malo pozycji w katalogu"); return 1; }
        log($"pozycji z geometria: {poz.Count} / {katalog.Pozycje.Count}");

        // ================= GEOMETRIA =================
        log("");
        log("=== GEOMETRIA ===");

        var pozytywySha = new List<double>();
        var pozytywyHash = new List<double>();
        var najblizszyObcy = new List<double>();
        int parIdentycznychMiedzyPaczkami = 0;
        // najblizsze pary o ROZNYM hashu pozycji — trzeba je obejrzec, bo albo sa
        // prawdziwymi duplikatami, ktorych hash nie zlapal, albo kolizjami histogramu
        var podejrzane = new List<(double d, Pozycja a, Pozycja b)>();

        for (int i = 0; i < poz.Count; i++)
        {
            double min = double.MaxValue;
            for (int j = 0; j < poz.Count; j++)
            {
                if (i == j) continue;
                var a = poz[i]; var b = poz[j];
                double d = Odciski.OdlegloscGeo(a.Geo.Hist, b.Geo.Hist);
                bool tenSamMesh = a.Geo.HashPozycji != null && a.Geo.HashPozycji == b.Geo.HashPozycji;
                if (j > i)
                {
                    if (a.ShaYdd == b.ShaYdd) pozytywySha.Add(d);
                    else if (tenSamMesh) pozytywyHash.Add(d);
                    if (tenSamMesh && a.Paczka != b.Paczka) parIdentycznychMiedzyPaczkami++;
                    if (!tenSamMesh && d < 0.05) podejrzane.Add((d, a, b));
                }
                if (!tenSamMesh && d < min) min = d;
            }
            if (min < double.MaxValue) najblizszyObcy.Add(min);
        }

        log($"  pary identyczne co do bajtu       : {Percentyle(pozytywySha)}");
        log($"  pary o tym samym hashu pozycji    : {Percentyle(pozytywyHash)}");
        log($"  NAJBLIZSZY OBCY MESH (na pozycje) : {Percentyle(najblizszyObcy)}");
        log($"  par 'ten sam mesh' miedzy paczkami: {parIdentycznychMiedzyPaczkami}");

        log("");
        log($"  --- pary o ROZNYM meshu, a odlegloscia < 0,05: {podejrzane.Count} ---");
        foreach (var (d, a, b) in podejrzane.OrderBy(x => x.d).Take(25))
        {
            // ile trojkatow i jak blisko sa pudelka — to rozstrzyga, czy histogram klamie
            double bb = Odciski.OdlegloscBbox(a.Geo.Bbox, b.Geo.Bbox);
            log($"    d={d:F4} bbox={bb:F3}  {a.Opis}{a.Sufiks} (tri {a.Geo.Trojkaty}, v {a.Geo.Wierzcholki})"
              + $"  vs  {b.Opis}{b.Sufiks} (tri {b.Geo.Trojkaty}, v {b.Geo.Wierzcholki})");
        }

        // ================= TEKSTURY =================
        log("");
        log("=== TEKSTURY ===");

        var wszystkie = katalog.Pozycje
            .SelectMany(p => p.Tekstury.Where(t => t.Zdekodowana).Select(t => (poz: p, tex: t)))
            .ToList();
        log($"tekstur zdekodowanych: {wszystkie.Count} / {katalog.Pozycje.Sum(p => p.Tekstury.Count)}");

        // --- pozytywy: ten sam SHA ---
        var phSha = new List<double>();
        var kolSha = new List<double>();
        foreach (var g in wszystkie.GroupBy(x => x.tex.Sha).Where(g => g.Count() > 1))
        {
            var l = g.ToList();
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                {
                    phSha.Add(Odciski.Hamming(l[i].tex.PHash, l[j].tex.PHash));
                    kolSha.Add(Odciski.OdlegloscKoloru(l[i].tex.Kolor, l[j].tex.Kolor));
                }
        }
        log($"  identyczne co do bajtu — PHash    : {Percentyle(phSha, "F1")}");
        log($"  identyczne co do bajtu — kolor    : {Percentyle(kolSha, "F2")}");

        // --- trudne negatywy: warianty kolorystyczne tego samego ciucha ---
        var phWar = new List<double>();
        var kolWar = new List<double>();
        foreach (var p in katalog.Pozycje)
        {
            var l = p.Tekstury.Where(t => t.Zdekodowana).ToList();
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                {
                    if (l[i].Sha == l[j].Sha) continue;
                    phWar.Add(Odciski.Hamming(l[i].PHash, l[j].PHash));
                    kolWar.Add(Odciski.OdlegloscKoloru(l[i].Kolor, l[j].Kolor));
                }
        }
        log($"  WARIANTY KOLORU tego samego ciucha — PHash : {Percentyle(phWar, "F1")}");
        log($"  WARIANTY KOLORU tego samego ciucha — kolor : {Percentyle(kolWar, "F2")}");

        log($"  WARIANCJA jasnosci (wszystkie)             : {Percentyle(wszystkie.Select(x => (double)x.tex.Wariancja).ToList(), "F1")}");

        // --- negatywy losowe ---
        var rnd = new Random(12345);
        var phLos = new List<double>();
        var kolLos = new List<double>();
        var najblizszeLosowe = new List<(int ph, double kol, string a, string b)>();
        for (int k = 0; k < 400_000 && wszystkie.Count > 1; k++)
        {
            var a = wszystkie[rnd.Next(wszystkie.Count)];
            var b = wszystkie[rnd.Next(wszystkie.Count)];
            if (ReferenceEquals(a.poz, b.poz) || a.tex.Sha == b.tex.Sha) continue;
            int ph = Odciski.Hamming(a.tex.PHash, b.tex.PHash);
            double kol = Odciski.OdlegloscKoloru(a.tex.Kolor, b.tex.Kolor);
            phLos.Add(ph); kolLos.Add(kol);
            if (ph <= 24)
                najblizszeLosowe.Add((ph, kol, $"{a.poz.Opis}/{a.tex.Plik}", $"{b.poz.Opis}/{b.tex.Plik}"));
        }
        log($"  losowe pary — PHash               : {Percentyle(phLos, "F1")}");
        log($"  losowe pary — kolor               : {Percentyle(kolLos, "F2")}");

        log("");
        log($"  --- losowe pary z PHash <= 24 (kandydaci na falszywy duplikat): {najblizszeLosowe.Count} ---");
        foreach (var x in najblizszeLosowe.OrderBy(v => v.ph).ThenBy(v => v.kol).Take(20))
            log($"    ph={x.ph,-4} kol={x.kol,7:F2}  {x.a}  vs  {x.b}");

        // ================= WNIOSEK =================
        log("");
        log("=== PROPOZYCJA PROGOW ===");
        double progGeo = najblizszyObcy.Count > 0
            ? najblizszyObcy.OrderBy(x => x).ElementAt(Math.Max(0, (int)(0.001 * najblizszyObcy.Count))) / 3.0
            : 0.02;
        log($"  geometria — identyczna : dist <= {progGeo:F4}   (1/3 najblizszego obcego mesha)");
        log($"  geometria — podobna    : dist <= {progGeo * 4:F4}");
        double progPh = phWar.Count > 0 ? Math.Max(4, phWar.OrderBy(x => x).First() / 2) : 6;
        log($"  tekstura  — PHash      : hamming <= {progPh:F0}");
        double progKol = kolWar.Count > 0 ? kolWar.OrderBy(x => x).First() / 2 : 6;
        log($"  tekstura  — kolor      : dist <= {progKol:F2}   (polowa najmniejszej roznicy miedzy wariantami)");
        return 0;
    }
}
