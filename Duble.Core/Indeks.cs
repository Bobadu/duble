// Indeks.cs — przejscie zrodla (folder albo archiwum .rpf) i zbudowanie z niego pozycji.
//
// Zrodlem moze byc:
//   * folder rozpakowanej paczki (nasze staging\wardrobe2\gen9\<paczka>), gdzie
//     kontenery sa FOLDERAMI o nazwie konczacej sie na .rpf
//   * prawdziwe archiwum .rpf (paczka prosto z internetu albo zbudowana dlc.rpf)
//
// Grupowanie w pozycje idzie po nazwach plikow, wg konwencji R*:
//   ubranie:  <typ>_<NNN>_<u|r>.ydd  +  <typ>_diff_<NNN>_<litera>_<rasa>.ytd
//   props:    p_<anchor>_<NNN>.ydd   +  p_<anchor>_diff_<NNN>_<litera>.ytd
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CodeWalker.GameFiles;

namespace Duble;

public static class Indeks
{
    // Rozbior nazw (konwencja R*, ogonek "_1" eksporterow, prefiks FiveM "ped^") siedzi w Nazwy.cs.

    /// <summary>Jeden plik zrodlowy, niezaleznie od tego czy lezy w folderze, czy w archiwum.</summary>
    class Wpis
    {
        public string Nazwa;        // sama nazwa pliku
        public string Kontener;     // nazwa kontenera (folder/archiwum .rpf) albo ""
        public string Sciezka;      // sciezka logiczna do raportu
        public Func<byte[]> Dane;
        public long Dlugosc;
    }

    public static List<Pozycja> Zrodlo(string sciezka, string nazwaPaczki, Action<string> log)
    {
        sciezka = Path.GetFullPath(sciezka);
        var wpisy = Directory.Exists(sciezka) ? ZFolderu(sciezka) : ZArchiwum(sciezka, log);
        if (wpisy.Count == 0) { log($"[uwaga] {sciezka}: nie znalazlam zadnych .ydd/.ytd"); return new List<Pozycja>(); }

        var paczka = nazwaPaczki ?? Path.GetFileNameWithoutExtension(sciezka.TrimEnd(Path.DirectorySeparatorChar));

        // WYKRYCIE FORMATU. RpfManager.IsGen9 jest STATYCZNE i czytane przy tworzeniu
        // czytnika, wiec nie wolno go zmieniac w trakcie pracy rownoleglej — ustawiamy
        // je raz na cale zrodlo. Paczka jest jednorodna: albo cala legacy, albo cala gen9.
        bool gen9 = WykryjGen9(wpisy, log);
        RpfManager.IsGen9 = gen9;
        log($"  format: {(gen9 ? "gen9 (Enhanced)" : "legacy")}");

        // Pliki o nazwie spoza konwencji NIE moga zniknac po cichu — to zwykle wlasnie
        // one sa smieciem po eksporcie i kandydatem na duplikat. Zbieramy je i wypisujemy.
        var pominiete = new ConcurrentBag<string>();

        // --- modele ---
        var modele = new ConcurrentBag<Pozycja>();
        var pliki = wpisy.Where(w => w.Nazwa.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase)).ToList();
        int zrobione = 0;
        Parallel.ForEach(pliki, w =>
        {
            var p = Model(w, paczka, gen9);
            if (p != null) modele.Add(p); else pominiete.Add(w.Nazwa);
            int n = Interlocked.Increment(ref zrobione);
            if (n % 200 == 0) log($"  modele: {n}/{pliki.Count}");
        });

