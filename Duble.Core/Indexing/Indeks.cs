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
using Duble.Core.Fingerprints;
using Duble.Core.Formats;
using Duble.Core.Model;
using Duble.Core.Naming;

namespace Duble.Core.Indexing;

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
        public string Znacznik;     // rozmiar|data (przyrostowosc)
    }

    public static List<Pozycja> Zrodlo(string sciezka, string nazwaPaczki, Action<string> log)
        => Zrodlo(sciezka, nazwaPaczki, new OpcjeIndeksu { Log = log ?? (_ => { }) });

    public static List<Pozycja> Zrodlo(string sciezka, string nazwaPaczki, OpcjeIndeksu opcje)
    {
        opcje ??= new OpcjeIndeksu();
        var log = opcje.Log ?? (_ => { });
        sciezka = Path.GetFullPath(sciezka);
        opcje.Anuluj.ThrowIfCancellationRequested();
        var wpisy = Directory.Exists(sciezka) ? ZFolderu(sciezka) : ZArchiwum(sciezka, log);
        if (wpisy.Count == 0) { log($"[uwaga] {sciezka}: nie znalazlam zadnych .ydd/.ytd"); return new List<Pozycja>(); }

        var paczka = nazwaPaczki ?? Path.GetFileNameWithoutExtension(sciezka.TrimEnd(Path.DirectorySeparatorChar));

        // FORMAT: CodeWalker w trybie gen9 czyta oba formaty po naglowku RSC7 kazdego pliku (Format.cs),
        // wiec tryb ustawiamy raz i nie dotykamy; etykieta Legacy/Enhanced per pozycja z naglowka (Rsc7.Gen9).
        CodeWalkerRuntime.Initialize();

        // PRZYROSTOWOSC: pliki o tej samej sciezce i znaczniku (rozmiar|data) bierzemy z poprzedniego katalogu.
        var stareModele = new Dictionary<string, Pozycja>(StringComparer.OrdinalIgnoreCase);
        var stareTekstury = new Dictionary<string, Tekstura>(StringComparer.OrdinalIgnoreCase);
        if (opcje.Poprzedni != null && !opcje.Wymus)
            foreach (var p in opcje.Poprzedni.Pozycje)
            {
                if (p.SciezkaYdd != null && p.Znacznik != null) stareModele[p.SciezkaYdd] = p;
                foreach (var t in p.Tekstury) if (t.Sciezka != null && t.Znacznik != null) stareTekstury[t.Sciezka] = t;
            }
        int bezZmianModeli = 0, bezZmianTekstur = 0;
        int porcja = Math.Max(1, opcje.Porcja);

        // Pliki o nazwie spoza konwencji NIE moga zniknac po cichu — to zwykle wlasnie
        // one sa smieciem po eksporcie i kandydatem na duplikat. Zbieramy je i wypisujemy.
        var pominiete = new ConcurrentBag<string>();

        // --- modele ---
        var modele = new ConcurrentBag<Pozycja>();
        var pliki = wpisy.Where(w => w.Nazwa.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase)).ToList();
        int zrobione = 0;
        foreach (var kawalek in pliki.Chunk(porcja))
        {
            opcje.Anuluj.ThrowIfCancellationRequested();
            Parallel.ForEach(kawalek, w =>
            {
                Pozycja p;
                if (stareModele.TryGetValue(w.Sciezka, out var stary) && stary.Znacznik == w.Znacznik)
                { p = ModelBezCzytania(w, paczka, stary); Interlocked.Increment(ref bezZmianModeli); }
                else p = Model(w, paczka);
                if (p != null) modele.Add(p); else pominiete.Add(w.Nazwa);
                int n = Interlocked.Increment(ref zrobione);
                if (n % 200 == 0) log($"  modele: {n}/{pliki.Count}");
            });
            opcje.Postep?.Invoke(new Postep("modele", zrobione, pliki.Count, paczka));
        }

        // --- tekstury ---
        var teksturyWg = new ConcurrentDictionary<string, ConcurrentBag<(Tekstura t, string rasa)>>();
        var tpliki = wpisy.Where(w => w.Nazwa.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)).ToList();
        zrobione = 0;
        foreach (var kawalek in tpliki.Chunk(porcja))
        {
            opcje.Anuluj.ThrowIfCancellationRequested();
            Parallel.ForEach(kawalek, w =>
            {
                Tekstura wynik; string klucz = null, rasa = "uni";
                if (stareTekstury.TryGetValue(w.Sciezka, out var st) && st.Znacznik == w.Znacznik
                    && (opcje.FolderMiniatur == null || st.Sha == null || !st.Zdekodowana || File.Exists(Path.Combine(opcje.FolderMiniatur, st.Sha + ".png"))))
                {
                    var n = Nazwy.Tekstura(w.Nazwa);
                    if (n != null)
                    {
                        string kontener = !string.IsNullOrEmpty(w.Kontener) ? w.Kontener : (n.Kontener ?? "");
                        klucz = $"{kontener}|{n.Typ}|{n.Numer}|{n.Props}"; rasa = n.Rasa;
                        wynik = st; Interlocked.Increment(ref bezZmianTekstur);
                    }
                    else wynik = null;
                }
                else wynik = TeksturaZ(w, opcje, out klucz, out rasa);
                if (wynik != null) teksturyWg.GetOrAdd(klucz, _ => new ConcurrentBag<(Tekstura, string)>()).Add((wynik, rasa));
                else pominiete.Add(w.Nazwa);
                int nz = Interlocked.Increment(ref zrobione);
                if (nz % 500 == 0) log($"  tekstury: {nz}/{tpliki.Count}");
            });
            opcje.Postep?.Invoke(new Postep("tekstury", zrobione, tpliki.Count, paczka));
        }
        if (bezZmianModeli + bezZmianTekstur > 0)
            log($"  bez zmian (z poprzedniego katalogu): {bezZmianModeli} modeli, {bezZmianTekstur} tekstur");

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

    /// <summary>Nazwa folderu kosza (Zastosowanie) — indeksator go pomija, zeby odrzucone pliki nie wrocily jako paczka.</summary>
    public const string FolderOdrzuconych = "_odrzucone";

    /// <summary>true = sciezka pliku ma (miedzy korzeniem a nazwa) segment `_odrzucone`.</summary>
    public static bool WKoszu(string korzen, string plik)
    {
        var wzgl = Path.GetRelativePath(korzen, plik);
        var czesci = wzgl.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < czesci.Length - 1; i++)
            if (czesci[i].Equals(FolderOdrzuconych, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static List<Wpis> ZFolderu(string korzen)
    {
        var wy = new List<Wpis>();
        foreach (var f in Directory.EnumerateFiles(korzen, "*", SearchOption.AllDirectories))
        {
            if (WKoszu(korzen, f)) continue;
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
            wy.Add(new Wpis { Nazwa = Path.GetFileName(f), Kontener = kont, Sciezka = sciezka, Dlugosc = fi.Length, Dane = () => File.ReadAllBytes(sciezka),
                              Znacznik = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks });
        }
        return wy;
    }

    static List<Wpis> ZArchiwum(string plik, Action<string> log)
    {
        var wy = new List<Wpis>();
        if (!File.Exists(plik)) return wy;
        var rpfInfo = new FileInfo(plik);
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
                        Dane = () => Rsc7.Owin(e, wlasciciel.ExtractFile(e)),
                        Znacznik = e.GetFileSize() + "|" + rpfInfo.Length + "|" + rpfInfo.LastWriteTimeUtc.Ticks
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
            Znacznik = w.Znacznik,
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

    /// <summary>Pozycja z poprzedniego katalogu: nazwa/kontener liczone na nowo (tanie), odcisk kopiowany bez czytania pliku.</summary>
    static Pozycja ModelBezCzytania(Wpis w, string paczka, Pozycja stary)
    {
        var n = Nazwy.Model(w.Nazwa);
        if (n == null) return null;
        string kontener = !string.IsNullOrEmpty(w.Kontener) ? w.Kontener : (n.Kontener ?? "");
        return new Pozycja
        {
            Id = $"{paczka}|{kontener}|{n.Typ}|{n.Numer}|{n.Sufiks}", Paczka = paczka, Kontener = kontener,
            Typ = n.Typ, Numer = n.Numer, Sufiks = n.Sufiks, Props = n.Props, Gen9 = stary.Gen9,
            SciezkaYdd = w.Sciezka, Znacznik = w.Znacznik,
            BajtyYdd = stary.BajtyYdd, ShaYdd = stary.ShaYdd, Geo = stary.Geo,
        };
    }

    static void ZapiszMiniature(string folder, string sha, byte[] px, int w, int h)
    {
        if (string.IsNullOrEmpty(folder) || sha == null) return;
        var plik = Path.Combine(folder, sha + ".png");
        if (File.Exists(plik)) return;
        Directory.CreateDirectory(folder);
        var rgb = Odciski.MiniaturaZPikseli(px, w, h, 128);
        try { File.WriteAllBytes(plik, Png.Rgb(rgb, 128, 128)); }
        catch (IOException) { /* wyscig dwoch watkow o ten sam sha — drugi przegrywa, plik i tak jest */ }
    }

    static Tekstura TeksturaZ(Wpis w, OpcjeIndeksu opcje, out string klucz, out string rasa)
    {
        klucz = null; rasa = "uni";
        var n = Nazwy.Tekstura(w.Nazwa);
        if (n == null) return null;
        string typ = n.Typ; int numer = n.Numer; bool props = n.Props; rasa = n.Rasa;
        string kontener = !string.IsNullOrEmpty(w.Kontener) ? w.Kontener : (n.Kontener ?? "");
        klucz = $"{kontener}|{typ}|{numer}|{props}";

        byte[] dane;
        try { dane = w.Dane(); } catch { return null; }
        var wy = new Tekstura { Plik = w.Nazwa, Sciezka = w.Sciezka, Bajty = dane.Length, Sha = Convert.ToHexString(SHA256.HashData(dane)), Znacznik = w.Znacznik };
        try
        {
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, dane, 13);
            var t = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
            if (t != null) Odciski.Tekstura(t, wy, 128, (px, pw, ph) => ZapiszMiniature(opcje.FolderMiniatur, wy.Sha, px, pw, ph));
        }
        catch { }
        return wy;
    }
}
