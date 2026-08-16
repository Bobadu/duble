// Sesja.cs — stan aplikacji: otwarty projekt (*.duble) + katalog odciskow + wynik porownania w pamieci + statystyki zrodel.
// Zapis: plik projektu (JSON), katalog.json i duble.json w <projekt>.duble.cache\. Miniatury i pelne tekstury z cache serwuje Zasob().
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CodeWalker.GameFiles;
using Duble;

namespace Duble.App;

public sealed class Sesja
{
    readonly object klucz = new();
    Dictionary<string, Tekstura> teksturyWgSha;   // indeks sha -> Tekstura (leniwy, kasowany po zmianie katalogu)
    public Projekt Projekt { get; private set; }
    public Katalog Katalog { get; private set; } = new();
    public WynikPorownania Wynik { get; private set; }
    public bool Otwarty => Projekt != null;
    /// <summary>Projekt/katalog/wynik sie zmienil (po zapisie, indeksowaniu, usunieciu zrodla, porownaniu).</summary>
    public event Action Zmiana;

    public void Nowy(string nazwa, string sciezkaPliku)
    {
        var p = Projekt.Nowy(nazwa, sciezkaPliku);
        Directory.CreateDirectory(p.FolderCache);
        p.Zapisz();
        lock (klucz) { Projekt = p; Katalog = new Katalog(); Wynik = null; teksturyWgSha = null; }
        Zmiana?.Invoke();
    }

    public void Otworz(string sciezkaPliku)
    {
        if (!File.Exists(sciezkaPliku)) throw new FileNotFoundException("brak projektu", sciezkaPliku);
        var p = Projekt.Wczytaj(sciezkaPliku);
        Directory.CreateDirectory(p.FolderCache);
        var k = Katalog.Wczytaj(p.PlikKatalogu);
        WynikPorownania w = null;
        try { if (File.Exists(p.PlikDubli)) w = WynikPorownania.Wczytaj(p.PlikDubli); } catch { w = null; }
        lock (klucz) { Projekt = p; Katalog = k; Wynik = w; teksturyWgSha = null; }
        Zmiana?.Invoke();
    }

    public void Zapisz()
    {
        lock (klucz)
        {
            if (Projekt == null) return;
            Directory.CreateDirectory(Projekt.FolderCache);
            Projekt.Zapisz();
            Katalog.Zbudowany = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Katalog.Zapisz(Projekt.PlikKatalogu);
            Wynik?.Zapisz(Projekt.PlikDubli);
        }
        Zmiana?.Invoke();
    }

    public void Zamknij()
    {
        lock (klucz) { Projekt = null; Katalog = new Katalog(); Wynik = null; teksturyWgSha = null; }
        Zmiana?.Invoke();
    }

    /// <summary>Wykonaj zmiane katalogu pod blokada (indeksowanie z watku roboczego).</summary>
    public void ZmienKatalog(Action<Katalog> akcja) { lock (klucz) { akcja(Katalog); teksturyWgSha = null; } }

    /// <summary>Porownanie pozycji WLACZONYCH zrodel progami projektu; wynik zapamietany i zapisany do duble.json.</summary>
    public void Porownaj(CancellationToken ct, Action<Postep> postep)
    {
        Projekt projekt; Katalog kopia;
        lock (klucz)
        {
            projekt = Projekt ?? throw new InvalidOperationException("brak projektu");
            var wlaczone = new HashSet<string>(projekt.Zrodla.Where(z => z.Wlaczone).Select(z => z.Id));
            kopia = new Katalog { Pozycje = Katalog.Pozycje.Where(p => p.ZrodloId == null || wlaczone.Contains(p.ZrodloId)).ToList() };
        }
        var progi = projekt.Ustawienia?.Progi ?? Progi.Domyslne;
        var wynik = Porownanie.Znajdz(kopia, null, progi, postep, ct);
        lock (klucz)
        {
            Wynik = wynik;
            Directory.CreateDirectory(projekt.FolderCache);
            wynik.Zapisz(projekt.PlikDubli);
        }
        Zmiana?.Invoke();
    }

