// Sesja.cs — stan aplikacji: otwarty projekt (*.duble) + katalog odciskow + wynik porownania w pamieci + statystyki zrodel.
// Zapis: plik projektu (JSON), katalog.json i duble.json w <projekt>.duble.cache\. Miniatury i pelne tekstury z cache serwuje Zasob().
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CodeWalker.GameFiles;

namespace Duble.App;

public sealed class Sesja
{
    readonly object klucz = new();
    readonly ICatalogStore katalogi;
    readonly IProjectStore projekty;
    readonly IClock zegar;

    public Sesja(ICatalogStore katalogi, IProjectStore projekty, IResolutionService rozstrzygniecia,
                 IGarmentIndexer indeksator, IArchiveCache archiwa, IClock zegar)
    {
        this.katalogi = katalogi;
        this.projekty = projekty;
        this.zegar = zegar;
        Rozstrzygniecia = rozstrzygniecia;
        Indeksator = indeksator;
        Archiwa = archiwa;
    }

    /// <summary>Indeksowanie zrodel — komendy wolaja je przez JobRunner.</summary>
    public IGarmentIndexer Indeksator { get; }

    /// <summary>Ponowny odczyt zaindeksowanych plikow (miniatury, podglady) — trzyma otwarte archiwa.</summary>
    public IArchiveCache Archiwa { get; }

    /// <summary>Reguly "kto zostaje" — komendy licza je dla grup, ktore pokazuja.</summary>
    public IResolutionService Rozstrzygniecia { get; }

    /// <summary>Zapisuje sam plik projektu (decyzje, zrodla, ustawienia) bez katalogu i wyniku.</summary>
    public void ZapiszProjekt()
    {
        var p = Project;
        if (p == null) return;
        var wynik = projekty.Save(p);
        if (wynik.IsFailure) throw new IOException(wynik.Error.Message);
    }

    Dictionary<string, TextureInfo> teksturyWgSha;   // indeks sha -> TextureInfo (leniwy, kasowany po zmianie katalogu)
    public Project Project { get; private set; }
    public Catalog Catalog { get; private set; } = new();
    public WynikPorownania Wynik { get; private set; }
    public bool Otwarty => Project != null;
    /// <summary>Project/katalog/wynik sie zmienil (po zapisie, indeksowaniu, usunieciu zrodla, porownaniu).</summary>
    public event Action Zmiana;

    public void Nowy(string nazwa, string sciezkaPliku)
    {
        var p = Project.Create(nazwa, sciezkaPliku, zegar.Now);
        Directory.CreateDirectory(p.CacheFolder);
        projekty.Save(p);
        lock (klucz) { Project = p; Catalog = new Catalog(); Wynik = null; teksturyWgSha = null; }
        Zmiana?.Invoke();
    }

    public void Otworz(string sciezkaPliku)
    {
        if (!File.Exists(sciezkaPliku)) throw new FileNotFoundException("brak projektu", sciezkaPliku);
        var wczytany = projekty.Load(sciezkaPliku);
        if (wczytany.IsFailure) throw new IOException(wczytany.Error.Message);
        var p = wczytany.Value;
        Directory.CreateDirectory(p.CacheFolder);
        var k = katalogi.Load(p.CatalogFile);
        WynikPorownania w = null;
        try { if (File.Exists(p.ComparisonFile)) w = WynikPorownania.Wczytaj(p.ComparisonFile); } catch { w = null; }
        lock (klucz) { Project = p; Catalog = k; Wynik = w; teksturyWgSha = null; }
        Zmiana?.Invoke();
    }

    public void Zapisz()
    {
        lock (klucz)
        {
            if (Project == null) return;
            Directory.CreateDirectory(Project.CacheFolder);
            projekty.Save(Project);
            katalogi.Save(Catalog, Project.CatalogFile);
            Wynik?.Zapisz(Project.ComparisonFile);
        }
        Zmiana?.Invoke();
    }

    public void Zamknij()
    {
        lock (klucz) { Project = null; Catalog = new Catalog(); Wynik = null; teksturyWgSha = null; }
        Zmiana?.Invoke();
    }

    /// <summary>Wykonaj zmiane katalogu pod blokada (indeksowanie z watku roboczego).</summary>
    public void ZmienKatalog(Action<Catalog> akcja) { lock (klucz) { akcja(Catalog); teksturyWgSha = null; } }

    /// <summary>Kopia katalogu z pozycjami WLACZONYCH zrodel (to porownujemy i kalibrujemy).</summary>
    public Catalog KatalogWlaczony()
    {
        lock (klucz)
        {
            var projekt = Project ?? throw new InvalidOperationException("brak projektu");
            var wlaczone = new HashSet<string>(projekt.Sources.Where(z => z.Enabled).Select(z => z.Id));
            return new Catalog { Garments = Catalog.Garments.Where(p => p.SourceId == null || wlaczone.Contains(p.SourceId)).ToList() };
        }
    }

