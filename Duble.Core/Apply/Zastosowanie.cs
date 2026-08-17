// Zastosowanie.cs — wykonanie decyzji (przeniesienie odrzuconych plikow do kosza) i cofniecie.
//
// ZASADA PROJEKTU: oryginal nigdy nie ginie. Odrzucone pliki NIE sa kasowane, tylko
// przenoszone do kosza (`_odrzucone` obok zrodla albo wskazany folder) z zachowaniem
// struktury wzgledem zrodla, a lista przeniesien laduje w Cofce (JSON) — jedno polecenie
// i wszystko wraca (calosc albo pojedyncza pozycja).
//
// PULAPKA, KTORA TO OBSLUGUJE: dwie pozycje o tym samym typie i numerze (np. feet_050
// i feet_050_1, gdzie ogonek dolozył eksporter) WSPOLDZIELA te same pliki tekstur.
// Przeniesienie "wszystkich plikow przegranego" okradloby wtedy zwyciezce. Dlatego
// przed przeniesieniem sprawdzamy, czy plik nie jest uzywany przez pozycje, ktora zostaje.
//
// Przebieg: Zaplanuj (co, dokad, co pomijamy i dlaczego) -> uzytkownik ogląda plan -> Wykonaj
// (ruchy + Cofka) -> Cofka.Zapisz. Cofnij(cofka, tylkoPozycje) przywraca.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Duble.Core.Comparison;
using Duble.Core.Indexing;
using Duble.Core.Model;

namespace Duble.Core.Apply;

/// <summary>Jeden plik pozycji w planie: dokad pojdzie i czy w ogole (Stan).</summary>
public sealed class RuchPliku
{
    public const string Przenies = "przenies";
    public const string Wspoldzielony = "wspoldzielony";   // uzywa go pozycja, ktora zostaje
    public const string WArchiwum = "wArchiwum";           // siedzi w .rpf — nie ruszamy archiwow
    public const string Brak = "brak";                     // nie ma go na dysku / brak zrodla

    public string GarmentId { get; set; }
    public string Z { get; set; }
    public string Do { get; set; }
    public long Bajty { get; set; }
    public string Stan { get; set; }
}

public sealed class PozycjaPlanu
{
    public string Id { get; set; }
    public string Nazwa { get; set; }        // typ_NNN
    public string Sufiks { get; set; }
    public string Zrodlo { get; set; }
    public string ZrodloId { get; set; }
    public string Kontener { get; set; }
    public string Kosz { get; set; }
    public List<RuchPliku> Pliki { get; set; } = new();
    public int DoPrzeniesienia => Pliki.Count(r => r.Stan == RuchPliku.Przenies);
    public long Bajty => Pliki.Where(r => r.Stan == RuchPliku.Przenies).Sum(r => r.Bajty);
    public int Wspoldzielone => Pliki.Count(r => r.Stan == RuchPliku.Wspoldzielony);
    public int WArchiwum => Pliki.Count(r => r.Stan == RuchPliku.WArchiwum);
    public int Brakujace => Pliki.Count(r => r.Stan == RuchPliku.Brak);
}

/// <summary>Skad liczyc sciezke wzgledna pozycji (Korzen zrodla) i dokad ja przeniesc (Kosz).</summary>
public sealed class CelPozycji
{
    public string Korzen { get; set; }
    public string Kosz { get; set; }
    public string Zrodlo { get; set; }
    public string ZrodloId { get; set; }
}

public sealed class PlanZastosowania
{
    public List<PozycjaPlanu> Pozycje { get; } = new();
    /// <summary>Nazwy zrodel, ktorych nie ma na dysku (pozycje z nich maja pliki w stanie Brak).</summary>
    public List<string> BrakujaceZrodla { get; } = new();
    public int Pliki => Pozycje.Sum(p => p.DoPrzeniesienia);
    public long Bajty => Pozycje.Sum(p => p.Bajty);
    public int Wspoldzielone => Pozycje.Sum(p => p.Wspoldzielone);
    public int WArchiwum => Pozycje.Sum(p => p.WArchiwum);
    public int Brakujace => Pozycje.Sum(p => p.Brakujace);

