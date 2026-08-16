using System;
using System.IO;

namespace Duble.Tests;

/// <summary>Sciezki do danych testowych; korzen repo szukany w gore od folderu testow (tools\Duble\Duble.sln).</summary>
public static class Sciezki
{
    public static string Korzen { get; } = Znajdz();

    static string Znajdz()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "tools", "Duble", "Duble.sln"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("nie znalazlam korzenia repo (tools\\Duble\\Duble.sln)");
    }

    public static string Golden(string plik) => Path.Combine(Korzen, "tools", "Duble", "Duble.Tests", "golden", plik);
    public static string Downloads(string paczka) => Path.Combine(Korzen, "downloads", paczka);
    public static string Gra => Environment.GetEnvironmentVariable("GTAV_ENHANCED");
    public static string Dlc(string paczka) => Gra == null ? null : Path.Combine(Gra, "onigiri", "dlcpacks", paczka, "dlc.rpf");

    /// <summary>Folder tymczasowy testu, czyszczony przez wolajacego.</summary>
    public static string Tymczasowy(string nazwa)
    {
        var p = Path.Combine(Path.GetTempPath(), "duble-tests", nazwa + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(p);
        return p;
    }

    public static bool SaLegacy4 => Directory.Exists(Downloads("vrp_clothes_f_civil01")) && Directory.Exists(Downloads("civil_f_premium"));
    public static bool JestGra => Gra != null && File.Exists(Dlc("studio_body"));
}
