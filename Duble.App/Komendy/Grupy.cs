// Komendy/Grupy.cs — compare.run, groups.list/get/decide/reset, apply.preview/run.
//
// Grupy pochodza z Sesja.Wynik (ComparisonResult z Duble.Core); "kto zostaje" liczy s.Rozstrzygniecia.Resolve(grupa, decyzja z projektu).
// Powody werdyktow ida do UI jako kody {kod, p} — UI formatuje je z i18n (slownik Core jest zlaczony ze slownikiem UI).
// Zastosuj: plan z Sesja.Plan (ApplyPlanner w Core), wykonanie w JobRunner "zastosuj", cofka do historia\<czas>.json,
// potem ponowne indeksowanie dotknietych zrodel + porownanie (Zrodla.Indeksuj/PorownajIZapisz).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Duble.App.Komendy;

public static class Grupy
{
    static readonly Dictionary<Verdict, int> KolejnoscWerdyktow = new()
    {
        [Verdict.Duplicate] = 0, [Verdict.Superset] = 1, [Verdict.NeedsReview] = 2, [Verdict.Retexture] = 3,
    };

    /// <summary>Grupy z wyniku, ktorych wszyscy czlonkowie nadal sa w katalogu (usuniete zrodlo = grupa znika), z rozstrzygnieciami; posortowane.</summary>
    public static List<(DuplicateGroup g, List<Garment> czl, Resolution r)> Zywe(Sesja s)
    {
        var wynik = s.Wynik; if (wynik == null || s.Project == null) return new();
        var wg = s.Catalog.Garments.ToDictionary(p => p.Id);
        var wy = new List<(DuplicateGroup g, List<Garment> czl, Resolution r)>();
        foreach (var g in wynik.Groups)
        {
            if (g.Members == null || g.Members.Count == 0 || !g.Members.All(wg.ContainsKey)) continue;
            if (string.IsNullOrEmpty(g.Id)) g.Id = DuplicateGroup.ComputeId(g.Members);
            wy.Add((g, g.Members.Select(id => wg[id]).ToList(), Rozstrzygnij(s, g)));
        }
        return wy.OrderBy(x => KolejnoscWerdyktow.TryGetValue(x.g.Verdict, out var k) ? k : 9).ThenByDescending(x => x.g.Members.Count).ThenBy(x => x.g.Members[0], StringComparer.Ordinal).ToList();
    }

    public static Resolution Rozstrzygnij(Sesja s, DuplicateGroup g)
        => s.Rozstrzygniecia.Resolve(g, s.Project.Decisions.TryGetValue(g.Id ?? "", out var d) ? d : null);

    /// <summary>Id pozycji odrzuconych we wszystkich zywych grupach (bez zignorowanych).</summary>
    public static HashSet<string> Odrzucone(List<(DuplicateGroup g, List<Garment> czl, Resolution r)> zywe)
        => new(zywe.Where(x => !x.r.Ignored).SelectMany(x => x.r.Rejected));

    public static object PlanJson(Sesja s, ApplyPlan plan, bool lista)
    {
        var wg = s.Catalog.Garments.ToDictionary(p => p.Id);
        var o = new Dictionary<string, object>
        {
            ["pozycje"] = plan.Garments.Count, ["pliki"] = plan.Files, ["bajty"] = plan.Bytes,
            ["wArchiwum"] = plan.InArchiveCount, ["wspoldzielone"] = plan.SharedCount, ["brakujace"] = plan.MissingCount,
            ["brakujaceZrodla"] = plan.MissingSources,
            ["kosz"] = s.Project.Settings?.BinFolder,
            ["kosze"] = plan.BinTotals().Select(k => new { kosz = k.kosz, pliki = k.pliki, bajty = k.bajty }).ToList(),
        };
        if (lista)
            o["lista"] = plan.Garments.Select(p => new
            {
                id = p.Id, nazwa = p.Name, sufiks = p.Suffix, zrodlo = p.SourceName, zrodloId = p.SourceId, kontener = p.Container, kosz = p.BinFolder,
                thumb = wg.TryGetValue(p.Id, out var poz) ? Widoki.Miniatura(poz) : null,
                pliki = p.MoveCount, bajty = p.Bytes, wspoldzielone = p.SharedCount, wArchiwum = p.InArchiveCount, brakujace = p.MissingCount,
            }).ToList();
        return o;
    }

