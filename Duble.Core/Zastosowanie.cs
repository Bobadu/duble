// Zastosowanie.cs — wykonanie decyzji i cofniecie.
//
// ZASADA PROJEKTU: oryginal nigdy nie ginie. Odrzucone pliki NIE sa kasowane, tylko
// przenoszone do `staging\wardrobe2\_odrzucone\` z zachowaniem struktury, a lista
// przeniesien ladzie w `cofnij.json` — jedno polecenie i wszystko wraca.
//
// PULAPKA, KTORA TO OBSLUGUJE: dwie pozycje o tym samym typie i numerze (np. feet_050
// i feet_050_1, gdzie ogonek dolozył eksporter) WSPOLDZIELA te same pliki tekstur.
// Przeniesienie "wszystkich plikow przegranego" okradloby wtedy zwyciezce. Dlatego
// przed przeniesieniem sprawdzamy, czy plik nie jest uzywany przez pozycje, ktora zostaje.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Duble;

public class Przeniesienie
{
    public string Z { get; set; }
    public string Do { get; set; }
}

public class Cofka
{
    public string Kiedy { get; set; }
    public List<Przeniesienie> Ruchy { get; set; } = new();
}

public static class Zastosowanie
{
    /// <summary>Wypisuje liste decyzji do pliku TSV, ktory mozna recznie poprawic.</summary>
    public static void ZapiszDecyzje(WynikPorownania wynik, Katalog katalog, string sciezka)
    {
        var wgId = katalog.Pozycje.ToDictionary(p => p.Id);
        var sb = new StringBuilder();
        sb.AppendLine("# Lista pozycji, ktore `duble zastosuj` przeniesie do _odrzucone\\.");
        sb.AppendLine("# Zmien TAK na NIE w pierwszej kolumnie przy tych, ktore chcesz zachowac.");
        sb.AppendLine("# Kolumny rozdzielone TABEM. Linie z # sa pomijane.");
        sb.AppendLine("odrzucic\twerdykt\tpozycja\tzostaje_zamiast\tpowod");
        foreach (var g in wynik.Grupy.Where(g => g.Werdykt == Porownanie.Duplikat || g.Werdykt == Porownanie.Nadzbior))
            foreach (var id in g.Pozycje.Where(x => x != g.Zwyciezca))
            {
                var powod = Teksty.Powod(g.Pary.FirstOrDefault()?.Powod ?? g.Powod, "pl");
                sb.AppendLine($"TAK\t{g.Werdykt}\t{id}\t{g.Zwyciezca}\t{powod.Replace('\t', ' ')}");
            }
        var kat = Path.GetDirectoryName(Path.GetFullPath(sciezka));
        if (!string.IsNullOrEmpty(kat)) Directory.CreateDirectory(kat);
        File.WriteAllText(sciezka, sb.ToString(), Encoding.UTF8);
    }

    public static int Zastosuj(Katalog katalog, string decyzje, string korzenOdrzuconych, string plikCofki, Action<string> log)
    {
        if (!File.Exists(decyzje)) { log($"[blad] brak pliku decyzji: {decyzje} — najpierw `duble porownaj`"); return 1; }

        // Katalog trzyma BEZWZGLEDNE sciezki. Po przeniesieniu projektu na druga maszyne
        // (inna litera dysku) sa nieaktualne — bez tej zapory `zastosuj` po cichu nie
        // przeniosl by niczego i wygladaloby to na sukces. Same decyzje przezywaja
        // przenosiny, bo odwoluja sie do Id pozycji, nie do sciezek.
        var martwe = katalog.Zrodla.Where(z => !Directory.Exists(z.Value) && !File.Exists(z.Value)).ToList();
        if (martwe.Count > 0)
        {
            log("[blad] katalog wskazuje na zrodla, ktorych nie ma na tym dysku:");
            foreach (var m in martwe) log($"    {m.Key} -> {m.Value}");
            log("Uruchom `duble.ps1` (przeindeksuje pod aktualne sciezki) i dopiero potem -Zastosuj.");
            return 1;
        }

        var wgId = katalog.Pozycje.ToDictionary(p => p.Id);

        var doOdrzucenia = new List<string>();
        foreach (var linia in File.ReadAllLines(decyzje))
        {
            if (string.IsNullOrWhiteSpace(linia) || linia.StartsWith("#")) continue;
            var pola = linia.Split('\t');
            if (pola.Length < 3 || pola[0].Equals("odrzucic", StringComparison.OrdinalIgnoreCase)) continue;
            if (!pola[0].Trim().Equals("TAK", StringComparison.OrdinalIgnoreCase)) continue;
            if (!wgId.ContainsKey(pola[2])) { log($"[uwaga] nieznana pozycja w decyzjach: {pola[2]}"); continue; }
            doOdrzucenia.Add(pola[2]);
        }
        if (doOdrzucenia.Count == 0) { log("nic do zrobienia — zadna linia nie ma TAK"); return 0; }
        log($"pozycji do odrzucenia: {doOdrzucenia.Count}");

        // Pliki uzywane przez pozycje, KTORE ZOSTAJA. Wszystko z tej listy jest nietykalne.
        var odrzucane = new HashSet<string>(doOdrzucenia);
        var chronione = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in katalog.Pozycje.Where(p => !odrzucane.Contains(p.Id)))
        {
            if (p.SciezkaYdd != null) chronione.Add(p.SciezkaYdd);
            foreach (var t in p.Tekstury) if (t.Sciezka != null) chronione.Add(t.Sciezka);
        }

