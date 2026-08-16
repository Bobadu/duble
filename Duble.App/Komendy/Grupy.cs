// Komendy/Grupy.cs — compare.run, groups.list/get/decide/reset, apply.preview.
//
// Grupy pochodza z Sesja.Wynik (WynikPorownania z Duble.Core); "kto zostaje" liczy Rozstrzygniecie.Policz(grupa, decyzja z projektu).
// Powody werdyktow ida do UI jako kody {kod, p} — UI formatuje je z i18n (slownik Core jest zlaczony ze slownikiem UI).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble;

namespace Duble.App.Komendy;

public static class Grupy
{
    static readonly Dictionary<string, int> KolejnoscWerdyktow = new()
    {
        [Porownanie.Duplikat] = 0, [Porownanie.Nadzbior] = 1, [Porownanie.DoWgladu] = 2, [Porownanie.Przemalowanie] = 3,
    };

    public static void Zarejestruj(Mostek m, Sesja s, JobRunner jr)
    {
        Sesja Wymag() => s.Otwarty ? s : throw new BladMostka("no_project", "brak otwartego projektu");

        Rozstrzygniecie Rozstrzygnij(Grupa g)
            => Rozstrzygniecie.Policz(g, s.Projekt.Decyzje.TryGetValue(g.Id ?? "", out var d) ? d : null);

        // grupy z wyniku, ktorych wszyscy czlonkowie nadal sa w katalogu (usuniete zrodlo = grupa znika)
        List<(Grupa g, List<Pozycja> czl, Rozstrzygniecie r)> Zywe()
        {
            var wynik = s.Wynik; if (wynik == null) return new();
            var wg = s.Katalog.Pozycje.ToDictionary(p => p.Id);
            var wy = new List<(Grupa g, List<Pozycja> czl, Rozstrzygniecie r)>();
            foreach (var g in wynik.Grupy)
            {
                if (g.Pozycje == null || g.Pozycje.Count == 0 || !g.Pozycje.All(wg.ContainsKey)) continue;
                if (string.IsNullOrEmpty(g.Id)) g.Id = Grupa.PoliczId(g.Pozycje);
                wy.Add((g, g.Pozycje.Select(id => wg[id]).ToList(), Rozstrzygnij(g)));
            }
            return wy.OrderBy(x => KolejnoscWerdyktow.TryGetValue(x.g.Werdykt, out var k) ? k : 9).ThenByDescending(x => x.g.Pozycje.Count).ThenBy(x => x.g.Pozycje[0], StringComparer.Ordinal).ToList();
        }

        object Powod(Powod p) => p == null ? null : new { kod = p.Kod, p = p.P };
        object Rozstrz(Rozstrzygniecie r) => new { zwyciezca = r.Zwyciezca, odrzucone = r.Odrzucone, ignoruj = r.Ignoruj, domyslna = r.Domyslna, notatka = r.Notatka };
        object Punkt(Punktacja p) => p == null ? null : new { razem = p.Razem, rozdz = p.Rozdz, mipy = p.Mipy, warianty = p.Warianty, format = p.Format, lod = p.Lod, rozdzPx = p.RozdzPx, udzialMipow = p.UdzialMipow, liczbaWariantow = p.LiczbaWariantow, zlyFormat = p.ZlyFormat, lody = p.Lody, brakTekstur = p.BrakTekstur };
        string Zrodlo(Pozycja p) => s.Projekt.Zrodla.Find(z => z.Id == p.ZrodloId)?.Nazwa ?? p.Paczka;

        object Czlonek(Pozycja p, Grupa g, bool szczegoly)
        {
            var thumb = p.Tekstury.FirstOrDefault(t => t.Zdekodowana && t.Sha != null)?.Sha;
            var podst = new Dictionary<string, object>
            {
                ["id"] = p.Id, ["zrodloId"] = p.ZrodloId, ["zrodlo"] = Zrodlo(p), ["kontener"] = p.Kontener, ["typ"] = p.Typ, ["numer"] = p.Numer, ["sufiks"] = p.Sufiks,
                ["gen9"] = p.Gen9, ["props"] = p.Props, ["punkty"] = g.Punkty.TryGetValue(p.Id, out var pkt) ? pkt : 0.0, ["thumb"] = thumb,
                ["tekstur"] = p.Tekstury.Count, ["wierzcholki"] = p.Geo?.Wierzcholki ?? 0, ["trojkaty"] = p.Geo?.Trojkaty ?? 0, ["lody"] = p.Geo?.Lody ?? 0,
                ["bajty"] = p.BajtyYdd + p.Tekstury.Sum(t => t.Bajty), ["wArchiwum"] = p.SciezkaYdd != null && p.SciezkaYdd.Contains('|'),
            };
            if (szczegoly)
            {
                podst["rozpiska"] = g.Rozpiska.TryGetValue(p.Id, out var r) ? Punkt(r) : null;
                podst["sciezkaYdd"] = p.SciezkaYdd;
                podst["bajtyYdd"] = p.BajtyYdd;
                podst["tekstury"] = p.Tekstury.Select(t => new { sha = t.Sha, plik = t.Plik, nazwa = t.Nazwa, w = t.W, h = t.H, format = t.Format, mipy = t.Mipy, alfa = t.Alfa, zdekodowana = t.Zdekodowana, bajty = t.Bajty }).ToList();
            }
            return podst;
        }

        object Grupa1(Grupa g, List<Pozycja> czl, Rozstrzygniecie r, bool szczegoly)
        {
            var o = new Dictionary<string, object>
            {
                ["id"] = g.Id, ["werdykt"] = g.Werdykt, ["powod"] = Powod(g.Pary.FirstOrDefault()?.Powod ?? g.Powod), ["zwyciezca"] = g.Zwyciezca,
                ["rozstrzygniecie"] = Rozstrz(r), ["czlonkowie"] = czl.Select(p => Czlonek(p, g, szczegoly)).ToList(),
            };
            if (szczegoly)
            {
                o["pary"] = g.Pary.Select(p => new { a = p.A, b = p.B, werdykt = p.Werdykt, powod = Powod(p.Powod), distGeo = p.DistGeo, pokrycieA = p.PokrycieA, pokrycieB = p.PokrycieB, wspolnychTekstur = p.WspolnychTekstur }).ToList();
                var progi = s.Projekt.Ustawienia?.Progi ?? Progi.Domyslne;
                var dop = new List<object>();
                for (int i = 0; i < czl.Count; i++)
                    for (int j = i + 1; j < czl.Count; j++)
                    {
                        var pary = new List<string[]>();
                        var uzyte = new HashSet<int>();
                        foreach (var ta in czl[i].Tekstury)
                            for (int k = 0; k < czl[j].Tekstury.Count; k++)
                            {
                                if (uzyte.Contains(k)) continue;
                                if (Porownanie.TaSamaGrafika(ta, czl[j].Tekstury[k], progi)) { uzyte.Add(k); pary.Add(new[] { ta.Sha, czl[j].Tekstury[k].Sha }); break; }
                            }
                        dop.Add(new { a = czl[i].Id, b = czl[j].Id, pary });
                    }
                o["dopasowania"] = dop;
            }
            return o;
        }

        // odrzucone pozycje ze wszystkich grup (bez zignorowanych) -> pliki do przeniesienia (jak Zastosowanie: bez wspoldzielonych z tymi, ktore zostaja)
        object PodgladZastosowania(List<(Grupa g, List<Pozycja> czl, Rozstrzygniecie r)> zywe)
        {
            var odrzucone = new HashSet<string>(zywe.Where(x => !x.r.Ignoruj).SelectMany(x => x.r.Odrzucone));
            var wg = s.Katalog.Pozycje.ToDictionary(p => p.Id);
            var chronione = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in s.Katalog.Pozycje.Where(p => !odrzucone.Contains(p.Id)))
            {
                if (p.SciezkaYdd != null) chronione.Add(p.SciezkaYdd);
                foreach (var t in p.Tekstury) if (t.Sciezka != null) chronione.Add(t.Sciezka);
            }
            int pliki = 0, wArchiwum = 0, wspoldzielone = 0; long bajty = 0;
            var widziane = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in odrzucone)
            {
                if (!wg.TryGetValue(id, out var p)) continue;
                var lista = new List<(string sciezka, long bajty)>();
                if (p.SciezkaYdd != null) lista.Add((p.SciezkaYdd, p.BajtyYdd));
                lista.AddRange(p.Tekstury.Where(t => t.Sciezka != null).Select(t => (t.Sciezka, t.Bajty)));
                foreach (var (sc, b) in lista)
                {
                    if (!widziane.Add(sc)) continue;
                    if (sc.Contains('|')) { wArchiwum++; continue; }
                    if (chronione.Contains(sc)) { wspoldzielone++; continue; }
                    pliki++; bajty += b;
                }
            }
            return new { pozycje = odrzucone.Count, pliki, bajty, wArchiwum, wspoldzielone };
        }

        m.Rejestruj("compare.run", _ =>
        {
            Wymag();
            bool ok = jr.SprobujUruchom("porownaj", s.Projekt.Nazwa, async (ct, postep) =>
            {
                await Task.Yield();
                postep(new Postep("porownaj", 0, 0, null));
                s.Porownaj(ct, postep);
                s.Zapisz();
                m.Zdarzenie("compare.done", new { podsumowanie = s.Podsumowanie() });
                m.Zdarzenie("project.changed", new { projekt = s.Podsumowanie() });
            });
            if (!ok) throw new BladMostka("busy", "trwa inne zadanie");
            return new { uruchomiono = true };
        });

        m.Rejestruj("groups.list", a =>
        {
            Wymag();
            var werdykty = Mostek.Lista(a, "werdykty"); var sloty = Mostek.Lista(a, "sloty"); var zrodla = Mostek.Lista(a, "zrodla");
            var szukaj = (Mostek.Tekst(a, "szukaj") ?? "").Trim().ToLowerInvariant();
            bool zignorowane = Mostek.Flaga(a, "zignorowane");
            var zywe = Zywe();
            var wynik = s.Wynik;
            var podsumowanie = new
            {
                grup = wynik == null ? (int?)null : zywe.Count,
                duplikat = zywe.Count(x => x.g.Werdykt == Porownanie.Duplikat), nadzbior = zywe.Count(x => x.g.Werdykt == Porownanie.Nadzbior),
                wglad = zywe.Count(x => x.g.Werdykt == Porownanie.DoWgladu), przemalowanie = zywe.Count(x => x.g.Werdykt == Porownanie.Przemalowanie),
                zignorowane = zywe.Count(x => x.r.Ignoruj), porownano = wynik?.Zbudowany,
                doOdrzucenia = PodgladZastosowania(zywe),
            };
            var filtrySloty = zywe.SelectMany(x => x.czl.Select(p => p.Typ)).GroupBy(t => t).Select(g => new { typ = g.Key, n = g.Count() }).OrderBy(x => x.typ).ToList();
            var filtryZrodla = zywe.SelectMany(x => x.czl.Select(p => p.ZrodloId ?? "")).GroupBy(t => t).Select(g => new { id = g.Key, nazwa = s.Projekt.Zrodla.Find(z => z.Id == g.Key)?.Nazwa ?? g.Key, n = g.Count() }).OrderBy(x => x.nazwa).ToList();
            var grupy = zywe.Where(x =>
                (zignorowane || !x.r.Ignoruj)
                && (werdykty.Count == 0 || werdykty.Contains(x.g.Werdykt))
                && (sloty.Count == 0 || x.czl.Any(p => sloty.Contains(p.Typ)))
                && (zrodla.Count == 0 || x.czl.Any(p => zrodla.Contains(p.ZrodloId ?? "")))
                && (szukaj.Length == 0 || x.czl.Any(p => ($"{p.Typ}_{p.Numer:d3} {p.Paczka} {p.Kontener} {Zrodlo(p)} {p.Id}").ToLowerInvariant().Contains(szukaj)))
            ).Select(x => Grupa1(x.g, x.czl, x.r, false)).ToList();
            return new { podsumowanie, filtry = new { sloty = filtrySloty, zrodla = filtryZrodla }, grupy };
        });

        m.Rejestruj("groups.get", a =>
        {
            Wymag();
            var id = Mostek.Tekst(a, "id", true);
            var x = Zywe().FirstOrDefault(y => y.g.Id == id);
            if (x.g == null) throw new BladMostka("not_found", id);
            return new { grupa = Grupa1(x.g, x.czl, x.r, true) };
        });

        m.Rejestruj("groups.decide", a =>
        {
            Wymag();
            var id = Mostek.Tekst(a, "id", true);
            var x = Zywe().FirstOrDefault(y => y.g.Id == id);
            if (x.g == null) throw new BladMostka("not_found", id);
            var czlonkowie = x.g.Pozycje;
            if (!s.Projekt.Decyzje.TryGetValue(id, out var d))
            {
                var dom = Rozstrzygniecie.Policz(x.g, null);
                d = new Decyzja { Zwyciezca = dom.Zwyciezca, Odrzucone = dom.Odrzucone.ToList() };
                s.Projekt.Decyzje[id] = d;
            }
            var zw = Mostek.Tekst(a, "zwyciezca");
            bool podanoOdrzucone = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("odrzucone", out var od) && od.ValueKind == JsonValueKind.Array;
            if (zw != null && czlonkowie.Contains(zw))
            {
                d.Zwyciezca = zw;
                if (!podanoOdrzucone) d.Odrzucone = czlonkowie.Where(c => c != zw).ToList();   // "zostaw te" = reszta odpada
            }
            if (podanoOdrzucone) d.Odrzucone = Mostek.Lista(a, "odrzucone").Where(c => czlonkowie.Contains(c) && c != d.Zwyciezca).Distinct().ToList();
            if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("ignoruj", out var ig) && (ig.ValueKind == JsonValueKind.True || ig.ValueKind == JsonValueKind.False)) d.Ignoruj = ig.GetBoolean();
            var notatka = Mostek.Tekst(a, "notatka");
            if (notatka != null) d.Notatka = notatka.Length == 0 ? null : notatka;
            s.Projekt.Zapisz();
            var r = Rozstrzygnij(x.g);
            m.Zdarzenie("groups.changed", new { id });
            m.Zdarzenie("project.changed", new { projekt = s.Podsumowanie() });
            return new { rozstrzygniecie = Rozstrz(r) };
        });

        m.Rejestruj("groups.reset", a =>
        {
            Wymag();
            var id = Mostek.Tekst(a, "id", true);
            var x = Zywe().FirstOrDefault(y => y.g.Id == id);
            if (x.g == null) throw new BladMostka("not_found", id);
            s.Projekt.Decyzje.Remove(id);
            s.Projekt.Zapisz();
            m.Zdarzenie("groups.changed", new { id });
            m.Zdarzenie("project.changed", new { projekt = s.Podsumowanie() });
            return new { rozstrzygniecie = Rozstrz(Rozstrzygnij(x.g)) };
        });

        m.Rejestruj("apply.preview", _ => { Wymag(); return PodgladZastosowania(Zywe()); });
    }
}
