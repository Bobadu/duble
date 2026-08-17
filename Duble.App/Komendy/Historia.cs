// Komendy/Historia.cs — history.list/get/undo (dzienniki zastosowan = UndoLog w <cache>\historia\*.json), report.exportHtml/exportCsv.
//
// Cofniecie idzie przez JobRunner "cofnij": ApplyPlanner.Cofnij (calosc albo wybrane pozycje) -> zapis cofki -> ponowne
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

        object Wpis(string plik, UndoLog c, bool szczegoly)
        {
            var o = new Dictionary<string, object>
            {
                ["plik"] = plik, ["nazwa"] = Path.GetFileName(plik), ["kiedy"] = c.When, ["opis"] = c.Description,
                ["pozycje"] = c.Garments.Count, ["pliki"] = c.Moves.Count, ["bajty"] = c.Bytes,
                ["kosze"] = c.Garments.Select(p => p.BinFolder).Where(k => k != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ["wspoldzielone"] = c.SharedCount, ["wArchiwum"] = c.InArchiveCount, ["brakujace"] = c.MissingCount,
                ["cofnieto"] = c.UndoneAt, ["czesciowo"] = c.PartlyUndone, ["moznaCofnac"] = c.CanUndo, ["przerwano"] = c.Aborted, ["blad"] = c.Error,
            };
            if (szczegoly)
                o["lista"] = c.Garments.Select(p => new
                {
                    id = p.Id, nazwa = p.Name, zrodlo = p.SourceName, zrodloId = p.SourceId, kosz = p.BinFolder,
                    pliki = c.Moves.Where(r => r.GarmentId == p.Id).Select(r => new { z = r.From, @do = r.To, bajty = r.Bytes, cofniety = r.Undone, jest = File.Exists(r.To) }).ToList(),
                    moznaCofnac = c.CanRestoreGarment(p.Id),
                }).ToList();
            return o;
        }

        m.Rejestruj("history.list", _ =>
        {
            Wymag();
            var wpisy = new List<object>();
            foreach (var plik in s.PlikiHistorii())
            {
                var wczytana = s.Cofki.Load(plik);
                if (wczytana.IsSuccess) wpisy.Add(Wpis(plik, wczytana.Value, false));
                else wpisy.Add(new { plik, nazwa = Path.GetFileName(plik), blad = wczytana.Error.Message, uszkodzony = true });
            }
            return new { wpisy };
        });

        m.Rejestruj("history.get", a =>
        {
            var plik = Plik(Mostek.Tekst(a, "plik", true));
            var wczytana = s.Cofki.Load(plik);
            if (wczytana.IsFailure) throw new BladMostka("io", wczytana.Error.Message);
            return new { wpis = Wpis(plik, wczytana.Value, true) };
        });

        m.Rejestruj("history.undo", a =>
        {
            var plik = Plik(Mostek.Tekst(a, "plik", true));
            var pozycje = Mostek.Lista(a, "pozycje");
            var wczytana = s.Cofki.Load(plik);
            if (wczytana.IsFailure) throw new BladMostka("io", wczytana.Error.Message);
            var cofka = wczytana.Value;
            if (!cofka.CanUndo) return new { uruchomiono = false, wrocilo = 0, pominieto = 0 };
            bool ok = jr.SprobujUruchom("cofnij", Path.GetFileName(plik), async (ct, postep) =>
            {
                await Task.Yield();
                int wrocilo, pominieto;
                try { (wrocilo, pominieto) = s.Wykonawca.Undo(cofka, pozycje.Count > 0 ? pozycje : null, new Progress<ProgressReport>(postep)); }
                // the log on disk is written even after an error: it is the record of what moved back
                finally { s.Cofki.Save(cofka, plik); m.Zdarzenie("history.changed", new { plik }); }
                var ids = pozycje.Count > 0 ? new HashSet<string>(pozycje) : null;
                var dotkniete = s.Project.Sources.Where(z => cofka.Garments.Any(p => p.SourceId == z.Id && (ids == null || ids.Contains(p.Id)))).ToList();
                if (dotkniete.Count > 0) Zrodla.Indeksuj(s, m, dotkniete, false, ct, postep);
                Zrodla.PorownajIZapisz(s, m, ct, postep);
                m.Zdarzenie("undo.done", new { plik, wrocilo, pominieto, cofnieto = cofka.UndoneAt });
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