    public IEnumerable<(string kosz, int pliki, long bajty)> Kosze()
        => Pozycje.Where(p => p.Kosz != null).GroupBy(p => p.Kosz, StringComparer.OrdinalIgnoreCase)
                  .Select(g => (g.Key, g.Sum(p => p.DoPrzeniesienia), g.Sum(p => p.Bajty)))
                  .Where(x => x.Item2 > 0);
}

public sealed class Przeniesienie
{
    public string Z { get; set; }
    public string Do { get; set; }
    public string GarmentId { get; set; }
    public long Bajty { get; set; }
    public bool Cofniety { get; set; }
}

public sealed class PozycjaCofki
{
    public string Id { get; set; }
    public string Nazwa { get; set; }
    public string Zrodlo { get; set; }
    public string ZrodloId { get; set; }
    public string Kosz { get; set; }
    public int Pliki { get; set; }
}

/// <summary>Dziennik jednego zastosowania: co przeniesiono i skad — wystarcza, zeby wszystko wrocilo.</summary>
public sealed class Cofka
{
    public string Kiedy { get; set; }
    public string Opis { get; set; }
    public List<Przeniesienie> Ruchy { get; set; } = new();
    public List<PozycjaCofki> Pozycje { get; set; } = new();
    public int Wspoldzielone { get; set; }
    public int WArchiwum { get; set; }
    public int Brakujace { get; set; }
    /// <summary>true = Wykonaj przerwano (anulowanie/blad) — Ruchy zawieraja tylko to, co zdazylo sie przeniesc.</summary>
    public bool Przerwano { get; set; }
    public string Blad { get; set; }
    /// <summary>Kiedy cofnieto WSZYSTKIE ruchy (null = jeszcze nie / czesciowo).</summary>
    public string Cofnieto { get; set; }

    [JsonIgnore] public long Bajty => Ruchy.Sum(r => r.Bajty);
    [JsonIgnore] public bool CzesciowoCofnieta => Cofnieto == null && Ruchy.Any(r => r.Cofniety);
    /// <summary>Jest co cofac: ruch niecofniety, plik nadal w koszu, a miejsce zrodlowe wolne.</summary>
    [JsonIgnore] public bool MoznaCofnac => Ruchy.Any(MoznaCofnacRuch);
    public static bool MoznaCofnacRuch(Przeniesienie r) => !r.Cofniety && File.Exists(r.Do) && !File.Exists(r.Z);
    public bool MoznaCofnacPozycje(string id) => Ruchy.Any(r => r.GarmentId == id && MoznaCofnacRuch(r));

    static readonly JsonSerializerOptions Opcje = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public void Zapisz(string plik)
    {
        var kat = Path.GetDirectoryName(Path.GetFullPath(plik));
        if (!string.IsNullOrEmpty(kat)) Directory.CreateDirectory(kat);
        var tmp = plik + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opcje));
        File.Move(tmp, plik, true);
    }
    public static Cofka Wczytaj(string plik)
    {
        var c = JsonSerializer.Deserialize<Cofka>(File.ReadAllText(plik), Opcje) ?? new Cofka();
        c.Ruchy ??= new(); c.Pozycje ??= new();
        return c;
    }
}

