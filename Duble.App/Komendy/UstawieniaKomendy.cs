// Komendy/UstawieniaKomendy.cs — ustawienia PROJEKTU (kosz, progi porownania), cache projektu, kalibracja.
//
// project.settings.get/set/resetProgi: kosz (null = _odrzucone obok zrodla) i progi (czesciowe: podane pola nadpisuja biezace;
// walidacja Thresholds.Sprawdz -> bad_args z lista pol). Zmiana progow = ponowne porownanie w tle (decyzje zostaja, PrzeniesDecyzje).
// cache.clear: tylko tex\ i mesh\ (odtwarzane na zadanie). calibrate.run: JobRunner "kalibracja" -> Kalibracja.Policz na
// pozycjach wlaczonych zrodel -> zdarzenie calibrate.done {wynik} (rozklady z kubelkami do wykresow, propozycja progow).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Duble.App.Komendy;

public static class UstawieniaKomendy
{
    static object ProgiJson(Thresholds p) => new
    {
        geometryIdentical = p.GeometryIdentical, geometrySimilar = p.GeometrySimilar, geometryTriangleTolerance = p.GeometryTriangleTolerance, geometryBoundsTolerance = p.GeometryBoundsTolerance,
        textureHashDistance = p.TextureHashDistance, textureColorDistance = p.TextureColorDistance, flatTextureVariance = p.FlatTextureVariance, flatTextureColorDistance = p.FlatTextureColorDistance,
        fullCoverage = p.FullCoverage, partialCoverage = p.PartialCoverage,
    };

    /// <summary>Nadpisuje w `p` pola podane w JSON (camelCase jak w ProgiJson). Zwraca liczbe zmienionych pol.</summary>
    public static int WczytajProgi(Thresholds p, JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) return 0;
        int n = 0;
        double D(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : double.NaN;
        foreach (var w in a.EnumerateObject())
        {
            var v = D(w.Value); if (double.IsNaN(v)) continue;
            switch (w.Name)
            {
                case "geometryIdentical": p.GeometryIdentical = v; break;
                case "geometrySimilar": p.GeometrySimilar = v; break;
                case "geometryTriangleTolerance": p.GeometryTriangleTolerance = v; break;
                case "geometryBoundsTolerance": p.GeometryBoundsTolerance = v; break;
                case "textureHashDistance": p.TextureHashDistance = (int)Math.Round(v); break;
                case "textureColorDistance": p.TextureColorDistance = v; break;
                case "flatTextureVariance": p.FlatTextureVariance = (float)v; break;
                case "flatTextureColorDistance": p.FlatTextureColorDistance = v; break;
                case "fullCoverage": p.FullCoverage = v; break;
                case "partialCoverage": p.PartialCoverage = v; break;
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
            var progi = pr.Ustawienia?.Thresholds;
            var cache = s.RozmiarCache();
            return new
            {
                kosz = pr.Ustawienia?.Kosz,
                progi = ProgiJson(progi ?? Thresholds.Default), progiDomyslne = ProgiJson(Thresholds.Default),
                progiZmienione = progi != null && !progi.SameAs(Thresholds.Default),
                cache = cache.ToDictionary(k => k.Key, k => new { pliki = k.Value.pliki, bajty = k.Value.bajty }),
                folderCache = pr.FolderCache,
                zrodla = pr.Zrodla.Count, pozycje = s.Catalog.Garments.Count,
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
            pr.Ustawienia ??= new Duble.Core.Projects.UstawieniaProjektu();
            bool zmianaProgow = false;
            if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("kosz", out var k))
                pr.Ustawienia.Kosz = k.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(k.GetString()) ? k.GetString() : null;
            if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("progi", out var pj) && pj.ValueKind == JsonValueKind.Object)
            {
                var nowe = (pr.Ustawienia.Thresholds ?? Thresholds.Default).Clone();
                WczytajProgi(nowe, pj);
                var bledy = nowe.Validate();
                if (bledy.Count > 0) throw new BladMostka("bad_args", string.Join(",", bledy));
                var stare = pr.Ustawienia.Thresholds ?? Thresholds.Default;
                zmianaProgow = !nowe.SameAs(stare);
                pr.Ustawienia.Thresholds = nowe.SameAs(Thresholds.Default) ? null : nowe;
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
            bool bylo = pr.Ustawienia?.Thresholds != null && !pr.Ustawienia.Thresholds.SameAs(Thresholds.Default);
            if (pr.Ustawienia != null) pr.Ustawienia.Thresholds = null;
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
            if (katalog.Garments.Count(p => p.Geometry?.ShapeHistogram != null && p.Geometry.Vertices > 0) < 2) throw new BladMostka("not_found", "za malo pozycji");
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