    public static void Zarejestruj(Mostek m, Sesja s, JobRunner jr)
    {
        Sesja Wymag() => s.Otwarty ? s : throw new BladMostka("no_project", "brak otwartego projektu");
        string Zrodlo(Garment p) => s.Project.Sources.Find(z => z.Id == p.SourceId)?.Name ?? p.PackName;

        object Grupa1(DuplicateGroup g, List<Garment> czl, Resolution r, bool szczegoly)
        {
            var o = new Dictionary<string, object>
            {
                ["id"] = g.Id, ["werdykt"] = g.Verdict.ToKey(), ["powod"] = Widoki.Reason(g.Pairs.FirstOrDefault()?.Reason ?? g.Reason), ["zwyciezca"] = g.Winner,
                ["rozstrzygniecie"] = Widoki.Rozstrz(r), ["czlonkowie"] = czl.Select(p => Widoki.Czlonek(p, g, szczegoly, Zrodlo)).ToList(),
            };
            if (szczegoly)
            {
                o["pary"] = g.Pairs.Select(p => new { a = p.A, b = p.B, werdykt = p.Verdict.ToKey(), powod = Widoki.Reason(p.Reason), distGeo = p.GeometryDistance, pokrycieA = p.CoverageA, pokrycieB = p.CoverageB, wspolnychTekstur = p.SharedTextures }).ToList();
                var progi = s.Project.Settings?.Thresholds ?? Thresholds.Default;
                var dop = new List<object>();
                for (int i = 0; i < czl.Count; i++)
                    for (int j = i + 1; j < czl.Count; j++)
                    {
                        var pary = new List<string[]>();
                        var uzyte = new HashSet<int>();
                        foreach (var ta in czl[i].Textures)
                            for (int k = 0; k < czl[j].Textures.Count; k++)
                            {
                                if (uzyte.Contains(k)) continue;
                                if (DuplicateFinder.SameGraphic(ta, czl[j].Textures[k], progi)) { uzyte.Add(k); pary.Add(new[] { ta.Sha256, czl[j].Textures[k].Sha256 }); break; }
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
            bool ok = jr.SprobujUruchom("porownaj", s.Project.Name, async (ct, postep) =>
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
            var szukaj = (Mostek.Text(a, "szukaj") ?? "").Trim().ToLowerInvariant();
            bool zignorowane = Mostek.Flaga(a, "zignorowane");
            var zywe = Zywe(s);
            var wynik = s.Wynik;
            var podsumowanie = new
            {
                grup = wynik == null ? (int?)null : zywe.Count,
                duplikat = zywe.Count(x => x.g.Verdict == Verdict.Duplicate), nadzbior = zywe.Count(x => x.g.Verdict == Verdict.Superset),
                wglad = zywe.Count(x => x.g.Verdict == Verdict.NeedsReview), przemalowanie = zywe.Count(x => x.g.Verdict == Verdict.Retexture),
                zignorowane = zywe.Count(x => x.r.Ignored), porownano = wynik?.Built,
                doOdrzucenia = PlanJson(s, s.Plan(Odrzucone(zywe)), false),
            };
            var filtrySloty = zywe.SelectMany(x => x.czl.Select(p => p.Slot)).GroupBy(t => t).Select(g => new { typ = g.Key, n = g.Count() }).OrderBy(x => x.typ).ToList();
            var filtryZrodla = zywe.SelectMany(x => x.czl.Select(p => p.SourceId ?? "")).GroupBy(t => t).Select(g => new { id = g.Key, nazwa = s.Project.Sources.Find(z => z.Id == g.Key)?.Name ?? g.Key, n = g.Count() }).OrderBy(x => x.nazwa).ToList();
            var grupy = zywe.Where(x =>
                (zignorowane || !x.r.Ignored)
                && (werdykty.Count == 0 || werdykty.Contains(x.g.Verdict.ToKey()))
                && (sloty.Count == 0 || x.czl.Any(p => sloty.Contains(p.Slot)))
                && (zrodla.Count == 0 || x.czl.Any(p => zrodla.Contains(p.SourceId ?? "")))
                && (szukaj.Length == 0 || x.czl.Any(p => ($"{p.Slot}_{p.Number:d3} {p.PackName} {p.Container} {Zrodlo(p)} {p.Id}").ToLowerInvariant().Contains(szukaj)))
            ).Select(x => Grupa1(x.g, x.czl, x.r, false)).ToList();
            return new { podsumowanie, filtry = new { sloty = filtrySloty, zrodla = filtryZrodla }, grupy };
        });

        m.Rejestruj("groups.get", a =>
        {
            Wymag();
            var id = Mostek.Text(a, "id", true);
            var x = Zywe(s).FirstOrDefault(y => y.g.Id == id);
            if (x.g == null) throw new BladMostka("not_found", id);
            return new { grupa = Grupa1(x.g, x.czl, x.r, true) };
        });

        m.Rejestruj("groups.decide", a =>
        {
            Wymag();
            var id = Mostek.Text(a, "id", true);
            var x = Zywe(s).FirstOrDefault(y => y.g.Id == id);
            if (x.g == null) throw new BladMostka("not_found", id);
            var czlonkowie = x.g.Members;
            if (!s.Project.Decisions.TryGetValue(id, out var d))
            {
                var dom = s.Rozstrzygniecia.Resolve(x.g, null);
                d = new Decision { Winner = dom.Winner, Rejected = dom.Rejected.ToList() };
                s.Project.Decisions[id] = d;
            }
            var zw = Mostek.Text(a, "zwyciezca");
            bool podanoOdrzucone = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("odrzucone", out var od) && od.ValueKind == JsonValueKind.Array;
            if (zw != null && czlonkowie.Contains(zw))
            {
                d.Winner = zw;
                if (!podanoOdrzucone) d.Rejected = czlonkowie.Where(c => c != zw).ToList();   // "zostaw te" = reszta odpada
            }
            if (podanoOdrzucone) d.Rejected = Mostek.Lista(a, "odrzucone").Where(c => czlonkowie.Contains(c) && c != d.Winner).Distinct().ToList();
            if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("ignoruj", out var ig) && (ig.ValueKind == JsonValueKind.True || ig.ValueKind == JsonValueKind.False)) d.Ignored = ig.GetBoolean();
            var notatka = Mostek.Text(a, "notatka");
            if (notatka != null) d.Note = notatka.Length == 0 ? null : notatka;
            s.ZapiszProjekt();
            var r = Rozstrzygnij(s, x.g);
            m.Zdarzenie("groups.changed", new { id });
            m.Zdarzenie("project.changed", new { projekt = s.Podsumowanie() });
            return new { rozstrzygniecie = Widoki.Rozstrz(r) };
        });

        m.Rejestruj("groups.reset", a =>
        {
            Wymag();
            var id = Mostek.Text(a, "id", true);
            var x = Zywe(s).FirstOrDefault(y => y.g.Id == id);
            if (x.g == null) throw new BladMostka("not_found", id);
            s.Project.Decisions.Remove(id);
            s.ZapiszProjekt();
            m.Zdarzenie("groups.changed", new { id });
            m.Zdarzenie("project.changed", new { projekt = s.Podsumowanie() });
            return new { rozstrzygniecie = Widoki.Rozstrz(Rozstrzygnij(s, x.g)) };
        });

        // {kosz?: string|null, ustawKosz?: bool} — dialog Zastosuj zmienia kosz projektu (null = obok zrodla) i od razu widzi nowy plan
        void UstawKosz(JsonElement a)
        {
            if (!Mostek.Flaga(a, "ustawKosz")) return;
            var kosz = Mostek.Text(a, "kosz");
            s.Project.Settings ??= new ProjectSettings();
            s.Project.Settings.BinFolder = string.IsNullOrWhiteSpace(kosz) ? null : kosz;
            s.ZapiszProjekt();
        }

        m.Rejestruj("apply.preview", a => { Wymag(); UstawKosz(a); return PlanJson(s, s.Plan(Odrzucone(Zywe(s))), true); });

        // apply.run {kosz?: string|null, ustawKosz?: bool} — przenosi wszystko, co odrzucone (plan liczony na swiezo), zapisuje cofke,
        // ponownie indeksuje dotkniete zrodla, porownuje; zdarzenia: job (typ "zastosuj"), apply.done, history.changed
        m.Rejestruj("apply.run", a =>
        {
            Wymag();
            UstawKosz(a);
            var plan = s.Plan(Odrzucone(Zywe(s)));
            if (plan.Files == 0) return new { uruchomiono = false, plan = PlanJson(s, plan, false) };
            bool ok = jr.SprobujUruchom("zastosuj", s.Project.Name, async (ct, postep) =>
            {
                await Task.Yield();
                var cofka = s.Wykonawca.Execute(plan, s.Project.Name, new Progress<ProgressReport>(postep), ct);
                var plik = s.NowyPlikHistorii();
                // ALWAYS, an aborted apply included: whatever did move has to remain undoable
                s.Cofki.Save(cofka, plik);
                m.Zdarzenie("history.changed", new { plik });
                // po przerwaniu (anulowanie) i tak porzadkujemy katalog — inaczej zostalby nieaktualny (przeniesione pliki nadal w katalogu)
                var ct2 = cofka.Aborted ? System.Threading.CancellationToken.None : ct;
                var dotkniete = s.Project.Sources.Where(z => cofka.Garments.Any(p => p.SourceId == z.Id)).ToList();
                if (dotkniete.Count > 0) Zrodla.Indeksuj(s, m, dotkniete, false, ct2, postep);
                Zrodla.PorownajIZapisz(s, m, ct2, postep);
                m.Zdarzenie("apply.done", new
                {
                    plik, przeniesione = cofka.Moves.Count, pozycje = cofka.Garments.Count, bajty = cofka.Bytes,
                    wspoldzielone = cofka.SharedCount, wArchiwum = cofka.InArchiveCount, brakujace = cofka.MissingCount,
                    kosze = cofka.Garments.Select(p => p.BinFolder).Where(k => k != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    przerwano = cofka.Aborted, blad = cofka.Error,
                });
            });
            if (!ok) throw new BladMostka("busy", "trwa inne zadanie");
            return new { uruchomiono = true, plan = PlanJson(s, plan, false) };
        });
    }
}
