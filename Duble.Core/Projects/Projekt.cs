// Projekt.cs — plik projektu aplikacji (*.duble): zestaw zrodel + decyzje + ustawienia. Obok lezy <plik>.cache\
// (katalog odciskow, miniatury, tekstury, siatki, historia zastosowan) — odtwarzalny, mozna skasowac.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duble.Core;

public class ZrodloProjektu
{
    public string Id { get; set; }
    public string Sciezka { get; set; }
    public string Nazwa { get; set; }
    public bool Wlaczone { get; set; } = true;
    public string Typ { get; set; }          // folder | rpf | fivem
    public string Format { get; set; }       // legacy | gen9 | mieszany | null (nieznany)
    public string Zaindeksowano { get; set; }
}

public class Decyzja
{
    public string Zwyciezca { get; set; }
    public List<string> Odrzucone { get; set; } = new();
    public bool Ignoruj { get; set; }
    public string Notatka { get; set; }
}

public class UstawieniaProjektu
{
    public string Kosz { get; set; }         // null = _odrzucone obok zrodla
    public Progi Progi { get; set; }         // null = domyslne
}

public class Projekt
{
    public int Wersja { get; set; } = 1;
    public string Nazwa { get; set; }
    public string Utworzony { get; set; }
    public List<ZrodloProjektu> Zrodla { get; set; } = new();
    public Dictionary<string, Decyzja> Decyzje { get; set; } = new();
    public UstawieniaProjektu Ustawienia { get; set; } = new();

    [JsonIgnore] public string Sciezka { get; set; }
    [JsonIgnore] public string FolderCache => Sciezka + ".cache";
    [JsonIgnore] public string PlikKatalogu => Path.Combine(FolderCache, "katalog.json");
    [JsonIgnore] public string PlikDubli => Path.Combine(FolderCache, "duble.json");
    [JsonIgnore] public string FolderMiniatur => Path.Combine(FolderCache, "thumbs");
    [JsonIgnore] public string FolderTekstur => Path.Combine(FolderCache, "tex");
    [JsonIgnore] public string FolderSiatek => Path.Combine(FolderCache, "mesh");
    [JsonIgnore] public string FolderHistorii => Path.Combine(FolderCache, "historia");

    static readonly JsonSerializerOptions Opcje = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static Projekt Nowy(string nazwa, string sciezka)
        => new Projekt { Nazwa = nazwa, Sciezka = Path.GetFullPath(sciezka), Utworzony = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };

    public static Projekt Wczytaj(string sciezka)
    {
        var p = JsonSerializer.Deserialize<Projekt>(File.ReadAllText(sciezka), Opcje) ?? new Projekt();
        p.Sciezka = Path.GetFullPath(sciezka);
        p.Zrodla ??= new(); p.Decyzje ??= new(); p.Ustawienia ??= new();
        return p;
    }

    public void Zapisz()
    {
        var kat = Path.GetDirectoryName(Sciezka);
        if (!string.IsNullOrEmpty(kat)) Directory.CreateDirectory(kat);
        File.WriteAllText(Sciezka, JsonSerializer.Serialize(this, Opcje));
    }

    /// <summary>Dodaje zrodlo (albo zwraca juz istniejace o tej samej sciezce). Nazwa = nazwa folderu/pliku,
    /// unikalna w projekcie (kolejne "stream" dostaja " (2)", " (3)"…) — bo Katalog grupuje pozycje po nazwie paczki.</summary>
    public ZrodloProjektu DodajZrodlo(string sciezka)
    {
        sciezka = Path.GetFullPath(sciezka).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var juz = Zrodla.Find(x => string.Equals(x.Sciezka?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), sciezka, StringComparison.OrdinalIgnoreCase));
        if (juz != null) return juz;
        var z = new ZrodloProjektu { Id = Guid.NewGuid().ToString("N").Substring(0, 8), Sciezka = sciezka, Typ = RozpoznajTyp(sciezka) };
        var baza = z.Typ == "rpf" ? Path.GetFileNameWithoutExtension(sciezka) : Path.GetFileName(sciezka);
        // "dlc.rpf" nic nie mowi — bierzemy nazwe folderu paczki (dlcpacks\studio_body\dlc.rpf -> studio_body)
        if (z.Typ == "rpf" && baza.Equals("dlc", StringComparison.OrdinalIgnoreCase)) baza = Path.GetFileName(Path.GetDirectoryName(sciezka)) ?? baza;
        if (string.IsNullOrEmpty(baza)) baza = sciezka;
        var nazwa = baza; int n = 2;
        while (Zrodla.Exists(x => string.Equals(x.Nazwa, nazwa, StringComparison.OrdinalIgnoreCase))) nazwa = $"{baza} ({n++})";
        z.Nazwa = nazwa;
        Zrodla.Add(z);
        return z;
    }

    /// <summary>rpf = plik .rpf; fivem = folder zasobu (fxmanifest/__resource/resource.toml/__stream.cfg albo podfolder stream); inaczej folder.</summary>
    public static string RozpoznajTyp(string sciezka)
    {
        if (File.Exists(sciezka)) return sciezka.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) ? "rpf" : "folder";
        foreach (var znak in new[] { "fxmanifest.lua", "__resource.lua", "resource.toml", "__stream.cfg" })
            if (File.Exists(Path.Combine(sciezka, znak))) return "fivem";
        if (Directory.Exists(Path.Combine(sciezka, "stream"))) return "fivem";
        return "folder";
    }
}