    /// <summary>Thresholds projektu (albo domyslne).</summary>
    public Thresholds ProgiProjektu => Project?.Settings?.Thresholds ?? Thresholds.Default;

    /// <summary>Rozmiar cache projektu: (pliki, bajty) per folder + razem.</summary>
    public Dictionary<string, (int pliki, long bajty)> RozmiarCache()
    {
        var wy = new Dictionary<string, (int, long)>();
        var p = Project; if (p == null) return wy;
        long razem = 0; int razemN = 0;
        foreach (var (nazwa, folder) in new[] { ("thumbs", p.ThumbnailFolder), ("tex", p.TextureFolder), ("mesh", p.MeshFolder), ("historia", p.HistoryFolder) })
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
        var p = Project; if (p == null) return (0, 0);
        int n = 0; long b = 0;
        foreach (var folder in new[] { tex ? p.TextureFolder : null, mesh ? p.MeshFolder : null })
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
    public void Porownaj(CancellationToken ct, Action<ProgressReport> postep)
    {
        var projekt = Project ?? throw new InvalidOperationException("brak projektu");
        var kopia = KatalogWlaczony();
        var progi = projekt.Settings?.Thresholds ?? Thresholds.Default;
        var wynik = Porownanie.Znajdz(kopia, null, progi, postep, ct);
        lock (klucz)
        {
            // decyzje uzytkownika przechodza na nowe (mniejsze) grupy — po Zastosuj / ponownym indeksowaniu nic nie wraca do "do odrzucenia"
            if (Wynik != null && projekt.Decisions.Count > 0 && Rozstrzygniecia.CarryOver(projekt.Decisions, Wynik.Grupy, wynik.Grupy) > 0)
                projekty.Save(projekt);
            Wynik = wynik;
            Directory.CreateDirectory(projekt.CacheFolder);
            wynik.Zapisz(projekt.ComparisonFile);
        }
        Zmiana?.Invoke();
    }

    // ---------------- zastosowanie: zrodlo pozycji, kosz, plan ----------------

    /// <summary>Zrodlo projektu, z ktorego pochodzi pozycja (po ZrodloId; starsze katalogi — po nazwie paczki).</summary>
    public ProjectSource ZrodloPozycji(Garment p)
    {
        var pr = Project; if (pr == null || p == null) return null;
        return pr.Sources.Find(z => z.Id == p.SourceId) ?? pr.Sources.Find(z => string.Equals(z.Name, p.PackName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Folder kosza dla zrodla: `Ustawienia.Kosz` (wskazany folder) albo `_odrzucone` obok zrodla — w obu przypadkach z podfolderem o nazwie zrodla.</summary>
    public string KoszDla(ProjectSource z)
    {
        var pr = Project; if (pr == null || z == null) return null;
        var kosz = pr.Settings?.BinFolder;
        if (string.IsNullOrWhiteSpace(kosz))
        {
            var sciezka = z.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var nad = Path.GetDirectoryName(sciezka) ?? sciezka;
            kosz = Path.Combine(nad, BinFolder.Name);
        }
        return Path.Combine(kosz, Bezpieczna(z.Name));
    }

    static string Bezpieczna(string nazwa)
    {
        var zle = Path.GetInvalidFileNameChars();
        var s = new string((nazwa ?? "zrodlo").Select(c => zle.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "zrodlo" : s;
    }

    /// <summary>Cel przenosin dla pozycji (null = zrodla nie ma w projekcie albo na dysku).</summary>
    public CelPozycji Cel(Garment p)
    {
        var z = ZrodloPozycji(p);
        if (z == null || z.Path == null || !(Directory.Exists(z.Path) || File.Exists(z.Path))) return null;
        return new CelPozycji { Korzen = z.Path, Kosz = KoszDla(z), Zrodlo = z.Name, ZrodloId = z.Id };
    }

    public PlanZastosowania Zaplanuj(IEnumerable<string> odrzucone)
    {
        lock (klucz) return Zastosowanie.Zaplanuj(Catalog, odrzucone, Cel);
    }

    /// <summary>Pliki historii zastosowan (najnowsze pierwsze).</summary>
    public List<string> PlikiHistorii()
    {
        var pr = Project; if (pr == null || !Directory.Exists(pr.HistoryFolder)) return new();
        return Directory.GetFiles(pr.HistoryFolder, "*.json").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
    }

    public string NowyPlikHistorii()
    {
        var pr = Project ?? throw new InvalidOperationException("brak projektu");
        Directory.CreateDirectory(pr.HistoryFolder);
        var baza = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var plik = Path.Combine(pr.HistoryFolder, baza + ".json");
        for (int i = 2; File.Exists(plik); i++) plik = Path.Combine(pr.HistoryFolder, $"{baza}-{i}.json");
        return plik;
    }

    public object Podsumowanie()
    {
        lock (klucz)
        {
            if (Project == null) return null;
            int? duplikaty = Wynik == null ? null : Wynik.Grupy.Count(g => g.Werdykt == Porownanie.Duplikat || g.Werdykt == Porownanie.Nadzbior);
            return new
            {
                nazwa = Project.Name, sciezka = Project.Path,
                zrodla = Project.Sources.Count, pozycje = Catalog.Garments.Count,
                tekstury = Catalog.Garments.Sum(p => p.Textures.Count),
                duplikaty, porownano = Wynik?.Zbudowany,
            };
        }
    }

    /// <summary>Statystyki jednego zrodla z katalogu (po ZrodloId). wArchiwum = pozycje, ktorych ydd siedzi w .rpf (nieprzenoszalne).</summary>
    public (int pozycje, int tekstury, Dictionary<string, int> perSlot, int bc7, string format, int wArchiwum) Statystyki(string zrodloId)
    {
        lock (klucz)
        {
            var poz = Catalog.Garments.Where(p => p.SourceId == zrodloId).ToList();
            var perSlot = poz.GroupBy(p => p.Slot).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
            int tekstury = poz.Sum(p => p.Textures.Count);
            int bc7 = poz.Sum(p => p.Textures.Count(t => t.Format == "BC7"));
            string format = poz.Count == 0 ? null : poz.All(p => p.GameFormat == GameFormat.Enhanced) ? "gen9" : poz.All(p => p.GameFormat == GameFormat.Legacy) ? "legacy" : "mieszany";
            int wArchiwum = poz.Count(p => p.ModelPath != null && p.ModelPath.Contains('|'));
            return (poz.Count, tekstury, perSlot, bc7, format, wArchiwum);
        }
    }

    public Garment ZnajdzPozycje(string id) { lock (klucz) return Catalog.Garments.FirstOrDefault(p => p.Id == id); }

    public TextureInfo ZnajdzTeksture(string sha)
    {
        if (string.IsNullOrEmpty(sha)) return null;
        lock (klucz)
        {
            if (teksturyWgSha == null)
            {
                teksturyWgSha = new Dictionary<string, TextureInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in Catalog.Garments.SelectMany(p => p.Textures)) if (t.Sha256 != null && !teksturyWgSha.ContainsKey(t.Sha256)) teksturyWgSha[t.Sha256] = t;
            }
            return teksturyWgSha.TryGetValue(sha, out var w) ? w : null;
        }
    }

    /// <summary>Dane binarne dla https://duble.data/&lt;kategoria&gt;/&lt;klucz&gt;[?query]: thumb (cache), tex (cache albo generuj),
    /// mesh (klucz = id pozycji, query "w=&lt;litera&gt;" = wariant tekstury; GLB generowany do cache mesh\).</summary>
    public Stream Zasob(string kategoria, string klucz, string query = null)
    {
        var p = Project;
        if (p == null || string.IsNullOrEmpty(klucz) || klucz.Contains("..") || klucz.Contains('/') || klucz.Contains('\\')) return null;
        string plik;
        switch (kategoria)
        {
            case "thumb": plik = Path.Combine(p.ThumbnailFolder, klucz + ".png"); break;
            case "tex":
                plik = Path.Combine(p.TextureFolder, klucz + ".png");
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
            if (poz == null || string.IsNullOrEmpty(poz.ModelPath)) return null;
            var tex = poz.Textures.FirstOrDefault(t => litera != null && string.Equals(ClothingFileName.ParseTexture(t.FileName)?.Letter, litera, StringComparison.OrdinalIgnoreCase))
                      ?? poz.Textures.FirstOrDefault();
            string Krotki(string sha) => string.IsNullOrEmpty(sha) ? "brak" : sha.Length > 16 ? sha.Substring(0, 16) : sha;
            var plik = Path.Combine(Project.MeshFolder, $"{Krotki(poz.ModelSha256)}_{Krotki(tex?.Sha256)}.glb");
            if (File.Exists(plik)) return plik;
            var glb = Podglad3D.Glb(Archiwa, poz, tex != null ? ClothingFileName.ParseTexture(tex.FileName)?.Letter : null);
            Directory.CreateDirectory(Project.MeshFolder);
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
            if (t?.Path == null) return false;
            var odczyt = Archiwa.Read(t.Path);
            var bajty = odczyt.IsSuccess ? odczyt.Value : null;
            if (bajty == null) return false;
            CodeWalkerRuntime.Initialize();
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, bajty, 13);
            var tex = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
            var png = tex == null ? null : TextureDecoder.PngRgba(tex, 1024);
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