        // --- tekstury ---
        var teksturyWg = new ConcurrentDictionary<string, ConcurrentBag<(Tekstura t, string rasa)>>();
        var tpliki = wpisy.Where(w => w.Nazwa.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)).ToList();
        zrobione = 0;
        Parallel.ForEach(tpliki, w =>
        {
            var wynik = TeksturaZ(w, out string klucz, out string rasa);
            if (wynik != null)
                teksturyWg.GetOrAdd(klucz, _ => new ConcurrentBag<(Tekstura, string)>()).Add((wynik, rasa));
            else pominiete.Add(w.Nazwa);
            int n = Interlocked.Increment(ref zrobione);
            if (n % 500 == 0) log($"  tekstury: {n}/{tpliki.Count}");
        });

        if (!pominiete.IsEmpty)
        {
            var lp = pominiete.OrderBy(x => x).ToList();
            log($"  [uwaga] {lp.Count} plikow spoza konwencji nazw — POMINIETE: "
                + string.Join(", ", lp.Take(6)) + (lp.Count > 6 ? $" (+{lp.Count - 6})" : ""));
        }

        // --- przypisanie tekstur do modeli ---
        // Klucz tekstury nie zawiera sufiksu u/r (tekstury go nie maja), wiec przy modelach
        // _u i _r o tym samym numerze rozdzielamy po rasie: _u bierze "uni", _r bierze reszte.
        var lista = modele.ToList();
        foreach (var grupa in lista.GroupBy(p => $"{p.Kontener}|{p.Typ}|{p.Numer}|{p.Props}"))
        {
            if (!teksturyWg.TryGetValue(grupa.Key, out var wszystkie)) continue;
            var czlonkowie = grupa.ToList();
            bool jestU = czlonkowie.Any(c => c.Sufiks.StartsWith("u"));
            bool jestR = czlonkowie.Any(c => c.Sufiks.StartsWith("r"));
            foreach (var m in czlonkowie)
            {
                IEnumerable<(Tekstura t, string rasa)> moje = wszystkie;
                if (jestU && jestR)
                    moje = m.Sufiks.StartsWith("u")
                        ? wszystkie.Where(x => x.rasa.Equals("uni", StringComparison.OrdinalIgnoreCase))
                        : wszystkie.Where(x => !x.rasa.Equals("uni", StringComparison.OrdinalIgnoreCase));
                m.Tekstury = moje.Select(x => x.t).OrderBy(t => t.Plik, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
        return lista.OrderBy(p => p.Typ).ThenBy(p => p.Numer).ToList();
    }

    // ===================== wejscie: folder / archiwum =====================

    static List<Wpis> ZFolderu(string korzen)
    {
        var wy = new List<Wpis>();
        foreach (var f in Directory.EnumerateFiles(korzen, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(f);
            if (!ext.Equals(".ydd", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".ytd", StringComparison.OrdinalIgnoreCase)) continue;
            // kontener = najblizszy przodek o nazwie konczacej sie na .rpf (u nas to FOLDER)
            string kont = "";
            var d = Path.GetDirectoryName(f);
            while (!string.IsNullOrEmpty(d) && d.Length >= korzen.Length)
            {
                var nazwa = Path.GetFileName(d);
                if (nazwa.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) { kont = nazwa; break; }
                d = Path.GetDirectoryName(d);
            }
            var fi = new FileInfo(f);
            var sciezka = f;
            wy.Add(new Wpis { Nazwa = Path.GetFileName(f), Kontener = kont, Sciezka = sciezka, Dlugosc = fi.Length, Dane = () => File.ReadAllBytes(sciezka) });
        }
        return wy;
    }

    static List<Wpis> ZArchiwum(string plik, Action<string> log)
    {
        var wy = new List<Wpis>();
        if (!File.Exists(plik)) return wy;
        var rpf = new RpfFile(plik, Path.GetFileName(plik));
        rpf.ScanStructure(s => { }, m => log("[scan] " + m));
        void Chodz(RpfFile f)
        {
            if (f.AllEntries != null)
                foreach (var e in f.AllEntries.OfType<RpfFileEntry>())
                {
                    var ext = Path.GetExtension(e.Name);
                    if (!ext.Equals(".ydd", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".ytd", StringComparison.OrdinalIgnoreCase)) continue;
                    var wlasciciel = e.File;
                    wy.Add(new Wpis
                    {
                        Nazwa = e.Name,
                        Kontener = Path.GetFileName(f.Path),
                        // zapamietujemy archiwum i sciezke w srodku, zeby raport mogl
                        // wydobyc te sama teksture drugi raz (do miniatury)
                        Sciezka = plik + "|" + e.Path,
                        Dlugosc = e.GetFileSize(),
                        Dane = () => wlasciciel.ExtractFile(e)
                    });
                }
            if (f.Children != null) foreach (var c in f.Children) Chodz(c);
        }
        Chodz(rpf);
        return wy;
    }

    // ===================== wykrycie gen9 =====================

    /// <summary>
    /// Paczki z internetu bywaja w formacie legacy, nasze skonwertowane sa gen9. Uklad
    /// bloku Texture rozni sie miedzy nimi, wiec zly wybor daje ciche smieci zamiast bledu.
    /// Sprawdzamy probke w obu trybach i wybieramy ten, ktory daje sensowne wartosci.
    /// </summary>
    static bool WykryjGen9(List<Wpis> wpisy, Action<string> log)
    {
        var probka = wpisy.Where(w => w.Nazwa.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
        if (probka.Count == 0) probka = wpisy.Take(8).ToList();

        int Punkty(bool gen9)
        {
            RpfManager.IsGen9 = gen9;
            int ok = 0;
            foreach (var w in probka)
            {
                try
                {
                    if (w.Nazwa.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
                    {
                        var ytd = new YtdFile();
                        RpfFile.LoadResourceFile(ytd, w.Dane(), 13);
                        var t = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
                        if (t != null && t.Width > 0 && t.Width <= 16384 && t.Height > 0 && t.Height <= 16384
                            && t.Levels >= 1 && t.Levels <= 16) ok++;
                    }
                    else
                    {
                        var ydd = new YddFile();
                        RpfFile.LoadResourceFile(ydd, w.Dane(), 165);
                        var d = ydd.Drawables?.FirstOrDefault();
                        var n = d?.DrawableModels?.High?.FirstOrDefault()?.Geometries?.FirstOrDefault()?.VertexBuffer?.VertexCount ?? 0;
                        if (n > 0 && n < 5_000_000) ok++;
                    }
                }
                catch { }
            }
            return ok;
        }

        int zg = Punkty(true), zl = Punkty(false);
        if (zg == 0 && zl == 0) log("[uwaga] nie rozpoznalam formatu zrodla — probuje jako gen9");
        return zg >= zl;
    }

    // ===================== pojedyncze pliki =====================

    static Pozycja Model(Wpis w, string paczka, bool gen9)
    {
        var n = Nazwy.Model(w.Nazwa);
        if (n == null) return null;
        string typ = n.Typ; int numer = n.Numer; bool props = n.Props; string sufiks = n.Sufiks;
        // kontener: folder/archiwum .rpf ma pierwszenstwo; przy luznych plikach FiveM bierzemy prefiks sprzed '^'
        string kontener = !string.IsNullOrEmpty(w.Kontener) ? w.Kontener : (n.Kontener ?? "");

        byte[] dane;
        try { dane = w.Dane(); } catch { return null; }

        var poz = new Pozycja
        {
            Id = $"{paczka}|{kontener}|{typ}|{numer}|{sufiks}",
            Paczka = paczka,
            Kontener = kontener,
            Typ = typ,
            Numer = numer,
            Sufiks = sufiks,
            Props = props,
            Gen9 = gen9,
            SciezkaYdd = w.Sciezka,
            BajtyYdd = dane.Length,
            ShaYdd = Convert.ToHexString(SHA256.HashData(dane))
        };
        try
        {
            var ydd = new YddFile();
            RpfFile.LoadResourceFile(ydd, dane, 165);
            poz.Geo = Odciski.Geometria(ydd.Drawables?.FirstOrDefault());
        }
        catch { poz.Geo = new Geo(); }
        return poz;
    }

    static Tekstura TeksturaZ(Wpis w, out string klucz, out string rasa)
    {
        klucz = null; rasa = "uni";
        var n = Nazwy.Tekstura(w.Nazwa);
        if (n == null) return null;
        string typ = n.Typ; int numer = n.Numer; bool props = n.Props; rasa = n.Rasa;
        string kontener = !string.IsNullOrEmpty(w.Kontener) ? w.Kontener : (n.Kontener ?? "");
        klucz = $"{kontener}|{typ}|{numer}|{props}";

        byte[] dane;
        try { dane = w.Dane(); } catch { return null; }
        var wy = new Tekstura { Plik = w.Nazwa, Sciezka = w.Sciezka, Bajty = dane.Length, Sha = Convert.ToHexString(SHA256.HashData(dane)) };
        try
        {
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, dane, 13);
            var t = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
            if (t != null) Odciski.Tekstura(t, wy);
        }
        catch { }
        return wy;
    }
}
