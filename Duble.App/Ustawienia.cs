// Ustawienia.cs — ustawienia PROGRAMU (nie projektu): jezyk, motyw, ostatnie projekty, polozenie okna.
// Plik: %AppData%\Bobadu\Duble\settings.json. Dane WebView2: %LocalAppData%\Bobadu\Duble\WebView2.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duble.App;

public class OstatniProjekt { public string Sciezka { get; set; } public string Name { get; set; } public string Ostatnio { get; set; } }
public class OknoStan { public double X { get; set; } public double Y { get; set; } public double W { get; set; } public double H { get; set; } public bool Maks { get; set; } }

public class Ustawienia
{
    public const int MaksOstatnich = 10;
    public string Jezyk { get; set; }            // null = z Windows (pl-* -> pl, reszta -> en)
    public string Motyw { get; set; } = "system"; // system | dark | light
    public List<OstatniProjekt> Ostatnie { get; set; } = new();
    public OknoStan Okno { get; set; }

    public static string FolderDanych => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bobadu", "Duble");
    public static string Sciezka => Path.Combine(FolderDanych, "settings.json");
    public static string FolderWebView2 => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bobadu", "Duble", "WebView2");
    public static string FolderProjektow => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Duble");

    static readonly JsonSerializerOptions Opcje = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static Ustawienia Wczytaj(string sciezka = null)
    {
        sciezka ??= Sciezka;
        try { if (File.Exists(sciezka)) return JsonSerializer.Deserialize<Ustawienia>(File.ReadAllText(sciezka), Opcje) ?? new Ustawienia(); }
        catch { /* uszkodzony plik -> domyslne */ }
        return new Ustawienia();
    }

    public void Zapisz(string sciezka = null)
    {
        sciezka ??= Sciezka;
        Directory.CreateDirectory(Path.GetDirectoryName(sciezka));
        File.WriteAllText(sciezka, JsonSerializer.Serialize(this, Opcje));
    }

    public void ZanotujProjekt(string sciezka, string nazwa)
    {
        Ostatnie.RemoveAll(o => string.Equals(o.Sciezka, sciezka, StringComparison.OrdinalIgnoreCase));
        Ostatnie.Insert(0, new OstatniProjekt { Sciezka = sciezka, Name = nazwa, Ostatnio = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
        if (Ostatnie.Count > MaksOstatnich) Ostatnie.RemoveRange(MaksOstatnich, Ostatnie.Count - MaksOstatnich);
    }

    /// <summary>Jezyk efektywny: ustawiony albo z Windows.</summary>
    [JsonIgnore]
    public string JezykEfektywny => !string.IsNullOrEmpty(Jezyk) ? Jezyk
        : System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pl", StringComparison.OrdinalIgnoreCase) ? "pl" : "en";
}
