using System;
using System.IO;

namespace Duble.Tests;

/// <summary>
/// Sciezki do danych testowych. Korzen projektu = folder z Duble.sln szukany w gore od katalogu wyjsciowego —
/// dziala i w repo publicznym (korzen repo), i w studiu (tools\Duble).
///
/// Paczki testowe (duze pliki .rpf) nie sa czescia repo: sciezke do nich podaje DUBLE_TEST_DATA, a jesli
/// zmiennej nie ma, uzywany jest folder `downloads` obok repo (uklad studia). Bez tych danych testy, ktore
/// ich wymagaja, wypisuja POMINIETY i przechodza — dzieki temu `dotnet test` dziala po samym `git clone`.
/// </summary>
public static class Sciezki
{
    /// <summary>Folder z Duble.sln.</summary>
    public static string Projekt { get; } = Znajdz();

    static string Znajdz()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Duble.sln"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("nie znalazlam folderu z Duble.sln");
    }

    /// <summary>Folder z paczkami testowymi albo null, gdy ich nie ma.</summary>
    public static string DaneTestowe { get; } = SzukajDanych();

    static string SzukajDanych()
    {
        var z = Environment.GetEnvironmentVariable("DUBLE_TEST_DATA");
        if (!string.IsNullOrWhiteSpace(z)) return Directory.Exists(z) ? z : null;
        var d = new DirectoryInfo(Projekt);
        while (d != null)
        {
            var kandydat = Path.Combine(d.FullName, "downloads");
            if (Directory.Exists(kandydat)) return kandydat;
            d = d.Parent;
        }
        return null;
    }

    public static string Ui => Path.Combine(Projekt, "Duble.App", "ui");
    public static string Golden(string plik) => Path.Combine(Projekt, "Duble.Tests", "golden", plik);
    public static string Downloads(string paczka) => Path.Combine(DaneTestowe ?? Path.Combine(Projekt, "_brak-danych-testowych"), paczka);
    public static string Gra => Environment.GetEnvironmentVariable("GTAV_ENHANCED");
    public static string Dlc(string paczka) => Gra == null ? null : Path.Combine(Gra, "onigiri", "dlcpacks", paczka, "dlc.rpf");

    /// <summary>Folder tymczasowy testu, czyszczony przez wolajacego.</summary>
    public static string Tymczasowy(string nazwa)
    {
        var p = Path.Combine(Path.GetTempPath(), "duble-tests", nazwa + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(p);
        return p;
    }

    public static bool SaLegacy4 => DaneTestowe != null && Directory.Exists(Downloads("vrp_clothes_f_civil01")) && Directory.Exists(Downloads("civil_f_premium"));
    public static bool JestGra => Gra != null && File.Exists(Dlc("studio_body"));
}