    public object Podsumowanie()
    {
        lock (klucz)
        {
            if (Projekt == null) return null;
            int? duplikaty = Wynik == null ? null : Wynik.Grupy.Count(g => g.Werdykt == Porownanie.Duplikat || g.Werdykt == Porownanie.Nadzbior);
            return new
            {
                nazwa = Projekt.Nazwa, sciezka = Projekt.Sciezka,
                zrodla = Projekt.Zrodla.Count, pozycje = Katalog.Pozycje.Count,
                tekstury = Katalog.Pozycje.Sum(p => p.Tekstury.Count),
                duplikaty, porownano = Wynik?.Zbudowany,
            };
        }
    }

    /// <summary>Statystyki jednego zrodla z katalogu (po ZrodloId).</summary>
    public (int pozycje, int tekstury, Dictionary<string, int> perSlot, int bc7, string format) Statystyki(string zrodloId)
    {
        lock (klucz)
        {
            var poz = Katalog.Pozycje.Where(p => p.ZrodloId == zrodloId).ToList();
            var perSlot = poz.GroupBy(p => p.Typ).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
            int tekstury = poz.Sum(p => p.Tekstury.Count);
            int bc7 = poz.Sum(p => p.Tekstury.Count(t => t.Format == "BC7"));
            string format = poz.Count == 0 ? null : poz.All(p => p.Gen9) ? "gen9" : poz.All(p => !p.Gen9) ? "legacy" : "mieszany";
            return (poz.Count, tekstury, perSlot, bc7, format);
        }
    }

    public Pozycja ZnajdzPozycje(string id) { lock (klucz) return Katalog.Pozycje.FirstOrDefault(p => p.Id == id); }

    public Tekstura ZnajdzTeksture(string sha)
    {
        if (string.IsNullOrEmpty(sha)) return null;
        lock (klucz)
        {
            if (teksturyWgSha == null)
            {
                teksturyWgSha = new Dictionary<string, Tekstura>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in Katalog.Pozycje.SelectMany(p => p.Tekstury)) if (t.Sha != null && !teksturyWgSha.ContainsKey(t.Sha)) teksturyWgSha[t.Sha] = t;
            }
            return teksturyWgSha.TryGetValue(sha, out var w) ? w : null;
        }
    }

    /// <summary>Dane binarne dla https://duble.data/&lt;kategoria&gt;/&lt;klucz&gt;: thumb (cache), tex (cache albo generuj), mesh (etap 4).</summary>
    public Stream Zasob(string kategoria, string klucz)
    {
        var p = Projekt;
        if (p == null || string.IsNullOrEmpty(klucz) || klucz.Contains("..") || klucz.Contains('/') || klucz.Contains('\\')) return null;
        string plik = kategoria switch
        {
            "thumb" => Path.Combine(p.FolderMiniatur, klucz + ".png"),
            "tex" => Path.Combine(p.FolderTekstur, klucz + ".png"),
            "mesh" => Path.Combine(p.FolderSiatek, klucz + ".glb"),
            _ => null,
        };
        if (plik == null) return null;
        if (!File.Exists(plik) && kategoria == "tex" && !GenerujTeksture(klucz, plik)) return null;
        if (!File.Exists(plik)) return null;
        return new FileStream(plik, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    /// <summary>Pelna tekstura (bok &lt;= 1024) z pliku zrodlowego -> PNG w cache tex\. false = nie ma takiej / nie da sie zdekodowac.</summary>
    bool GenerujTeksture(string sha, string plik)
    {
        try
        {
            var t = ZnajdzTeksture(sha);
            if (t?.Sciezka == null) return false;
            var bajty = Zrodla.Bajty(t.Sciezka);
            if (bajty == null) return false;
            Format.Przygotuj();
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, bajty, 13);
            var tex = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
            var png = tex == null ? null : Tekstury.PngRgba(tex, 1024);
            if (png == null) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(plik));
            var tmp = plik + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp";
            File.WriteAllBytes(tmp, png);
            try { File.Move(tmp, plik, true); } catch { try { File.Delete(tmp); } catch { } }
            return File.Exists(plik);
        }
        catch { return false; }
    }
}
