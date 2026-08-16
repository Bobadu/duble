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

        // FORMAT: CodeWalker w trybie gen9 czyta oba formaty po naglowku RSC7 kazdego pliku (Format.cs),
        // wiec tryb ustawiamy raz i nie dotykamy; etykieta Legacy/Enhanced per pozycja z naglowka (Rsc7.Gen9).
        Format.Przygotuj();

        // Pliki o nazwie spoza konwencji NIE moga zniknac po cichu — to zwykle wlasnie
        // one sa smieciem po eksporcie i kandydatem na duplikat. Zbieramy je i wypisujemy.
        var pominiete = new ConcurrentBag<string>();

        // --- modele ---
        var modele = new ConcurrentBag<Pozycja>();
        var pliki = wpisy.Where(w => w.Nazwa.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase)).ToList();
        int zrobione = 0;
        Parallel.ForEach(pliki, w =>
        {
            var p = Model(w, paczka);
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

        // format per kontener (folder z wieloma .rpf moze mieszac Legacy i gen9)
        foreach (var k in lista.GroupBy(p => p.Kontener).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            bool sa9 = k.Any(p => p.Gen9), saL = k.Any(p => !p.Gen9);
            log($"  {(string.IsNullOrEmpty(k.Key) ? "(pliki luzem)" : k.Key)}: {(sa9 && saL ? "MIESZANY" : sa9 ? "gen9 (Enhanced)" : "legacy")}, pozycji {k.Count()}");
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
            if (ext.Equals(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                // prawdziwe archiwum lezace w folderze (np. stream\civil01_female.rpf, dlcpacks\x\dlc.rpf)
                wy.AddRange(ZArchiwum(f, _ => { }));
                continue;
            }
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
                        // ExtractFile oddaje zasob bez naglowka RSC7 — doklejamy go (Rsc7.Owin), zeby LoadResourceFile czytal poprawnie
                        Dane = () => Rsc7.Owin(e, wlasciciel.ExtractFile(e))
                    });
                }
            if (f.Children != null) foreach (var c in f.Children) Chodz(c);
        }
        Chodz(rpf);
        return wy;
    }

    // ===================== pojedyncze pliki =====================

    static Pozycja Model(Wpis w, string paczka)
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
            Gen9 = Rsc7.Gen9(dane, ".ydd") ?? false,   // etykieta formatu z naglowka pliku
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
