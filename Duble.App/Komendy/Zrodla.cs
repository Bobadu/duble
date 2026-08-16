// Komendy/Zrodla.cs — sources.list/add/remove/toggle/index/cancel/detectGames/pickFolder/pickRpf.
//
// Indeksowanie idzie przez JobRunner (jedno zadanie naraz): dla kazdego zrodla Indeks.Zrodlo z OpcjeIndeksu
// (postep -> zdarzenie "job", anulowanie, przyrostowosc z poprzedniego katalogu, miniatury do cache projektu),
// pozycje dostaja ZrodloId, katalog jest podmieniany per zrodlo (UsunPaczke + Wstaw) i zapisywany na koncu.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duble;

namespace Duble.App.Komendy;

public static class Zrodla
{
    public static void Zarejestruj(Mostek m, Sesja s, JobRunner jr)
    {
        Sesja Wymag() => s.Otwarty ? s : throw new BladMostka("no_project", "brak otwartego projektu");
        object Zrodlo(ZrodloProjektu z)
        {
            var st = s.Statystyki(z.Id);
            bool istnieje = Directory.Exists(z.Sciezka) || File.Exists(z.Sciezka);
            return new
            {
                id = z.Id, nazwa = z.Nazwa, sciezka = z.Sciezka, typ = z.Typ, format = st.format ?? z.Format, wlaczone = z.Wlaczone,
                zaindeksowano = z.Zaindeksowano, istnieje, pozycje = st.pozycje, tekstury = st.tekstury, perSlot = st.perSlot, bc7 = st.bc7,
            };
        }
        object Lista() => new { zrodla = Wymag().Projekt.Zrodla.Select(Zrodlo).ToList() };
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
                int przed = s.Projekt.Zrodla.Count;
                var z = s.Projekt.DodajZrodlo(p);
                if (s.Projekt.Zrodla.Count == przed) { pominiete.Add(p); continue; }   // juz bylo
                dodane.Add(Zrodlo(z));
            }
            if (dodane.Count > 0) { s.Projekt.Zapisz(); Zmienilo(); }
            return new { dodane, pominiete };
        }

        m.Rejestruj("sources.list", _ => Lista());
        m.Rejestruj("sources.add", a => Dodaj(Mostek.Lista(a, "sciezki")));
        m.Rejestruj("sources.pickFolder", _ => { Wymag(); var f = m.Dialogi.WybierzFolder(null, null); return f == null ? new { dodane = new List<object>(), pominiete = new List<string>() } : Dodaj(new[] { f }); });
        m.Rejestruj("sources.pickRpf", _ => { Wymag(); var f = m.Dialogi.WybierzPliki(null, "rpf", true, null); return f == null || f.Length == 0 ? new { dodane = new List<object>(), pominiete = new List<string>() } : Dodaj(f); });
        m.Rejestruj("sources.remove", a =>
        {
            var id = Mostek.Tekst(a, "id", true);
            var z = Wymag().Projekt.Zrodla.Find(x => x.Id == id) ?? throw new BladMostka("not_found", id);
            s.Projekt.Zrodla.Remove(z);
            s.ZmienKatalog(k => { k.Pozycje.RemoveAll(p => p.ZrodloId == id); k.Zrodla.Remove(z.Nazwa); });
            s.Zapisz();
            Zmienilo(id);
            return new { };
        });
        m.Rejestruj("sources.toggle", a =>
        {
            var id = Mostek.Tekst(a, "id", true);
            var z = Wymag().Projekt.Zrodla.Find(x => x.Id == id) ?? throw new BladMostka("not_found", id);
            z.Wlaczone = Mostek.Flaga(a, "wlaczone", !z.Wlaczone);
            s.Projekt.Zapisz();
            Zmienilo(id);
            return new { wlaczone = z.Wlaczone };
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
            var zrodla = s.Projekt.Zrodla.Where(z => ids.Count == 0 ? z.Wlaczone : ids.Contains(z.Id)).ToList();
            if (zrodla.Count == 0) return new { uruchomiono = false };
            var opis = string.Join(", ", zrodla.Select(z => z.Nazwa));
            bool ok = jr.SprobujUruchom("indeks", opis, async (ct, postep) =>
            {
                await Task.Yield();
                foreach (var z in zrodla)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Directory.Exists(z.Sciezka) && !File.Exists(z.Sciezka)) continue;
                    Katalog poprzedni = null;
                    s.ZmienKatalog(k => poprzedni = new Katalog { Pozycje = k.Pozycje.ToList() });
                    var opcje = new OpcjeIndeksu
                    {
                        Log = _ => { }, Anuluj = ct, Poprzedni = poprzedni, Wymus = wymus,
                        FolderMiniatur = s.Projekt.FolderMiniatur,
                        Postep = p => postep(new Postep(p.Etap, p.Zrobione, p.Wszystkie, z.Nazwa)),
                    };
                    postep(new Postep("start", 0, 0, z.Nazwa));
                    var pozycje = Indeks.Zrodlo(z.Sciezka, z.Nazwa, opcje);
                    foreach (var p in pozycje) p.ZrodloId = z.Id;
                    s.ZmienKatalog(k => { k.UsunPaczke(z.Nazwa); k.Wstaw(pozycje); k.Zrodla[z.Nazwa] = z.Sciezka; });
                    z.Zaindeksowano = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    z.Format = pozycje.Count == 0 ? null : pozycje.All(p => p.Gen9) ? "gen9" : pozycje.All(p => !p.Gen9) ? "legacy" : "mieszany";
                    Zmienilo(z.Id);
                }
                // po indeksowaniu od razu porownanie — widok Duplikaty ma byc zawsze aktualny
                postep(new Postep("porownaj", 0, 0, null));
                s.Porownaj(ct, postep);
                s.Zapisz();
                Zmienilo();
                m.Zdarzenie("compare.done", new { podsumowanie = s.Podsumowanie() });
            });
            if (!ok) throw new BladMostka("busy", "trwa inne zadanie");
            return new { uruchomiono = true, zrodla = zrodla.Select(z => z.Id).ToList() };
        });
    }
}
