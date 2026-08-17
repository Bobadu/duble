// Komendy/Projekty.cs — project.recent/new/open/get/save/close/pickOpen/pickFolder/forget.
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Duble.App.Komendy;

public static class Projekty
{
    static readonly Regex Niedozwolone = new(@"[\\/:*?""<>|]+", RegexOptions.Compiled);

    public static void Zarejestruj(Mostek m, Sesja s)
    {
        void Otwarto()
        {
            m.Ustawienia.ZanotujProjekt(s.Project.Path, s.Project.Name);
            try { m.Ustawienia.Zapisz(m.PlikUstawien); } catch { }
            m.Zdarzenie("project.opened", new { projekt = s.Podsumowanie() });
        }

        m.Rejestruj("project.recent", _ => new
        {
            ostatnie = m.Ustawienia.Ostatnie.Select(o => new { o.Sciezka, o.Name, o.Ostatnio, istnieje = File.Exists(o.Sciezka) }).ToList(),
            folderDomyslny = Ustawienia.FolderProjektow,
        });
        m.Rejestruj("project.get", _ => new { projekt = s.Podsumowanie() });
        m.Rejestruj("project.new", a =>
        {
            var nazwa = (Mostek.Tekst(a, "nazwa", true) ?? "").Trim();
            if (nazwa.Length == 0) throw new BladMostka("bad_args", "pusta nazwa");
            var folder = Mostek.Tekst(a, "folder");
            if (string.IsNullOrWhiteSpace(folder)) folder = Ustawienia.FolderProjektow;
            var plikNazwa = Regex.Replace(Niedozwolone.Replace(nazwa, " "), @"\s+", " ").Trim();
            if (plikNazwa.Length == 0) plikNazwa = "Project";
            var sciezka = Path.Combine(folder, plikNazwa + ".duble");
            if (File.Exists(sciezka)) throw new BladMostka("io", "plik juz istnieje: " + sciezka);
            try { Directory.CreateDirectory(folder); s.Nowy(nazwa, sciezka); }
            catch (BladMostka) { throw; }
            catch (Exception e) { throw new BladMostka("io", e.Message); }
            Otwarto();
            return new { projekt = s.Podsumowanie() };
        });
        m.Rejestruj("project.open", a =>
        {
            var sciezka = Mostek.Tekst(a, "sciezka", true);
            if (!File.Exists(sciezka)) throw new BladMostka("not_found", sciezka);
            try { s.Otworz(sciezka); }
            catch (Exception e) { throw new BladMostka("io", e.Message); }
            Otwarto();
            return new { projekt = s.Podsumowanie() };
        });
        m.Rejestruj("project.pickOpen", _ =>
        {
            var pliki = m.Dialogi.WybierzPliki(null, "duble", false, Ustawienia.FolderProjektow);
            if (pliki == null || pliki.Length == 0) return new { projekt = (object)null };
            try { s.Otworz(pliki[0]); }
            catch (Exception e) { throw new BladMostka("io", e.Message); }
            Otwarto();
            return new { projekt = s.Podsumowanie() };
        });
        m.Rejestruj("project.pickFolder", _ => new { sciezka = m.Dialogi.WybierzFolder(null, Ustawienia.FolderProjektow) });
        m.Rejestruj("project.save", _ => { if (!s.Otwarty) throw new BladMostka("no_project", "brak projektu"); s.Zapisz(); return new { }; });
        m.Rejestruj("project.close", _ => { s.Zamknij(); m.Zdarzenie("project.closed", new { }); return new { }; });
        m.Rejestruj("project.forget", a =>
        {
            var sciezka = Mostek.Tekst(a, "sciezka", true);
            m.Ustawienia.Ostatnie.RemoveAll(o => string.Equals(o.Sciezka, sciezka, StringComparison.OrdinalIgnoreCase));
            try { m.Ustawienia.Zapisz(m.PlikUstawien); } catch { }
            return new { };
        });
    }
}
