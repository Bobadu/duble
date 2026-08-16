// Sesja.cs — stan aplikacji: otwarty projekt (*.duble) + katalog odciskow w pamieci + statystyki zrodel.
// Zapis: plik projektu (JSON) i katalog.json w <projekt>.duble.cache\. Miniatury z cache serwuje Zasob().
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duble;

namespace Duble.App;

public sealed class Sesja
{
    readonly object klucz = new();
    public Projekt Projekt { get; private set; }
    public Katalog Katalog { get; private set; } = new();
    public bool Otwarty => Projekt != null;
    /// <summary>Projekt/katalog sie zmienil (po zapisie, indeksowaniu, usunieciu zrodla).</summary>
    public event Action Zmiana;

    public void Nowy(string nazwa, string sciezkaPliku)
    {
        var p = Projekt.Nowy(nazwa, sciezkaPliku);
        Directory.CreateDirectory(p.FolderCache);
        p.Zapisz();
        lock (klucz) { Projekt = p; Katalog = new Katalog(); }
        Zmiana?.Invoke();
    }

    public void Otworz(string sciezkaPliku)
    {
        if (!File.Exists(sciezkaPliku)) throw new FileNotFoundException("brak projektu", sciezkaPliku);
        var p = Projekt.Wczytaj(sciezkaPliku);
        Directory.CreateDirectory(p.FolderCache);
        var k = Katalog.Wczytaj(p.PlikKatalogu);
        lock (klucz) { Projekt = p; Katalog = k; }
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
        }
        Zmiana?.Invoke();
    }

    public void Zamknij()
    {
        lock (klucz) { Projekt = null; Katalog = new Katalog(); }
        Zmiana?.Invoke();
    }

    /// <summary>Wykonaj zmiane katalogu pod blokada (indeksowanie z watku roboczego).</summary>
    public void ZmienKatalog(Action<Katalog> akcja) { lock (klucz) akcja(Katalog); }

    public object Podsumowanie()
    {
        lock (klucz)
        {
            if (Projekt == null) return null;
            return new
            {
                nazwa = Projekt.Nazwa, sciezka = Projekt.Sciezka,
                zrodla = Projekt.Zrodla.Count, pozycje = Katalog.Pozycje.Count,
                tekstury = Katalog.Pozycje.Sum(p => p.Tekstury.Count),
                duplikaty = (int?)null,
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

    /// <summary>Dane binarne dla https://duble.data/&lt;kategoria&gt;/&lt;klucz&gt; — miniatury z cache projektu.</summary>
    public Stream Zasob(string kategoria, string klucz)
    {
        var p = Projekt;
        if (p == null || string.IsNullOrEmpty(klucz) || klucz.Contains("..")) return null;
        string plik = kategoria switch
        {
            "thumb" => Path.Combine(p.FolderMiniatur, klucz + ".png"),
            "tex" => Path.Combine(p.FolderTekstur, klucz + ".png"),
            "mesh" => Path.Combine(p.FolderSiatek, klucz + ".glb"),
            _ => null,
        };
        if (plik == null || !File.Exists(plik)) return null;
        return new FileStream(plik, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }
}