        var cofka = new Cofka { Kiedy = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        int przeniesione = 0, wspoldzielone = 0, wArchiwum = 0, brakujace = 0;

        foreach (var id in doOdrzucenia)
        {
            var p = wgId[id];
            var pliki = new List<string>();
            if (p.SciezkaYdd != null) pliki.Add(p.SciezkaYdd);
            pliki.AddRange(p.Tekstury.Where(t => t.Sciezka != null).Select(t => t.Sciezka));

            foreach (var zrodlo in pliki.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (zrodlo.Contains('|'))
                {
                    // plik siedzi w archiwum .rpf — nie ruszamy zawartosci archiwow
                    wArchiwum++;
                    continue;
                }
                if (chronione.Contains(zrodlo))
                {
                    // ten sam plik obsluguje pozycje, ktora zostaje (np. feet_050 i feet_050_1)
                    wspoldzielone++;
                    continue;
                }
                if (!File.Exists(zrodlo)) { brakujace++; continue; }

                var cel = Path.Combine(korzenOdrzuconych, p.Paczka, ZKorzenia(zrodlo, p.Paczka));
                Directory.CreateDirectory(Path.GetDirectoryName(cel));
                if (File.Exists(cel)) File.Delete(cel);
                File.Move(zrodlo, cel);
                cofka.Ruchy.Add(new Przeniesienie { Z = zrodlo, Do = cel });
                przeniesione++;
            }
            log($"  odrzucone: {p.Opis}{p.Sufiks}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(plikCofki)));
        File.WriteAllText(plikCofki, JsonSerializer.Serialize(cofka, new JsonSerializerOptions { WriteIndented = true }));

        log("");
        log($"przeniesionych plikow : {przeniesione}");
        if (wspoldzielone > 0) log($"pominietych (wspoldzielone z pozycja, ktora zostaje): {wspoldzielone}");
        if (wArchiwum > 0) log($"pominietych (w archiwum .rpf — rozpakuj paczke, zeby ruszyc): {wArchiwum}");
        if (brakujace > 0) log($"pominietych (brak pliku na dysku): {brakujace}");
        log($"cofka: {plikCofki}   — `duble cofnij` przywraca wszystko");
        return 0;
    }

    /// <summary>Sciezka wzgledem folderu paczki, zeby w _odrzucone zachowac uklad kontenerow.</summary>
    static string ZKorzenia(string pelna, string paczka)
    {
        var czesci = pelna.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int i = Array.FindLastIndex(czesci, c => c.Equals(paczka, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i < czesci.Length - 1
            ? string.Join(Path.DirectorySeparatorChar, czesci.Skip(i + 1))
            : Path.GetFileName(pelna);
    }

    public static int Cofnij(string plikCofki, Action<string> log)
    {
        if (!File.Exists(plikCofki)) { log($"[blad] brak pliku cofki: {plikCofki}"); return 1; }
        var cofka = JsonSerializer.Deserialize<Cofka>(File.ReadAllText(plikCofki));
        int wrocilo = 0, potkniecia = 0;
        foreach (var r in cofka.Ruchy)
        {
            if (!File.Exists(r.Do)) { potkniecia++; continue; }
            if (File.Exists(r.Z)) { log($"[uwaga] cel juz istnieje, pomijam: {r.Z}"); potkniecia++; continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(r.Z));
            File.Move(r.Do, r.Z);
            wrocilo++;
        }
        log($"przywrocono {wrocilo} plikow" + (potkniecia > 0 ? $", pominieto {potkniecia}" : ""));
        log("katalog jest teraz nieaktualny — uruchom `duble.ps1` zeby przeindeksowac");
        return 0;
    }
}