public static class Zastosowanie
{
    /// <summary>Wypisuje liste decyzji do pliku TSV, ktory mozna recznie poprawic (CLI).</summary>
    public static void ZapiszDecyzje(WynikPorownania wynik, Catalog katalog, string sciezka)
    {
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

    // ===================== plan =====================

    /// <summary>Plan przeniesien dla odrzuconych pozycji. `cel(p)` mowi, skad liczyc sciezke wzgledna i dokad przeniesc
    /// (null = zrodla nie ma na dysku -> pliki pozycji w stanie Brak). Pliki uzywane przez pozycje, ktore zostaja,
    /// dostaja stan Wspoldzielony; wpisy z archiwow (sciezka z '|') — WArchiwum.</summary>
    public static PlanZastosowania Zaplanuj(Catalog katalog, IEnumerable<string> odrzucone, Func<Garment, CelPozycji> cel)
    {
        var plan = new PlanZastosowania();
        var wgId = katalog.Garments.ToDictionary(p => p.Id);
        var odrzucane = new HashSet<string>(odrzucone.Where(wgId.ContainsKey));
        if (odrzucane.Count == 0) return plan;

        // Pliki uzywane przez pozycje, KTORE ZOSTAJA. Wszystko z tej listy jest nietykalne.
        var chronione = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in katalog.Garments.Where(p => !odrzucane.Contains(p.Id)))
        {
            if (p.ModelPath != null) chronione.Add(p.ModelPath);
            foreach (var t in p.Textures) if (t.Path != null) chronione.Add(t.Path);
        }

        // ten sam plik moze byc w dwoch odrzucanych pozycjach (feet_050 i feet_050_1 obie odrzucone) — przenosimy raz
        var zaplanowane = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var brakZrodel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kolejnosc = odrzucane.Select(id => wgId[id]).OrderBy(p => p.PackName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Slot, StringComparer.Ordinal).ThenBy(p => p.Number).ThenBy(p => p.Suffix, StringComparer.Ordinal);
        foreach (var p in kolejnosc)
        {
            var c = cel?.Invoke(p);
            var pp = new PozycjaPlanu
            {
                Id = p.Id, Nazwa = $"{p.Slot}_{p.Number:d3}", Sufiks = p.Suffix, Kontener = p.Container,
                Zrodlo = c?.Zrodlo ?? p.PackName, ZrodloId = c?.ZrodloId ?? p.SourceId, Kosz = c?.Kosz,
            };
            if (c == null) brakZrodel.Add(pp.Zrodlo);

            var pliki = new List<(string sciezka, long bajty)>();
            if (p.ModelPath != null) pliki.Add((p.ModelPath, p.ModelSize));
            pliki.AddRange(p.Textures.Where(t => t.Path != null).Select(t => (t.Path, t.Size)));
            foreach (var (sciezka, bajty) in pliki.DistinctBy(x => x.sciezka, StringComparer.OrdinalIgnoreCase))
            {
                var r = new RuchPliku { GarmentId = p.Id, Z = sciezka, Bajty = bajty };
                if (sciezka.Contains('|')) r.Stan = RuchPliku.WArchiwum;
                else if (chronione.Contains(sciezka)) r.Stan = RuchPliku.Wspoldzielony;
                else if (c == null || !File.Exists(sciezka)) r.Stan = RuchPliku.Brak;
                else if (!zaplanowane.Add(sciezka)) continue;   // juz w planie innej odrzucanej pozycji
                else { r.Stan = RuchPliku.Przenies; r.Do = Path.Combine(c.Kosz, Wzgledna(c.Korzen, sciezka)); }
                pp.Pliki.Add(r);
            }
            plan.Pozycje.Add(pp);
        }
        plan.BrakujaceZrodla.AddRange(brakZrodel.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        return plan;
    }

    /// <summary>Sciezka wzgledem korzenia zrodla (zachowujemy uklad kontenerow w koszu); plik spoza korzenia -> sama nazwa.</summary>
    public static string Wzgledna(string korzen, string plik)
    {
        if (string.IsNullOrEmpty(korzen)) return Path.GetFileName(plik);
        try
        {
            var pelnyKorzen = Path.GetFullPath(korzen).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pelny = Path.GetFullPath(plik);
            if (File.Exists(pelnyKorzen)) pelnyKorzen = Path.GetDirectoryName(pelnyKorzen);   // korzen to plik (archiwum) — liczymy od jego folderu
            var wzgl = Path.GetRelativePath(pelnyKorzen, pelny);
            if (wzgl.StartsWith("..") || Path.IsPathRooted(wzgl)) return Path.GetFileName(plik);
            return wzgl;
        }
        catch { return Path.GetFileName(plik); }
    }

    // ===================== wykonanie =====================

    /// <summary>Przenosi pliki w stanie Przenies. Nie rzuca przy anulowaniu — konczy petle i ustawia Przerwano, zeby wolajacy
    /// ZAWSZE mogl zapisac cofke z tym, co juz sie przenioslo. Wyjatek IO przy pojedynczym pliku tez przerywa (Blad).</summary>
    public static Cofka Wykonaj(PlanZastosowania plan, string opis, Action<Postep> postep = null, CancellationToken ct = default)
    {
        var cofka = new Cofka
        {
            Kiedy = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Opis = opis,
            Wspoldzielone = plan.Wspoldzielone, WArchiwum = plan.WArchiwum, Brakujace = plan.Brakujace,
        };
        var ruchy = plan.Pozycje.SelectMany(p => p.Pliki.Where(r => r.Stan == RuchPliku.Przenies).Select(r => (p, r))).ToList();
        int n = ruchy.Count, i = 0;
        var pozycje = new Dictionary<string, PozycjaCofki>();
        foreach (var (p, r) in ruchy)
        {
            postep?.Invoke(new Postep("zastosuj", i, n, p.Nazwa));
            if (ct.IsCancellationRequested) { cofka.Przerwano = true; break; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(r.Do));
                if (File.Exists(r.Do)) File.Delete(r.Do);   // stary odrzut o tej samej nazwie — nadpisujemy (to kosz)
                File.Move(r.Z, r.Do);
            }
            catch (Exception e) { cofka.Przerwano = true; cofka.Blad = $"{r.Z}: {e.Message}"; break; }
            cofka.Ruchy.Add(new Przeniesienie { Z = r.Z, Do = r.Do, GarmentId = p.Id, Bajty = r.Bajty });
            if (!pozycje.TryGetValue(p.Id, out var pc))
                pozycje[p.Id] = pc = new PozycjaCofki { Id = p.Id, Nazwa = p.Nazwa + (string.IsNullOrEmpty(p.Sufiks) ? "" : " " + p.Sufiks), Zrodlo = p.Zrodlo, ZrodloId = p.ZrodloId, Kosz = p.Kosz };
            pc.Pliki++;
            i++;
        }
        postep?.Invoke(new Postep("zastosuj", i, n, null));
        cofka.Pozycje = pozycje.Values.ToList();
        return cofka;
    }

