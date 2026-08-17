// Sesja.cs — stan aplikacji: otwarty projekt (*.duble) + katalog odciskow + wynik porownania w pamieci + statystyki zrodel.
// Zapis: plik projektu (JSON), katalog.json i duble.json w <projekt>.duble.cache\. Miniatury i pelne tekstury z cache serwuje Zasob().
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CodeWalker.GameFiles;
using Duble.Core;

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

    /// <summary>Kopia katalogu z pozycjami WLACZONYCH zrodel (to porownujemy i kalibrujemy).</summary>
    public Katalog KatalogWlaczony()
    {
        lock (klucz)
        {
            var projekt = Projekt ?? throw new InvalidOperationException("brak projektu");
            var wlaczone = new HashSet<string>(projekt.Zrodla.Where(z => z.Wlaczone).Select(z => z.Id));
            return new Katalog { Pozycje = Katalog.Pozycje.Where(p => p.ZrodloId == null || wlaczone.Contains(p.ZrodloId)).ToList() };
        }
    }

    /// <summary>Progi projektu (albo domyslne).</summary>
    public Progi ProgiProjektu => Projekt?.Ustawienia?.Progi ?? Progi.Domyslne;

    /// <summary>Rozmiar cache projektu: (pliki, bajty) per folder + razem.</summary>
    public Dictionary<string, (int pliki, long bajty)> RozmiarCache()
    {
        var wy = new Dictionary<string, (int, long)>();
        var p = Projekt; if (p == null) return wy;
        long razem = 0; int razemN = 0;
        foreach (var (nazwa, folder) in new[] { ("thumbs", p.FolderMiniatur), ("tex", p.FolderTekstur), ("mesh", p.FolderSiatek), ("historia", p.FolderHistorii) })
        {
            int n = 0; long b = 0;
            if (Directory.Exists(folder))
                foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) { n++; try { b += new FileInfo(f).Length; } catch { } }
            wy[nazwa] = (n, b); razem += b; razemN += n;
        }
        wy["razem"] = (razemN, razem);
        return wy;
    }

    /// <summary>Usuwa pliki podgladow odtwarzanych na zadanie (tex\ i/lub mesh\). Zwraca (usuniete, bajty).</summary>
    public (int pliki, long bajty) WyczyscCache(bool tex, bool mesh)
    {
        var p = Projekt; if (p == null) return (0, 0);
        int n = 0; long b = 0;
        foreach (var folder in new[] { tex ? p.FolderTekstur : null, mesh ? p.FolderSiatek : null })
        {
            if (folder == null || !Directory.Exists(folder)) continue;
            foreach (var f in Directory.EnumerateFiles(folder))
            {
                try { var dl = new FileInfo(f).Length; File.Delete(f); n++; b += dl; } catch { }
            }
        }
        return (n, b);
    }

    /// <summary>Porownanie pozycji WLACZONYCH zrodel progami projektu; wynik zapamietany i zapisany do duble.json.</summary>
    public void Porownaj(CancellationToken ct, Action<Postep> postep)
    {
        var projekt = Projekt ?? throw new InvalidOperationException("brak projektu");
        var kopia = KatalogWlaczony();
        var progi = projekt.Ustawienia?.Progi ?? Progi.Domyslne;
        var wynik = Porownanie.Znajdz(kopia, null, progi, postep, ct);
        lock (klucz)
        {
            // decyzje uzytkownika przechodza na nowe (mniejsze) grupy — po Zastosuj / ponownym indeksowaniu nic nie wraca do "do odrzucenia"
            if (Wynik != null && projekt.Decyzje.Count > 0 && Rozstrzygniecie.PrzeniesDecyzje(projekt.Decyzje, Wynik.Grupy, wynik.Grupy) > 0)
                projekt.Zapisz();
            Wynik = wynik;
            Directory.CreateDirectory(projekt.FolderCache);
            wynik.Zapisz(projekt.PlikDubli);
        }
        Zmiana?.Invoke();
    }

    // ---------------- zastosowanie: zrodlo pozycji, kosz, plan ----------------

    /// <summary>Zrodlo projektu, z ktorego pochodzi pozycja (po ZrodloId; starsze katalogi — po nazwie paczki).</summary>
    public ZrodloProjektu ZrodloPozycji(Pozycja p)
    {
        var pr = Projekt; if (pr == null || p == null) return null;
        return pr.Zrodla.Find(z => z.Id == p.ZrodloId) ?? pr.Zrodla.Find(z => string.Equals(z.Nazwa, p.Paczka, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Folder kosza dla zrodla: `Ustawienia.Kosz` (wskazany folder) albo `_odrzucone` obok zrodla — w obu przypadkach z podfolderem o nazwie zrodla.</summary>
    public string KoszDla(ZrodloProjektu z)
    {
        var pr = Projekt; if (pr == null || z == null) return null;
        var kosz = pr.Ustawienia?.Kosz;
        if (string.IsNullOrWhiteSpace(kosz))
        {
            var sciezka = z.Sciezka.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var nad = Path.GetDirectoryName(sciezka) ?? sciezka;
            kosz = Path.Combine(nad, Indeks.FolderOdrzuconych);
        }
        return Path.Combine(kosz, Bezpieczna(z.Nazwa));
    }

    static string Bezpieczna(string nazwa)
    {
        var zle = Path.GetInvalidFileNameChars();
        var s = new string((nazwa ?? "zrodlo").Select(c => zle.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "zrodlo" : s;
    }

    /// <summary>Cel przenosin dla pozycji (null = zrodla nie ma w projekcie albo na dysku).</summary>
    public CelPozycji Cel(Pozycja p)
    {
        var z = ZrodloPozycji(p);
        if (z == null || z.Sciezka == null || !(Directory.Exists(z.Sciezka) || File.Exists(z.Sciezka))) return null;
        return new CelPozycji { Korzen = z.Sciezka, Kosz = KoszDla(z), Zrodlo = z.Nazwa, ZrodloId = z.Id };
    }

    public PlanZastosowania Zaplanuj(IEnumerable<string> odrzucone)
    {
        lock (klucz) return Zastosowanie.Zaplanuj(Katalog, odrzucone, Cel);
    }

    /// <summary>Pliki historii zastosowan (najnowsze pierwsze).</summary>
    public List<string> PlikiHistorii()
    {
        var pr = Projekt; if (pr == null || !Directory.Exists(pr.FolderHistorii)) return new();
        return Directory.GetFiles(pr.FolderHistorii, "*.json").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
    }

    public string NowyPlikHistorii()
    {
        var pr = Projekt ?? throw new InvalidOperationException("brak projektu");
        Directory.CreateDirectory(pr.FolderHistorii);
        var baza = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var plik = Path.Combine(pr.FolderHistorii, baza + ".json");
        for (int i = 2; File.Exists(plik); i++) plik = Path.Combine(pr.FolderHistorii, $"{baza}-{i}.json");
        return plik;
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

    /// <summary>Statystyki jednego zrodla z katalogu (po ZrodloId). wArchiwum = pozycje, ktorych ydd siedzi w .rpf (nieprzenoszalne).</summary>
    public (int pozycje, int tekstury, Dictionary<string, int> perSlot, int bc7, string format, int wArchiwum) Statystyki(string zrodloId)
    {
        lock (klucz)
        {
            var poz = Katalog.Pozycje.Where(p => p.ZrodloId == zrodloId).ToList();
            var perSlot = poz.GroupBy(p => p.Typ).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
            int tekstury = poz.Sum(p => p.Tekstury.Count);
            int bc7 = poz.Sum(p => p.Tekstury.Count(t => t.Format == "BC7"));
            string format = poz.Count == 0 ? null : poz.All(p => p.Gen9) ? "gen9" : poz.All(p => !p.Gen9) ? "legacy" : "mieszany";
            int wArchiwum = poz.Count(p => p.SciezkaYdd != null && p.SciezkaYdd.Contains('|'));
            return (poz.Count, tekstury, perSlot, bc7, format, wArchiwum);
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

    /// <summary>Dane binarne dla https://duble.data/&lt;kategoria&gt;/&lt;klucz&gt;[?query]: thumb (cache), tex (cache albo generuj),
    /// mesh (klucz = id pozycji, query "w=&lt;litera&gt;" = wariant tekstury; GLB generowany do cache mesh\).</summary>
    public Stream Zasob(string kategoria, string klucz, string query = null)
    {
        var p = Projekt;
        if (p == null || string.IsNullOrEmpty(klucz) || klucz.Contains("..") || klucz.Contains('/') || klucz.Contains('\\')) return null;
        string plik;
        switch (kategoria)
        {
            case "thumb": plik = Path.Combine(p.FolderMiniatur, klucz + ".png"); break;
            case "tex":
                plik = Path.Combine(p.FolderTekstur, klucz + ".png");
                if (!File.Exists(plik) && !GenerujTeksture(klucz, plik)) return null;
                break;
            case "mesh":
                plik = GenerujSiatke(klucz, Parametr(query, "w"));
                if (plik == null) return null;
                break;
            default: return null;
        }
        if (!File.Exists(plik)) return null;
        return new FileStream(plik, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    static string Parametr(string query, string nazwa)
    {
        if (string.IsNullOrEmpty(query)) return null;
        foreach (var czesc in query.Split('&'))
        {
            var i = czesc.IndexOf('=');
            if (i > 0 && czesc.Substring(0, i) == nazwa) return Uri.UnescapeDataString(czesc.Substring(i + 1));
        }
        return null;
    }

    /// <summary>GLB pozycji (najwyzszy LOD + tekstura wariantu) w cache mesh\&lt;ShaYdd16&gt;_&lt;ShaTex16&gt;.glb — nazwa zalezy od zawartosci,
    /// wiec po ponownym indeksowaniu innych plikow cache sam sie uniewaznia. Zwraca sciezke pliku albo null.</summary>
    string GenerujSiatke(string idPozycji, string litera)
    {
        try
        {
            var poz = ZnajdzPozycje(idPozycji);
            if (poz == null || string.IsNullOrEmpty(poz.SciezkaYdd)) return null;
            var tex = poz.Tekstury.FirstOrDefault(t => litera != null && string.Equals(Nazwy.Tekstura(t.Plik)?.Litera, litera, StringComparison.OrdinalIgnoreCase))
                      ?? poz.Tekstury.FirstOrDefault();
            string Krotki(string sha) => string.IsNullOrEmpty(sha) ? "brak" : sha.Length > 16 ? sha.Substring(0, 16) : sha;
            var plik = Path.Combine(Projekt.FolderSiatek, $"{Krotki(poz.ShaYdd)}_{Krotki(tex?.Sha)}.glb");
            if (File.Exists(plik)) return plik;
            var glb = Podglad3D.Glb(poz, tex != null ? Nazwy.Tekstura(tex.Plik)?.Litera : null);
            Directory.CreateDirectory(Projekt.FolderSiatek);
            var tmp = plik + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp";
            File.WriteAllBytes(tmp, glb);
            try { File.Move(tmp, plik, true); } catch { try { File.Delete(tmp); } catch { } }
            return File.Exists(plik) ? plik : null;
        }
        catch { return null; }
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
