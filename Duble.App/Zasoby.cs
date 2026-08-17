// Zasoby.cs — odpowiedzi na https://duble.app/* (pliki UI) i https://duble.data/* (slowniki, miniatury, tekstury, siatki).
//
// UI: w trybie dev z folderu (edycja na zywo), normalnie z zasobow osadzonych w exe (LogicalName "ui/<sciezka>").
// Dane: i18n = slownik UI (ui\i18n\<jezyk>.json) + slownik Core (Teksty.Slownik) zlaczone; reszta przez delegat Dane
// (ustawia Sesja: thumb -> <projekt>.duble.cache\thumbs\<sha>.png itd.).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Duble.App;

public sealed class Zasoby
{
    readonly string uiFolder;
    readonly Dictionary<string, string> osadzone = new(StringComparer.OrdinalIgnoreCase);   // "index.html" -> logical name

    /// <summary>(kategoria, klucz, query bez '?') -> strumien albo null. Kategorie: thumb, tex, mesh (mesh: query "w=&lt;litera&gt;" = wariant tekstury).</summary>
    public Func<string, string, string, Stream> Dane { get; set; }

    public bool ZFolderu => uiFolder != null;

    public Zasoby(string uiFolder)
    {
        this.uiFolder = uiFolder != null ? Path.GetFullPath(uiFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar : null;
        foreach (var n in typeof(Zasoby).Assembly.GetManifestResourceNames())
            if (n.StartsWith("ui/", StringComparison.OrdinalIgnoreCase) || n.StartsWith("ui\\", StringComparison.OrdinalIgnoreCase))
                osadzone[n.Substring(3).Replace('\\', '/')] = n;
    }

    public bool Rozwiaz(string url, out Stream tresc, out string mime, out int status)
    {
        tresc = null; mime = "application/octet-stream"; status = 404;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var sciezka = Uri.UnescapeDataString(u.AbsolutePath.TrimStart('/'));
        if (u.Host.Equals("duble.app", StringComparison.OrdinalIgnoreCase))
        {
            if (sciezka.Length == 0) sciezka = "index.html";
            if (sciezka.Contains("..")) return false;
            mime = Mime(sciezka);
            if (uiFolder != null)
            {
                var pelna = Path.GetFullPath(Path.Combine(uiFolder, sciezka));
                if (!pelna.StartsWith(uiFolder, StringComparison.OrdinalIgnoreCase) || !File.Exists(pelna)) return false;
                tresc = new FileStream(pelna, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                status = 200; return true;
            }
            if (!osadzone.TryGetValue(sciezka, out var nazwa)) return false;
            tresc = typeof(Zasoby).Assembly.GetManifestResourceStream(nazwa);
            status = tresc != null ? 200 : 404; return tresc != null;
        }
        if (u.Host.Equals("duble.data", StringComparison.OrdinalIgnoreCase))
        {
            var czesci = sciezka.Split('/', 2);
            if (czesci.Length < 2 || czesci[1].Length == 0) return false;
            var kategoria = czesci[0]; var klucz = Path.GetFileNameWithoutExtension(czesci[1]);
            if (kategoria == "i18n")
            {
                tresc = new MemoryStream(Encoding.UTF8.GetBytes(Slownik(klucz))); mime = "application/json; charset=utf-8"; status = 200; return true;
            }
            tresc = Dane?.Invoke(kategoria, klucz, (u.Query ?? "").TrimStart('?'));
            if (tresc == null) return false;
            mime = Mime(czesci[1]); status = 200; return true;
        }
        return false;
    }

    /// <summary>Slownik UI + Core dla jezyka (klucze UI wygrywaja przy kolizji).</summary>
    public string Slownik(string jezyk)
    {
        var wynik = new Dictionary<string, string>(Duble.Core.Teksty.Slownik(jezyk));
        if (Rozwiaz($"https://duble.app/i18n/{jezyk}.json", out var s, out _, out _))
            using (s)
            {
                var ui = JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
                foreach (var kv in ui) wynik[kv.Key] = kv.Value;
            }
        return JsonSerializer.Serialize(wynik, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public static string Mime(string sciezka) => (Path.GetExtension(sciezka) ?? "").ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".ico" => "image/x-icon",
        ".glb" => "model/gltf-binary",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        _ => "application/octet-stream",
    };
}