    // ===================== cofniecie =====================

    /// <summary>Przywraca pliki (wszystkie albo tylko podanych pozycji). Ruch pominiety, gdy pliku nie ma juz w koszu albo
    /// miejsce zrodlowe jest zajete. Oznacza Cofniety; gdy nie zostal zaden niecofniety ruch — ustawia Cofnieto.</summary>
    public static (int wrocilo, int pominieto) Cofnij(Cofka cofka, IEnumerable<string> tylkoPozycje = null, Action<Postep> postep = null)
    {
        var tylko = tylkoPozycje == null ? null : new HashSet<string>(tylkoPozycje);
        var doCofniecia = cofka.Ruchy.Where(r => !r.Cofniety && (tylko == null || tylko.Contains(r.GarmentId))).ToList();
        int wrocilo = 0, pominieto = 0, i = 0;
        foreach (var r in doCofniecia)
        {
            postep?.Invoke(new Postep("cofnij", i++, doCofniecia.Count, Path.GetFileName(r.Z)));
            if (!File.Exists(r.Do))
            {
                // pliku nie ma w koszu, a jest na starym miejscu = w praktyce juz cofniety (np. cofka nie zdazyla sie zapisac) — uznajemy
                if (File.Exists(r.Z)) { r.Cofniety = true; } else pominieto++;
                continue;
            }
            if (File.Exists(r.Z)) { pominieto++; continue; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(r.Z));
                File.Move(r.Do, r.Z);
                r.Cofniety = true; wrocilo++;
                UsunPusteFoldery(Path.GetDirectoryName(r.Do));
            }
            catch { pominieto++; }
        }
        if (cofka.Ruchy.All(r => r.Cofniety) && cofka.Ruchy.Count > 0) cofka.Cofnieto = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return (wrocilo, pominieto);
    }

    /// <summary>Po cofnieciu sprzatamy puste foldery kosza (do 8 poziomow w gore) — zeby po Cofnij nie zostawal szkielet _odrzucone.</summary>
    static void UsunPusteFoldery(string folder)
    {
        for (int k = 0; k < 8 && !string.IsNullOrEmpty(folder); k++)
        {
            try
            {
                if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any()) return;
                Directory.Delete(folder);
            }
            catch { return; }
            folder = Path.GetDirectoryName(folder);
        }
    }

    // ===================== CLI (plik decyzji TSV + jeden korzen kosza) =====================

    public static int Zastosuj(Catalog katalog, string decyzje, string korzenOdrzuconych, string plikCofki, Action<string> log)
    {
        if (!File.Exists(decyzje)) { log($"[blad] brak pliku decyzji: {decyzje} — najpierw `duble porownaj`"); return 1; }

        // The catalog holds ABSOLUTE paths. Move a project to another machine (a different drive letter)
        // and they are stale — without this guard `apply` would quietly move nothing and look like a success.
        // The decisions themselves survive the move, because they refer to garment ids, not to paths.
        var martwe = katalog.Sources.Where(z => !Directory.Exists(z.Value) && !File.Exists(z.Value)).ToList();
        if (martwe.Count > 0)
        {
            log("[blad] katalog wskazuje na zrodla, ktorych nie ma na tym dysku:");
            foreach (var m in martwe) log($"    {m.Key} -> {m.Value}");
            log("Uruchom `duble.ps1` (przeindeksuje pod aktualne sciezki) i dopiero potem -Zastosuj.");
            return 1;
        }

        var wgId = katalog.Garments.ToDictionary(p => p.Id);
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

        var plan = Zaplanuj(katalog, doOdrzucenia, p => new CelPozycji
        {
            Korzen = katalog.Sources.TryGetValue(p.PackName, out var k) ? k : null,
            Kosz = Path.Combine(korzenOdrzuconych, p.PackName), Zrodlo = p.PackName,
        });
        var cofka = Wykonaj(plan, "duble zastosuj");
        foreach (var p in cofka.Pozycje) log($"  odrzucone: {p.Zrodlo} / {p.Nazwa}");
        cofka.Zapisz(plikCofki);

        log("");
        log($"przeniesionych plikow : {cofka.Ruchy.Count}");
        if (cofka.Wspoldzielone > 0) log($"pominietych (wspoldzielone z pozycja, ktora zostaje): {cofka.Wspoldzielone}");
        if (cofka.WArchiwum > 0) log($"pominietych (w archiwum .rpf — rozpakuj paczke, zeby ruszyc): {cofka.WArchiwum}");
        if (cofka.Brakujace > 0) log($"pominietych (brak pliku na dysku): {cofka.Brakujace}");
        if (cofka.Przerwano) log($"[blad] przerwano: {cofka.Blad}");
        log($"cofka: {plikCofki}   — `duble cofnij` przywraca wszystko");
        return cofka.Przerwano ? 1 : 0;
    }

    public static int Cofnij(string plikCofki, Action<string> log)
    {
        if (!File.Exists(plikCofki)) { log($"[blad] brak pliku cofki: {plikCofki}"); return 1; }
        var cofka = Cofka.Wczytaj(plikCofki);
        var (wrocilo, potkniecia) = Cofnij(cofka);
        cofka.Zapisz(plikCofki);
        log($"przywrocono {wrocilo} plikow" + (potkniecia > 0 ? $", pominieto {potkniecia}" : ""));
        log("katalog jest teraz nieaktualny — uruchom `duble.ps1` zeby przeindeksowac");
        return 0;
    }
}
