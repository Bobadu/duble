// Komendy/KatalogPozycji.cs — catalog.list (wszystkie zaindeksowane pozycje z filtrami) i catalog.item (karta pozycji: tekstury, jakosc, grupy).
//
// Lista idzie w calosci (bez stronicowania) — UI rysuje tylko widoczne wiersze (siatka wirtualizowana); przy 5 000 pozycji to < 1 MB.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Duble.App.Komendy;

public static class KatalogPozycji
{
    static readonly Dictionary<Verdict, int> Ostrosc = new()
    {
        [Verdict.Duplicate] = 0, [Verdict.Superset] = 1, [Verdict.NeedsReview] = 2, [Verdict.Retexture] = 3,
    };

    public static void Zarejestruj(Mostek m, Sesja s)
    {
        Sesja Wymag() => s.Otwarty ? s : throw new BladMostka("no_project", "brak otwartego projektu");
        string Zrodlo(Garment p) => s.Project.Sources.Find(z => z.Id == p.SourceId)?.Name ?? p.PackName;

        // pozycja -> najostrzejszy werdykt zywej grupy, w ktorej jest (bez zignorowanych) + lista grup
        Dictionary<string, List<(DuplicateGroup g, Resolution r)>> GrupyPozycji()
        {
            var wy = new Dictionary<string, List<(DuplicateGroup, Resolution)>>();
            foreach (var (g, czl, r) in Grupy.Zywe(s))
                foreach (var p in czl)
                {
                    if (!wy.TryGetValue(p.Id, out var l)) wy[p.Id] = l = new();
                    l.Add((g, r));
                }
            return wy;
        }
        // the interface reads a verdict key, so the sharpest verdict in the group goes across as one
        string Werdykt(List<(DuplicateGroup g, Resolution r)> l)
            => l == null ? null : l.Where(x => !x.r.Ignored).Select(x => x.g.Verdict)
                .OrderBy(w => Ostrosc.TryGetValue(w, out var o) ? o : 9)
                .Select(w => w.ToKey()).FirstOrDefault();

        m.Rejestruj("catalog.list", a =>
        {
            Wymag();
            var zrodla = Mostek.Lista(a, "zrodla"); var sloty = Mostek.Lista(a, "sloty"); var formaty = Mostek.Lista(a, "formaty");
            bool problemy = Mostek.Flaga(a, "problemy"), wGrupie = Mostek.Flaga(a, "wGrupie");
            var szukaj = (Mostek.Text(a, "szukaj") ?? "").Trim().ToLowerInvariant();
            var grupy = GrupyPozycji();
            var wszystkie = s.Catalog.Garments;
            var filtrySloty = wszystkie.GroupBy(p => p.Slot).Select(g => new { typ = g.Key, n = g.Count() }).OrderBy(x => x.typ).ToList();
            var filtryZrodla = wszystkie.GroupBy(p => p.SourceId ?? "").Select(g => new { id = g.Key, nazwa = s.Project.Sources.Find(z => z.Id == g.Key)?.Name ?? g.Key, n = g.Count() }).OrderBy(x => x.nazwa).ToList();
            var pozycje = new List<object>();
            foreach (var p in wszystkie)
            {
                bool bezMipow = p.Textures.Any(t => t.MipLevels <= 1), bc1Alfa = p.Textures.Any(t => t.Format == "BC1" && t.AlphaShare > 0.02f), bc7 = p.Textures.Any(t => t.Format == "BC7");
                var werdykt = Werdykt(grupy.TryGetValue(p.Id, out var l) ? l : null);
                if (zrodla.Count > 0 && !zrodla.Contains(p.SourceId ?? "")) continue;
                if (sloty.Count > 0 && !sloty.Contains(p.Slot)) continue;
                if (formaty.Count > 0 && !formaty.Contains(p.GameFormat.ToLabel())) continue;
                if (problemy && !(bezMipow || bc1Alfa)) continue;
                if (wGrupie && werdykt == null) continue;
                if (szukaj.Length > 0 && !($"{p.Slot}_{p.Number:d3} {p.PackName} {p.Container} {Zrodlo(p)} {p.Id}").ToLowerInvariant().Contains(szukaj)) continue;
                pozycje.Add(new
                {
                    id = p.Id, zrodloId = p.SourceId, zrodlo = Zrodlo(p), kontener = p.Container, typ = p.Slot, numer = p.Number, sufiks = p.Suffix,
                    gen9 = p.GameFormat == GameFormat.Enhanced, props = p.IsProp, thumb = Widoki.Miniatura(p), tekstur = p.Textures.Count,
                    bajty = p.ModelSize + p.Textures.Sum(t => t.Size), wArchiwum = Widoki.WArchiwum(p),
                    bezMipow, bc1Alfa, bc7, grupa = werdykt,
                });
            }
            return new
            {
                razem = wszystkie.Count, tekstury = wszystkie.Sum(p => p.Textures.Count), pokazane = pozycje.Count,
                filtry = new { sloty = filtrySloty, zrodla = filtryZrodla, formaty = new { legacy = wszystkie.Count(p => p.GameFormat == GameFormat.Legacy), gen9 = wszystkie.Count(p => p.GameFormat == GameFormat.Enhanced) } },
                pozycje,
            };
        });

        m.Rejestruj("catalog.item", a =>
        {
            Wymag();
            var id = Mostek.Text(a, "id", true);
            var p = s.ZnajdzPozycje(id) ?? throw new BladMostka("not_found", id);
            var poz = Widoki.Czlonek(p, null, true, Zrodlo);
            var z = s.ZrodloPozycji(p);
            poz["zrodloSciezka"] = z?.Path;
            var wg = s.Catalog.Garments.ToDictionary(x => x.Id);
            var grupy = GrupyPozycji().TryGetValue(id, out var l) ? l : new();
            return new
            {
                pozycja = poz,
                grupy = grupy.Select(x => new
                {
                    id = x.g.Id, werdykt = x.g.Verdict.ToKey(), ignoruj = x.r.Ignored, powod = Widoki.Reason(x.g.Pairs.FirstOrDefault()?.Reason ?? x.g.Reason),
                    inni = x.g.Members.Where(i => i != id && wg.ContainsKey(i)).Select(i => new { id = i, nazwa = $"{wg[i].Slot}_{wg[i].Number:d3}", sufiks = wg[i].Suffix, zrodlo = Zrodlo(wg[i]) }).ToList(),
                    stan = x.r.Ignored ? "ignoruj" : x.r.Winner == id && x.r.Rejected.Count > 0 ? "zostaje" : x.r.Rejected.Contains(id) ? "odrzucona" : "neutral",
                }).ToList(),
            };
        });
    }
}
