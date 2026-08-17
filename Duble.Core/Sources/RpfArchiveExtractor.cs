// RpfArchiveExtractor.cs — kopia zrodla z rozlozonymi archiwami: .rpf -> folder "<nazwa>.rpf\" z plikami na dysku.
//
// Po co: do archiwum .rpf nie piszemy (tylko czytamy), wiec Zastosuj nie moze przeniesc pliku, ktory w nim siedzi.
// Rozpakowana kopia to zwykly folder — mozna ja indeksowac jak dzis (kontener = folder o nazwie *.rpf), porzadkowac
// (Zastosuj/Cofnij), a potem spakowac z powrotem swoim narzedziem. Oryginal zostaje nietkniety.
//
// Zasoby (ydd/ytd/…) zapisujemy jako pliki RSC7: 16 B naglowka (wersja, flagi stron) + ladunek deflate — dokladnie
// tak, jak eksportuje CodeWalker/OpenIV, wiec czytaja to nasz indeksator, CodeWalker i FiveM (stream). Pliki binarne
// (.meta, .ymt, .xml…) ida jak wyciagniete. Zagniezdzone archiwa (dlc.rpf\x64\...\paczka.rpf) staja sie podfolderami.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CodeWalker.GameFiles;
using Duble.Core.Indexing;

namespace Duble.Core.Sources;

public static class RpfArchiveExtractor
{
    public sealed class Wynik
    {
        public string Folder { get; set; }
        public int Files { get; set; }
        public int Archiwa { get; set; }
        public long Bytes { get; set; }
        public List<string> Bledy { get; } = new();
    }

    /// <summary>Bajty pliku na dysku dla wpisu archiwum: zasob -> naglowek RSC7 + deflate; binarny -> bez zmian.</summary>
    public static byte[] PlikRsc7(RpfFileEntry wpis, byte[] dane)
    {
        if (dane == null) return null;
        return wpis is RpfResourceFileEntry re ? ResourceBuilder.AddResourceHeader(re, ResourceBuilder.Compress(dane)) : dane;
    }

    /// <summary>Rozpakowuje jedno archiwum (z zagniezdzonymi) do `folder` (folder = zawartosc korzenia archiwum).</summary>
    public static Wynik Archiwum(string rpf, string folder, Action<ProgressReport> postep = null, CancellationToken ct = default)
    {
        var wynik = new Wynik { Folder = folder };
        if (!File.Exists(rpf)) { wynik.Bledy.Add("brak archiwum: " + rpf); return wynik; }
        var plik = new RpfFile(rpf, Path.GetFileName(rpf));
        plik.ScanStructure(s => { }, m => wynik.Bledy.Add("[scan] " + m));
        Rozloz(plik, folder, wynik, postep, ct);
        return wynik;
    }

    /// <summary>Kopia zrodla-folderu do `folder`: zwykle pliki kopiowane, archiwa .rpf rozkladane do podfolderow o tej samej nazwie.
    /// Pomija kosz `_odrzucone`. Zrodlo-plik .rpf -> jak Archiwum.</summary>
    public static Wynik SourceName(string sciezka, string folder, Action<ProgressReport> postep = null, CancellationToken ct = default)
    {
        if (File.Exists(sciezka)) return Archiwum(sciezka, folder, postep, ct);
        var wynik = new Wynik { Folder = folder };
        if (!Directory.Exists(sciezka)) { wynik.Bledy.Add("brak folderu: " + sciezka); return wynik; }
        var pliki = Directory.EnumerateFiles(sciezka, "*", SearchOption.AllDirectories).Where(f => !BinFolder.Contains(sciezka, f)).ToList();
        int i = 0;
        foreach (var f in pliki)
        {
            ct.ThrowIfCancellationRequested();
            var wzgl = Path.GetRelativePath(sciezka, f);
            postep?.Invoke(new ProgressReport("rozpakuj", i++, pliki.Count, wzgl));
            var cel = Path.Combine(folder, wzgl);
            try
            {
                if (f.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                {
                    var w = Archiwum(f, cel, null, ct);
                    wynik.Files += w.Files; wynik.Archiwa += w.Archiwa; wynik.Bytes += w.Bytes; wynik.Bledy.AddRange(w.Bledy);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cel));
                    File.Copy(f, cel, true);
                    wynik.Files++; wynik.Bytes += new FileInfo(f).Length;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { wynik.Bledy.Add($"{wzgl}: {e.Message}"); }
        }
        postep?.Invoke(new ProgressReport("rozpakuj", pliki.Count, pliki.Count, null));
        return wynik;
    }

    static void Rozloz(RpfFile plik, string folder, Wynik wynik, Action<ProgressReport> postep, CancellationToken ct)
    {
        wynik.Archiwa++;
        // wpisy plikow tego archiwum i wszystkich zagniezdzonych; sciezka wzgledna = Path bez prefiksu korzenia
        var korzen = (plik.Path ?? "").ToLowerInvariant();
        var wszystkie = new List<(RpfFileEntry e, RpfFile f)>();
        void Zbierz(RpfFile f)
        {
            if (f.AllEntries != null)
                foreach (var e in f.AllEntries.OfType<RpfFileEntry>())
                {
                    if (e is RpfBinaryFileEntry && e.NameLower.EndsWith(".rpf")) continue;   // zagniezdzone archiwum: przez Children
                    wszystkie.Add((e, f));
                }
            if (f.Children != null) foreach (var c in f.Children) { wynik.Archiwa++; Zbierz(c); }
        }
        Zbierz(plik);
        int i = 0;
        foreach (var (e, f) in wszystkie)
        {
            ct.ThrowIfCancellationRequested();
            var sciezka = e.Path ?? e.Name;
            if (sciezka.StartsWith(korzen + "\\", StringComparison.OrdinalIgnoreCase)) sciezka = sciezka.Substring(korzen.Length + 1);
            else if (sciezka.Equals(korzen, StringComparison.OrdinalIgnoreCase)) sciezka = e.Name;
            postep?.Invoke(new ProgressReport("rozpakuj", i++, wszystkie.Count, sciezka));
            try
            {
                var dane = f.ExtractFile(e);
                if (dane == null) { wynik.Bledy.Add($"{sciezka}: nie udalo sie wyciagnac ({f.LastError})"); continue; }
                var bajty = PlikRsc7(e, dane);
                var cel = Path.Combine(folder, sciezka);
                Directory.CreateDirectory(Path.GetDirectoryName(cel));
                var tmp = cel + ".tmp";
                File.WriteAllBytes(tmp, bajty);
                File.Move(tmp, cel, true);
                wynik.Files++; wynik.Bytes += bajty.Length;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { wynik.Bledy.Add($"{sciezka}: {ex.Message}"); }
        }
        postep?.Invoke(new ProgressReport("rozpakuj", wszystkie.Count, wszystkie.Count, null));
    }
}
