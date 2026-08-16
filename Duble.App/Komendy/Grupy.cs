// Komendy/Grupy.cs — compare.run, groups.list/get/decide/reset, apply.preview/run.
//
// Grupy pochodza z Sesja.Wynik (WynikPorownania z Duble.Core); "kto zostaje" liczy Rozstrzygniecie.Policz(grupa, decyzja z projektu).
// Powody werdyktow ida do UI jako kody {kod, p} — UI formatuje je z i18n (slownik Core jest zlaczony ze slownikiem UI).
// Zastosuj: plan z Sesja.Zaplanuj (Zastosowanie w Core), wykonanie w JobRunner "zastosuj", cofka do historia\<czas>.json,
// potem ponowne indeksowanie dotknietych zrodel + porownanie (Zrodla.Indeksuj/PorownajIZapisz).
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

    /// <summary>Grupy z wyniku, ktorych wszyscy czlonkowie nadal sa w katalogu (usuniete zrodlo = grupa znika), z rozstrzygnieciami; posortowane.</summary>
    public static List<(Grupa g, List<Pozycja> czl, Rozstrzygniecie r)> Zywe(Sesja s)
    {
        var wynik = s.Wynik; if (wynik == null || s.Projekt == null) return new();
        var wg = s.Katalog.Pozycje.ToDictionary(p => p.Id);
        var wy = new List<(Grupa g, List<Pozycja> czl, Rozstrzygniecie r)>();
        foreach (var g in wynik.Grupy)
        {
            if (g.Pozycje == null || g.Pozycje.Count == 0 || !g.Pozycje.All(wg.ContainsKey)) continue;
            if (string.IsNullOrEmpty(g.Id)) g.Id = Grupa.PoliczId(g.Pozycje);
            wy.Add((g, g.Pozycje.Select(id => wg[id]).ToList(), Rozstrzygnij(s, g)));
        }
        return wy.OrderBy(x => KolejnoscWerdyktow.TryGetValue(x.g.Werdykt, out var k) ? k : 9).ThenByDescending(x => x.g.Pozycje.Count).ThenBy(x => x.g.Pozycje[0], StringComparer.Ordinal).ToList();
    }

    public static Rozstrzygniecie Rozstrzygnij(Sesja s, Grupa g)
        => Rozstrzygniecie.Policz(g, s.Projekt.Decyzje.TryGetValue(g.Id ?? "", out var d) ? d : null);

    /// <summary>Id pozycji odrzuconych we wszystkich zywych grupach (bez zignorowanych).</summary>
    public static HashSet<string> Odrzucone(List<(Grupa g, List<Pozycja> czl, Rozstrzygniecie r)> zywe)
        => new(zywe.Where(x => !x.r.Ignoruj).SelectMany(x => x.r.Odrzucone));

    public static object PlanJson(Sesja s, PlanZastosowania plan, bool lista)
    {
        var wg = s.Katalog.Pozycje.ToDictionary(p => p.Id);
        var o = new Dictionary<string, object>
        {
            ["pozycje"] = plan.Pozycje.Count, ["pliki"] = plan.Pliki, ["bajty"] = plan.Bajty,
            ["wArchiwum"] = plan.WArchiwum, ["wspoldzielone"] = plan.Wspoldzielone, ["brakujace"] = plan.Brakujace,
            ["brakujaceZrodla"] = plan.BrakujaceZrodla,
            ["kosz"] = s.Projekt.Ustawienia?.Kosz,
            ["kosze"] = plan.Kosze().Select(k => new { kosz = k.kosz, pliki = k.pliki, bajty = k.bajty }).ToList(),
        };
        if (lista)
            o["lista"] = plan.Pozycje.Select(p => new
            {
                id = p.Id, nazwa = p.Nazwa, sufiks = p.Sufiks, zrodlo = p.Zrodlo, zrodloId = p.ZrodloId, kontener = p.Kontener, kosz = p.Kosz,
                thumb = wg.TryGetValue(p.Id, out var poz) ? Widoki.Miniatura(poz) : null,
                pliki = p.DoPrzeniesienia, bajty = p.Bajty, wspoldzielone = p.Wspoldzielone, wArchiwum = p.WArchiwum, brakujace = p.Brakujace,
            }).ToList();
        return o;
    }

    public static void Zarejestruj(Mostek m, Sesja s, JobRunner jr)
    {
        Sesja Wymag() => s.Otwarty ? s : throw new BladMostka("no_project", "brak otwartego projektu");
        string Zrodlo(Pozycja p) => s.Projekt.Zrodla.Find(z => z.Id == p.ZrodloId)?.Nazwa ?? p.Paczka;

        object Grupa1(Grupa g, List<Pozycja> czl, Rozstrzygniecie r, bool szczegoly)
        {
            var o = new Dictionary<string, object>
            {
                ["id"] = g.Id, ["werdykt"] = g.Werdykt, ["powod"] = Widoki.Powod(g.Pary.FirstOrDefault()?.Powod ?? g.Powod), ["zwyciezca"] = g.Zwyciezca,
                ["rozstrzygniecie"] = Widoki.Rozstrz(r), ["czlonkowie"] = czl.Select(p => Widoki.Czlonek(p, g, szczegoly, Zrodlo)).ToList(),
            };
            if (szczegoly)
            {
                o["pary"] = g.Pary.Select(p => new { a = p.A, b = p.B, werdykt = p.Werdykt, powod = Widoki.Powod(p.Powod), distGeo = p.DistGeo, pokrycieA = p.PokrycieA, pokrycieB = p.PokrycieB, wspolnychTekstur = p.WspolnychTekstur }).ToList();
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

        m.Rejestruj("compare.run", _ =>
        {
            Wymag();
            bool ok = jr.SprobujUruchom("porownaj", s.Projekt.Nazwa, async (ct, postep) =>
            {
                await Task.Yield();
                Zrodla.PorownajIZapisz(s, m, ct, postep);
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
            var zywe = Zywe(s);
            var wynik = s.Wynik;
            var podsumowanie = new
            {
                grup = wynik == null ? (int?)null : zywe.Count,
                duplikat = zywe.Count(x => x.g.Werdykt == Porownanie.Duplikat), nadzbior = zywe.Count(x => x.g.Werdykt == Porownanie.Nadzbior),
                wglad = zywe.Count(x => x.g.Werdykt == Porownanie.DoWgladu), przemalowanie = zywe.Count(x => x.g.Werdykt == Porownanie.Przemalowanie),
                zignorowane = zywe.Count(x => x.r.Ignoruj), porownano = wynik?.Zbudowany,
                doOdrzucenia = PlanJson(s, s.Zaplanuj(Odrzucone(zywe)), false),
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
            var x = Zywe(s).FirstOrDefault(y => y.g.Id == id);
            if (x.g == null) throw new BladMostka("not_found", id);
            return new { grupa = Grupa1(x.g, x.czl, x.r, true) };
        });

        m.Rejestruj("groups.decide", a =>
        {
            Wymag();
            var id = Mostek.Tekst(a, "id", true);
            var x = Zywe(s).FirstOrDefault(y => y.g.Id == id);
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
            var r = Rozstrzygnij(s, x.g);
            m.Zdarzenie("groups.changed", new { id });
            m.Zdarzenie("project.changed", new { projekt = s.Podsumowanie() });
            return new { rozstrzygniecie = Widoki.Rozstrz(r) };
        });

        m.Rejestruj("groups.reset", a =>
        {
            Wymag();
            var id = Mostek.Tekst(a, "id", true);
            var x = Zywe(s).FirstOrDefault(y => y.g.Id == id);
            if (x.g == null) throw new BladMostka("not_found", id);
            s.Projekt.Decyzje.Remove(id);
            s.Projekt.Zapisz();
            m.Zdarzenie("groups.changed", new { id });
            m.Zdarzenie("project.changed", new { projekt = s.Podsumowanie() });
            return new { rozstrzygniecie = Widoki.Rozstrz(Rozstrzygnij(s, x.g)) };
        });

        // {kosz?: string|null, ustawKosz?: bool} — dialog Zastosuj zmienia kosz projektu (null = obok zrodla) i od razu widzi nowy plan
        void UstawKosz(JsonElement a)
        {
            if (!Mostek.Flaga(a, "ustawKosz")) return;
            var kosz = Mostek.Tekst(a, "kosz");
            s.Projekt.Ustawienia ??= new UstawieniaProjektu();
            s.Projekt.Ustawienia.Kosz = string.IsNullOrWhiteSpace(kosz) ? null : kosz;
            s.Projekt.Zapisz();
        }

        m.Rejestruj("apply.preview", a => { Wymag(); UstawKosz(a); return PlanJson(s, s.Zaplanuj(Odrzucone(Zywe(s))), true); });

        // apply.run {kosz?: string|null, ustawKosz?: bool} — przenosi wszystko, co odrzucone (plan liczony na swiezo), zapisuje cofke,
        // ponownie indeksuje dotkniete zrodla, porownuje; zdarzenia: job (typ "zastosuj"), apply.done, history.changed
        m.Rejestruj("apply.run", a =>
        {
            Wymag();
            UstawKosz(a);
            var plan = s.Zaplanuj(Odrzucone(Zywe(s)));
            if (plan.Pliki == 0) return new { uruchomiono = false, plan = PlanJson(s, plan, false) };
            bool ok = jr.SprobujUruchom("zastosuj", s.Projekt.Nazwa, async (ct, postep) =>
            {
                await Task.Yield();
                var cofka = Zastosowanie.Wykonaj(plan, s.Projekt.Nazwa, postep, ct);
                var plik = s.NowyPlikHistorii();
                cofka.Zapisz(plik);   // ZAWSZE — takze po przerwaniu (to, co juz sie przenioslo, musi dac sie cofnac)
                m.Zdarzenie("history.changed", new { plik });
                // po przerwaniu (anulowanie) i tak porzadkujemy katalog — inaczej zostalby nieaktualny (przeniesione pliki nadal w katalogu)
                var ct2 = cofka.Przerwano ? System.Threading.CancellationToken.None : ct;
                var dotkniete = s.Projekt.Zrodla.Where(z => cofka.Pozycje.Any(p => p.ZrodloId == z.Id)).ToList();
                if (dotkniete.Count > 0) Zrodla.Indeksuj(s, m, dotkniete, false, ct2, postep);
                Zrodla.PorownajIZapisz(s, m, ct2, postep);
                m.Zdarzenie("apply.done", new
                {
                    plik, przeniesione = cofka.Ruchy.Count, pozycje = cofka.Pozycje.Count, bajty = cofka.Bajty,
                    wspoldzielone = cofka.Wspoldzielone, wArchiwum = cofka.WArchiwum, brakujace = cofka.Brakujace,
                    kosze = cofka.Pozycje.Select(p => p.Kosz).Where(k => k != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    przerwano = cofka.Przerwano, blad = cofka.Blad,
                });
            });
            if (!ok) throw new BladMostka("busy", "trwa inne zadanie");
            return new { uruchomiono = true, plan = PlanJson(s, plan, false) };
        });
    }
}
