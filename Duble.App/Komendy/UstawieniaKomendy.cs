// Komendy/UstawieniaKomendy.cs — ustawienia PROJEKTU (kosz, progi porownania), cache projektu, kalibracja.
//
// project.settings.get/set/resetProgi: kosz (null = _odrzucone obok zrodla) i progi (czesciowe: podane pola nadpisuja biezace;
// walidacja Progi.Sprawdz -> bad_args z lista pol). Zmiana progow = ponowne porownanie w tle (decyzje zostaja, PrzeniesDecyzje).
// cache.clear: tylko tex\ i mesh\ (odtwarzane na zadanie). calibrate.run: JobRunner "kalibracja" -> Kalibracja.Policz na
// pozycjach wlaczonych zrodel -> zdarzenie calibrate.done {wynik} (rozklady z kubelkami do wykresow, propozycja progow).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.Core;

namespace Duble.App.Komendy;

public static class UstawieniaKomendy
{
    static object ProgiJson(Progi p) => new
    {
        geoIdentyczna = p.GeoIdentyczna, geoPodobna = p.GeoPodobna, geoPodobnaTri = p.GeoPodobnaTri, geoPodobnaBbox = p.GeoPodobnaBbox,
        texPHash = p.TexPHash, texKolor = p.TexKolor, texWariancjaMin = p.TexWariancjaMin, texKolorPlaska = p.TexKolorPlaska,
        pelnePokrycie = p.PelnePokrycie, czesciowePokrycie = p.CzesciowePokrycie,
    };

    /// <summary>Nadpisuje w `p` pola podane w JSON (camelCase jak w ProgiJson). Zwraca liczbe zmienionych pol.</summary>
    public static int WczytajProgi(Progi p, JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) return 0;
        int n = 0;
        double D(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : double.NaN;
        foreach (var w in a.EnumerateObject())
        {
            var v = D(w.Value); if (double.IsNaN(v)) continue;
            switch (w.Name)
            {
                case "geoIdentyczna": p.GeoIdentyczna = v; break;
                case "geoPodobna": p.GeoPodobna = v; break;
                case "geoPodobnaTri": p.GeoPodobnaTri = v; break;
                case "geoPodobnaBbox": p.GeoPodobnaBbox = v; break;
                case "texPHash": p.TexPHash = (int)Math.Round(v); break;
                case "texKolor": p.TexKolor = v; break;
                case "texWariancjaMin": p.TexWariancjaMin = (float)v; break;
                case "texKolorPlaska": p.TexKolorPlaska = v; break;
                case "pelnePokrycie": p.PelnePokrycie = v; break;
                case "czesciowePokrycie": p.CzesciowePokrycie = v; break;
                default: continue;
            }
            n++;
        }
        return n;
    }

    public static void Zarejestruj(Mostek m, Sesja s, JobRunner jr)
    {
        Sesja Wymag() => s.Otwarty ? s : throw new BladMostka("no_project", "brak otwartego projektu");

        object Stan(bool? porownanie = null)
        {
            var pr = s.Projekt;
            var progi = pr.Ustawienia?.Progi;
            var cache = s.RozmiarCache();
            return new
            {
                kosz = pr.Ustawienia?.Kosz,
                progi = ProgiJson(progi ?? Progi.Domyslne), progiDomyslne = ProgiJson(Progi.Domyslne),
                progiZmienione = progi != null && !progi.Rowne(Progi.Domyslne),
                cache = cache.ToDictionary(k => k.Key, k => new { pliki = k.Value.pliki, bajty = k.Value.bajty }),
                folderCache = pr.FolderCache,
                zrodla = pr.Zrodla.Count, pozycje = s.Katalog.Pozycje.Count,
                porownanie,   // true = ruszylo ponowne porownanie, false = zajety, null = niepotrzebne
            };
        }

        bool Porownaj() => jr.SprobujUruchom("porownaj", s.Projekt.Nazwa, async (ct, postep) =>
        {
            await Task.Yield();
            Zrodla.PorownajIZapisz(s, m, ct, postep);
        });

        m.Rejestruj("project.settings.get", _ => { Wymag(); return Stan(); });

        m.Rejestruj("project.settings.set", a =>
        {
            Wymag();
            var pr = s.Projekt;
            pr.Ustawienia ??= new Duble.Core.UstawieniaProjektu();
            bool zmianaProgow = false;
            if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("kosz", out var k))
                pr.Ustawienia.Kosz = k.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(k.GetString()) ? k.GetString() : null;
            if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("progi", out var pj) && pj.ValueKind == JsonValueKind.Object)
            {
                var nowe = (pr.Ustawienia.Progi ?? Progi.Domyslne).Kopia();
                WczytajProgi(nowe, pj);
                var bledy = nowe.Sprawdz();
                if (bledy.Count > 0) throw new BladMostka("bad_args", string.Join(",", bledy));
                var stare = pr.Ustawienia.Progi ?? Progi.Domyslne;
                zmianaProgow = !nowe.Rowne(stare);
                pr.Ustawienia.Progi = nowe.Rowne(Progi.Domyslne) ? null : nowe;
            }
            pr.Zapisz();
            m.Zdarzenie("settings.changed", new { zrodlo = "project" });
            bool? por = zmianaProgow && s.Wynik != null ? Porownaj() : null;
            return Stan(por);
        });

        m.Rejestruj("project.settings.resetProgi", _ =>
        {
            Wymag();
            var pr = s.Projekt;
            bool bylo = pr.Ustawienia?.Progi != null && !pr.Ustawienia.Progi.Rowne(Progi.Domyslne);
            if (pr.Ustawienia != null) pr.Ustawienia.Progi = null;
            pr.Zapisz();
            m.Zdarzenie("settings.changed", new { zrodlo = "project" });
            bool? por = bylo && s.Wynik != null ? Porownaj() : null;
            return Stan(por);
        });

        m.Rejestruj("cache.clear", a =>
        {
            Wymag();
            bool tex = Mostek.Flaga(a, "tex", true), mesh = Mostek.Flaga(a, "mesh", true);
            var (pliki, bajty) = s.WyczyscCache(tex, mesh);
            m.Zdarzenie("settings.changed", new { zrodlo = "cache" });
            return new { usunieto = pliki, bajty, cache = s.RozmiarCache().ToDictionary(k => k.Key, k => new { pliki = k.Value.pliki, bajty = k.Value.bajty }) };
        });

        m.Rejestruj("calibrate.run", _ =>
        {
            Wymag();
            var katalog = s.KatalogWlaczony();
            if (katalog.Pozycje.Count(p => p.Geo?.Hist != null && p.Geo.Wierzcholki > 0) < 2) throw new BladMostka("not_found", "za malo pozycji");
            var progi = s.ProgiProjektu;
            bool ok = jr.SprobujUruchom("kalibracja", s.Projekt.Nazwa, async (ct, postep) =>
            {
                await Task.Yield();
                postep(new Postep("kalibracja", 0, 0, null));
                var w = Kalibracja.Policz(katalog, progi, ct);
                m.Zdarzenie("calibrate.done", new { wynik = w });
            });
            if (!ok) throw new BladMostka("busy", "trwa inne zadanie");
            return new { uruchomiono = true };
        });
    }
}
