// Powody.cs — powody werdyktow jako KODY + parametry, teksty PL/EN z i18n\*.json.
//
// Aplikacja okienkowa jest dwujezyczna, wiec silnik nie moze zwracac gotowych polskich zdan.
// Zasada: Powod = kod + slownik parametrow (stringi, liczby juz sformatowane InvariantCulture).
// Wartosc parametru zaczynajaca sie od '@' jest kluczem do przetlumaczenia (np. "@geo.identyczna").
// Teksty PL musza odtwarzac napisy sprzed refaktoru co do znaku — sprawdza to test zlotego wzorca.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Duble.Core;

public class Powod
{
    public string Kod { get; set; }
    public Dictionary<string, string> P { get; set; } = new();

    public Powod() { }

    public Powod(string kod, params (string k, object v)[] p)
    {
        Kod = kod;
        foreach (var (k, v) in p) P[k] = Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    public override string ToString() => Teksty.Powod(this, "pl");
}

public static class Teksty
{
    public static readonly string[] Jezyki = { "pl", "en" };
    static readonly ConcurrentDictionary<string, Dictionary<string, string>> Slowniki = new();
    static readonly Regex ReParam = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    public static Dictionary<string, string> Slownik(string jezyk)
        => Slowniki.GetOrAdd(Normalizuj(jezyk), Wczytaj);

    static string Normalizuj(string jezyk)
        => string.IsNullOrEmpty(jezyk) ? "pl" : jezyk.ToLowerInvariant().StartsWith("pl") ? "pl" : "en";

    static Dictionary<string, string> Wczytaj(string jezyk)
    {
        var nazwa = $"Duble.Core.i18n.{jezyk}.json";
        using var s = typeof(Teksty).Assembly.GetManifestResourceStream(nazwa)
            ?? throw new FileNotFoundException("brak zasobu " + nazwa);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
    }

    /// <summary>Tekst pod kluczem w danym jezyku; brak w EN → PL; brak w PL → "[klucz]".</summary>
    public static string T(string jezyk, string klucz, IReadOnlyDictionary<string, string> p = null)
    {
        jezyk = Normalizuj(jezyk);
        if (!Slownik(jezyk).TryGetValue(klucz, out var szablon) && !Slownik("pl").TryGetValue(klucz, out szablon))
            szablon = "[" + klucz + "]";
        if (p == null || p.Count == 0) return szablon;
        return ReParam.Replace(szablon, m =>
        {
            if (!p.TryGetValue(m.Groups[1].Value, out var v) || v == null) return m.Value;
            return v.StartsWith("@") ? T(jezyk, v.Substring(1)) : v;
        });
    }

    public static string Powod(Powod p, string jezyk) => p == null ? "" : T(jezyk, "powod." + p.Kod, p.P);
}

/// <summary>Skladniki oceny jakosci 0..100 (Porownanie.Jakosc) — do slupkow w aplikacji i tekstu w raporcie.</summary>
public class Punktacja
{
    public double Razem { get; set; }
    public double Rozdz { get; set; }       // max 40
    public double Mipy { get; set; }        // max 20
    public double Warianty { get; set; }    // max 20
    public double Format { get; set; }      // max 10
    public double Lod { get; set; }         // max 10
    public double RozdzPx { get; set; }     // mediana boku (px)
    public double UdzialMipow { get; set; } // 0..1
    public int LiczbaWariantow { get; set; }
    public int ZlyFormat { get; set; }      // ile BC1 z alfa
    public int Lody { get; set; }
    public bool BrakTekstur { get; set; }

    public string Tekst(string jezyk)
    {
        if (BrakTekstur) return Teksty.T(jezyk, "jakosc.brak");
        var inv = CultureInfo.InvariantCulture;
        var p = new Dictionary<string, string>
        {
            ["px"] = RozdzPx.ToString("F0", inv), ["pRozdz"] = Rozdz.ToString("F0", inv),
            ["mipy"] = UdzialMipow.ToString("P0", inv), ["pMipy"] = Mipy.ToString("F0", inv),
            ["n"] = LiczbaWariantow.ToString(inv), ["pWar"] = Warianty.ToString("F0", inv),
            ["pFmt"] = Format.ToString("F0", inv),
            ["zly"] = ZlyFormat > 0 ? Teksty.T(jezyk, "jakosc.zlyFormat", new Dictionary<string, string> { ["n"] = ZlyFormat.ToString(inv) }) : "",
            ["lod"] = Lody.ToString(inv), ["pLod"] = Lod.ToString("F0", inv),
        };
        return Teksty.T(jezyk, "jakosc.rozpiska", p);
    }
}
