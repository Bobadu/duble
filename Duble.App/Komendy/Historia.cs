// Komendy/Historia.cs — history.list/get/undo (dzienniki zastosowan = Cofka w <cache>\historia\*.json), report.exportHtml/exportCsv.
//
// Cofniecie idzie przez JobRunner "cofnij": Zastosowanie.Cofnij (calosc albo wybrane pozycje) -> zapis cofki -> ponowne
// indeksowanie zrodel tych pozycji -> porownanie; zdarzenia undo.done + history.changed.
// Raport HTML (Raport.Zbuduj, jezyk UI, decyzje z projektu) idzie przez JobRunner "raport" (dekoduje tekstury -> sekundy);
// CSV jest natychmiastowe.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Duble.App.Komendy;

public static class Historia
{
    public static void Zarejestruj(Mostek m, Sesja s, JobRunner jr)
    {
        Sesja Wymag() => s.Otwarty ? s : throw new BladMostka("no_project", "brak otwartego projektu");

        string Plik(string nazwa)
        {
            // tylko pliki z folderu historii projektu (nazwa albo pelna sciezka)
            var pr = Wymag().Project;
            var kandydat = Path.IsPathRooted(nazwa) ? nazwa : Path.Combine(pr.HistoryFolder, nazwa);
            var pelny = Path.GetFullPath(kandydat);
            if (!pelny.StartsWith(Path.GetFullPath(pr.HistoryFolder), StringComparison.OrdinalIgnoreCase) || !File.Exists(pelny)) throw new BladMostka("not_found", nazwa);
            return pelny;
        }

        object Wpis(string plik, Cofka c, bool szczegoly)
        {
            var o = new Dictionary<string, object>
            {
                ["plik"] = plik, ["nazwa"] = Path.GetFileName(plik), ["kiedy"] = c.Kiedy, ["opis"] = c.Opis,
                ["pozycje"] = c.Pozycje.Count, ["pliki"] = c.Ruchy.Count, ["bajty"] = c.Bajty,
                ["kosze"] = c.Pozycje.Select(p => p.Kosz).Where(k => k != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ["wspoldzielone"] = c.Wspoldzielone, ["wArchiwum"] = c.WArchiwum, ["brakujace"] = c.Brakujace,
                ["cofnieto"] = c.Cofnieto, ["czesciowo"] = c.CzesciowoCofnieta, ["moznaCofnac"] = c.MoznaCofnac, ["przerwano"] = c.Przerwano, ["blad"] = c.Blad,
            };
            if (szczegoly)
                o["lista"] = c.Pozycje.Select(p => new
                {
                    id = p.Id, nazwa = p.Nazwa, zrodlo = p.Zrodlo, zrodloId = p.ZrodloId, kosz = p.Kosz,
                    pliki = c.Ruchy.Where(r => r.GarmentId == p.Id).Select(r => new { z = r.Z, @do = r.Do, bajty = r.Bajty, cofniety = r.Cofniety, jest = File.Exists(r.Do) }).ToList(),
                    moznaCofnac = c.MoznaCofnacPozycje(p.Id),
                }).ToList();
            return o;
        }

        m.Rejestruj("history.list", _ =>
        {
            Wymag();
            var wpisy = new List<object>();
            foreach (var plik in s.PlikiHistorii())
            {
                try { wpisy.Add(Wpis(plik, Cofka.Wczytaj(plik), false)); }
                catch (Exception e) { wpisy.Add(new { plik, nazwa = Path.GetFileName(plik), blad = e.Message, uszkodzony = true }); }
            }
            return new { wpisy };
        });

        m.Rejestruj("history.get", a =>
        {
            var plik = Plik(Mostek.Tekst(a, "plik", true));
            return new { wpis = Wpis(plik, Cofka.Wczytaj(plik), true) };
        });

        m.Rejestruj("history.undo", a =>
        {
            var plik = Plik(Mostek.Tekst(a, "plik", true));
            var pozycje = Mostek.Lista(a, "pozycje");
            var cofka = Cofka.Wczytaj(plik);
            if (!cofka.MoznaCofnac) return new { uruchomiono = false, wrocilo = 0, pominieto = 0 };
            bool ok = jr.SprobujUruchom("cofnij", Path.GetFileName(plik), async (ct, postep) =>
            {
                await Task.Yield();
                int wrocilo, pominieto;
                try { (wrocilo, pominieto) = Zastosowanie.Cofnij(cofka, pozycje.Count > 0 ? pozycje : null, postep); }
                finally { cofka.Zapisz(plik); m.Zdarzenie("history.changed", new { plik }); }   // stan cofki na dysku takze po bledzie
                var ids = pozycje.Count > 0 ? new HashSet<string>(pozycje) : null;
                var dotkniete = s.Project.Sources.Where(z => cofka.Pozycje.Any(p => p.ZrodloId == z.Id && (ids == null || ids.Contains(p.Id)))).ToList();
                if (dotkniete.Count > 0) Zrodla.Indeksuj(s, m, dotkniete, false, ct, postep);
                Zrodla.PorownajIZapisz(s, m, ct, postep);
                m.Zdarzenie("undo.done", new { plik, wrocilo, pominieto, cofnieto = cofka.Cofnieto });
            });
            if (!ok) throw new BladMostka("busy", "trwa inne zadanie");
            return new { uruchomiono = true };
        });

        // ---- eksport ----
        string Jezyk() => m.Ustawienia?.JezykEfektywny ?? "pl";
        Func<DuplicateGroup, Resolution> Rozstrzygnij() => g => Grupy.Rozstrzygnij(s, g);
        string Docelowy(string sciezka, string filtr, string domyslna)
        {
            if (!string.IsNullOrWhiteSpace(sciezka)) return sciezka;
            var folder = Path.GetDirectoryName(s.Project.Path);
            return m.Dialogi.ZapiszPlik(null, filtr, domyslna, folder);
        }
        string BezpiecznaNazwa(string n) => string.Concat((n ?? "projekt").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

        m.Rejestruj("report.exportHtml", a =>
        {
            Wymag();
            if (s.Wynik == null) throw new BladMostka("not_found", "brak porownania");
            var plik = Docelowy(Mostek.Tekst(a, "sciezka"), "html", BezpiecznaNazwa(s.Project.Name) + "-raport.html");
            if (plik == null) return new { anulowano = true };
            var jezyk = Jezyk(); var rozstrzygnij = Rozstrzygnij(); var tytul = s.Project.Name;
            bool ok = jr.SprobujUruchom("raport", Path.GetFileName(plik), async (ct, postep) =>
            {
                await Task.Yield();
                postep(new ProgressReport("raport", 0, 0, Path.GetFileName(plik)));
                Raport.Zbuduj(s.Archiwa, s.Catalog, s.Wynik, plik, _ => { }, jezyk, rozstrzygnij, tytul);
                m.Zdarzenie("report.done", new { plik, typ = "html" });
            });
            if (!ok) throw new BladMostka("busy", "trwa inne zadanie");
            return new { uruchomiono = true, plik };
        });

        m.Rejestruj("report.exportCsv", a =>
        {
            Wymag();
            if (s.Wynik == null) throw new BladMostka("not_found", "brak porownania");
            var plik = Docelowy(Mostek.Tekst(a, "sciezka"), "csv", BezpiecznaNazwa(s.Project.Name) + "-grupy.csv");
            if (plik == null) return new { anulowano = true };
            try
            {
                var csv = Raport.Csv(s.Catalog, s.Wynik, Rozstrzygnij(), Jezyk());
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(plik)));
                File.WriteAllText(plik, csv, new System.Text.UTF8Encoding(false));   // BOM jest juz w tresci
            }
            catch (Exception e) { throw new BladMostka("io", e.Message); }
            m.Zdarzenie("report.done", new { plik, typ = "csv" });
            return new { plik };
        });
    }
}
