// Komendy/Zrodla.cs — sources.list/add/remove/toggle/index/cancel/detectGames/pickFolder/pickRpf/unpack.
//
// Indeksowanie idzie przez JobRunner (jedno zadanie naraz): dla kazdego zrodla Indeks.SourceName z OpcjeIndeksu
// (postep -> zdarzenie "job", anulowanie, przyrostowosc z poprzedniego katalogu, miniatury do cache projektu),
// pozycje dostaja ZrodloId, katalog jest podmieniany per zrodlo (UsunPaczke + Wstaw) i zapisywany na koncu.
// Indeksuj()/PorownajIZapisz() sa wspolne: uzywa ich tez Zastosuj/Cofnij (ponowne indeksowanie dotknietych zrodel) i Rozpakuj.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Duble.App.Komendy;

public static class Zrodla
{
    /// <summary>Krotkie id zrodla; zostaje w pliku projektu i w katalogu (Garment.SourceId).</summary>
    static string NoweId() => Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>Indeksuje podane zrodla (przyrostowo; wymus = od nowa) i podmienia ich pozycje w katalogu sesji. Bez porownania.</summary>
    public static void Indeksuj(Sesja s, Mostek m, IList<ProjectSource> zrodla, bool wymus, CancellationToken ct, Action<ProgressReport> postep)
    {
        foreach (var z in zrodla)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(z.Path) && !File.Exists(z.Path)) continue;
            Catalog poprzedni = null;
            s.ZmienKatalog(k => poprzedni = new Catalog { Garments = k.Garments.ToList() });
            var opcje = new IndexOptions
            {
                PreviousCatalog = poprzedni,
                Force = wymus,
                ThumbnailFolder = s.Project.ThumbnailFolder,
            };
            postep(new ProgressReport("start", 0, 0, z.Name));
            var raport = s.Indeksator.Index(z.Path, z.Name, opcje,
                new Progress<ProgressReport>(p => postep(new ProgressReport(p.Stage, p.Done, p.Total, z.Name))), ct);
            if (raport.IsFailure) throw new BladMostka("io", raport.Error.Message);
            var pozycje = raport.Value.Garments;
            foreach (var p in pozycje) p.SourceId = z.Id;
            s.ZmienKatalog(k => { k.RemovePack(z.Name); k.Upsert(pozycje); k.Sources[z.Name] = z.Path; });
            z.IndexedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            z.Format = pozycje.Count == 0 ? SourceFormat.Unknown
                : pozycje.All(p => p.GameFormat == GameFormat.Enhanced) ? SourceFormat.Enhanced
                : pozycje.All(p => p.GameFormat == GameFormat.Legacy) ? SourceFormat.Legacy : SourceFormat.Mixed;
            m.Zdarzenie("sources.changed", new { id = z.Id });
            m.Zdarzenie("project.changed", new { projekt = s.Podsumowanie() });
        }
    }

    /// <summary>Porownanie + zapis projektu/katalogu/wyniku + zdarzenia (widok Duplikaty ma byc zawsze aktualny).</summary>
    public static void PorownajIZapisz(Sesja s, Mostek m, CancellationToken ct, Action<ProgressReport> postep)
    {
        postep(new ProgressReport("compare", 0, 0, null));
        s.Porownaj(ct, postep);
        s.Zapisz();
        m.Zdarzenie("sources.changed", new { id = (string)null });
        m.Zdarzenie("project.changed", new { projekt = s.Podsumowanie() });
        m.Zdarzenie("compare.done", new { podsumowanie = s.Podsumowanie() });
    }

    public static void Zarejestruj(Mostek m, Sesja s, JobRunner jr)
    {
        Sesja Wymag() => s.Otwarty ? s : throw new BladMostka("no_project", "brak otwartego projektu");
        object Zrodlo(ProjectSource z)
        {
            var st = s.Statystyki(z.Id);
            bool istnieje = Directory.Exists(z.Path) || File.Exists(z.Path);
            return new
            {
                id = z.Id, nazwa = z.Name, sciezka = z.Path, typ = z.Kind.ToLabel(), format = st.format ?? z.Format.ToLabel(), wlaczone = z.Enabled,
                zaindeksowano = z.IndexedAt, istnieje, pozycje = st.pozycje, tekstury = st.tekstury, perSlot = st.perSlot, bc7 = st.bc7,
                archiwa = st.wArchiwum, kosz = s.KoszDla(z),
            };
        }
        object Lista() => new { zrodla = Wymag().Project.Sources.Select(Zrodlo).ToList() };
        void Zmienilo(string id = null)
        {
            m.Zdarzenie("sources.changed", new { id });
            m.Zdarzenie("project.changed", new { projekt = s.Podsumowanie() });
        }
        object Dodaj(IEnumerable<string> sciezki)
        {
            Wymag();
            var dodane = new List<object>(); var pominiete = new List<string>();
            foreach (var p in sciezki.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (!Directory.Exists(p) && !File.Exists(p)) { pominiete.Add(p); continue; }
                if (File.Exists(p) && !p.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) { pominiete.Add(p); continue; }
                int przed = s.Project.Sources.Count;
                var z = s.Project.AddSource(p, NoweId());
                if (s.Project.Sources.Count == przed) { pominiete.Add(p); continue; }   // juz bylo
                dodane.Add(Zrodlo(z));
            }
            if (dodane.Count > 0) { s.ZapiszProjekt(); Zmienilo(); }
            return new { dodane, pominiete };
        }

        m.Rejestruj("sources.list", _ => Lista());
        m.Rejestruj("sources.add", a => Dodaj(Mostek.Lista(a, "sciezki")));
        m.Rejestruj("sources.pickFolder", _ => { Wymag(); var f = m.Dialogi.WybierzFolder(null, null); return f == null ? new { dodane = new List<object>(), pominiete = new List<string>() } : Dodaj(new[] { f }); });
        m.Rejestruj("sources.pickRpf", _ => { Wymag(); var f = m.Dialogi.WybierzPliki(null, "rpf", true, null); return f == null || f.Length == 0 ? new { dodane = new List<object>(), pominiete = new List<string>() } : Dodaj(f); });
        m.Rejestruj("sources.remove", a =>
        {
            var id = Mostek.Text(a, "id", true);
            var z = Wymag().Project.Sources.Find(x => x.Id == id) ?? throw new BladMostka("not_found", id);
            s.Project.Sources.Remove(z);
            s.ZmienKatalog(k => { k.Garments.RemoveAll(p => p.SourceId == id); k.Sources.Remove(z.Name); });
            s.Zapisz();
            Zmienilo(id);
            return new { };
        });
        m.Rejestruj("sources.toggle", a =>
        {
            var id = Mostek.Text(a, "id", true);
            var z = Wymag().Project.Sources.Find(x => x.Id == id) ?? throw new BladMostka("not_found", id);
            z.Enabled = Mostek.Flaga(a, "wlaczone", !z.Enabled);
            s.ZapiszProjekt();
            Zmienilo(id);
            return new { wlaczone = z.Enabled };
        });
        m.Rejestruj("sources.cancel", _ => { jr.Anuluj(); return new { }; });
        m.Rejestruj("sources.detectGames", _ => new
        {
            gry = Gry.Wykryj().Select(g => new { gra = g.Gra, sciezka = g.Sciezka, propozycje = g.Propozycje.Select(p => new { nazwa = p.Nazwa, sciezka = p.Sciezka, typ = p.Typ }).ToList() }).ToList(),
        });
        m.Rejestruj("sources.index", a =>
        {
            Wymag();
            var ids = Mostek.Lista(a, "ids"); bool wymus = Mostek.Flaga(a, "wymus");
            var zrodla = s.Project.Sources.Where(z => ids.Count == 0 ? z.Enabled : ids.Contains(z.Id)).ToList();
            if (zrodla.Count == 0) return new { uruchomiono = false };
            var opis = string.Join(", ", zrodla.Select(z => z.Name));
            bool ok = jr.SprobujUruchom("indeks", opis, async (ct, postep) =>
            {
                await Task.Yield();
                Indeksuj(s, m, zrodla, wymus, ct, postep);
                // po indeksowaniu od razu porownanie — widok Duplikaty ma byc zawsze aktualny
                PorownajIZapisz(s, m, ct, postep);
            });
            if (!ok) throw new BladMostka("busy", "trwa inne zadanie");
            return new { uruchomiono = true, zrodla = zrodla.Select(z => z.Id).ToList() };
        });

        // Rozpakuj do folderu: kopia zrodla z rozlozonymi archiwami (RpfArchiveExtractor), opcjonalnie dodana jako nowe zrodlo (oryginal wylaczony)
        m.Rejestruj("sources.unpack", a =>
        {
            Wymag();
            var id = Mostek.Text(a, "id", true);
            var folder = Mostek.Text(a, "folder", true);
            bool dodaj = Mostek.Flaga(a, "dodajZrodlo", true);
            var z = s.Project.Sources.Find(x => x.Id == id) ?? throw new BladMostka("not_found", id);
            if (!Directory.Exists(z.Path) && !File.Exists(z.Path)) throw new BladMostka("not_found", z.Path);
            var cel = Path.Combine(folder, NazwaFolderuKopii(z));
            if (Directory.Exists(cel) && Directory.EnumerateFileSystemEntries(cel).Any()) throw new BladMostka("io", "folder juz istnieje i nie jest pusty: " + cel);
            bool ok = jr.SprobujUruchom("rozpakuj", z.Name, async (ct, postep) =>
            {
                await Task.Yield();
                var w = s.Rozpakowywacz.ExtractSource(z.Path, cel, new Progress<ProgressReport>(postep), ct);
                string dodano = null;
                if (dodaj && w.Files > 0)
                {
                    var nowe = s.Project.AddSource(cel, NoweId());
                    z.Enabled = false;
                    dodano = nowe.Id;
                    s.ZapiszProjekt();
                    m.Zdarzenie("sources.changed", new { id = (string)null });
                    Indeksuj(s, m, new[] { nowe }, false, ct, postep);
                    PorownajIZapisz(s, m, ct, postep);
                }
                m.Zdarzenie("unpack.done", new { id, folder = cel, pliki = w.Files, archiwa = w.Archives, bajty = w.Bytes, bledy = w.Errors.Take(20).ToList(), dodano });
            });
            if (!ok) throw new BladMostka("busy", "trwa inne zadanie");
            return new { uruchomiono = true, folder = cel };
        });
    }

    /// <summary>Nazwa podfolderu kopii: dla `dlc.rpf` nazwa zrodla (= folder paczki), dla innego archiwum nazwa pliku (folder `x.rpf`), dla folderu nazwa zrodla.</summary>
    public static string NazwaFolderuKopii(ProjectSource z)
    {
        if (z.Kind == SourceKind.Archive)
        {
            var plik = Path.GetFileName(z.Path);
            return plik.Equals("dlc.rpf", StringComparison.OrdinalIgnoreCase) ? z.Name : plik;
        }
        return z.Name;
    }
}
