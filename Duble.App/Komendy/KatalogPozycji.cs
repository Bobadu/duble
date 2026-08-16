// Komendy/KatalogPozycji.cs — catalog.list (wszystkie zaindeksowane pozycje z filtrami) i catalog.item (karta pozycji: tekstury, jakosc, grupy).
//
// Lista idzie w calosci (bez stronicowania) — UI rysuje tylko widoczne wiersze (siatka wirtualizowana); przy 5 000 pozycji to < 1 MB.
using System;
using System.Collections.Generic;
using System.Linq;
using Duble;

namespace Duble.App.Komendy;

public static class KatalogPozycji
{
    static readonly Dictionary<string, int> Ostrosc = new()
    {
        [Porownanie.Duplikat] = 0, [Porownanie.Nadzbior] = 1, [Porownanie.DoWgladu] = 2, [Porownanie.Przemalowanie] = 3,
    };

    public static void Zarejestruj(Mostek m, Sesja s)
    {
        Sesja Wymag() => s.Otwarty ? s : throw new BladMostka("no_project", "brak otwartego projektu");
        string Zrodlo(Pozycja p) => s.Projekt.Zrodla.Find(z => z.Id == p.ZrodloId)?.Nazwa ?? p.Paczka;

        // pozycja -> najostrzejszy werdykt zywej grupy, w ktorej jest (bez zignorowanych) + lista grup
        Dictionary<string, List<(Grupa g, Rozstrzygniecie r)>> GrupyPozycji()
        {
            var wy = new Dictionary<string, List<(Grupa, Rozstrzygniecie)>>();
            foreach (var (g, czl, r) in Grupy.Zywe(s))
                foreach (var p in czl)
                {
                    if (!wy.TryGetValue(p.Id, out var l)) wy[p.Id] = l = new();
                    l.Add((g, r));
                }
            return wy;
        }
        string Werdykt(List<(Grupa g, Rozstrzygniecie r)> l)
            => l == null ? null : l.Where(x => !x.r.Ignoruj).Select(x => x.g.Werdykt).OrderBy(w => Ostrosc.TryGetValue(w, out var o) ? o : 9).FirstOrDefault();

        m.Rejestruj("catalog.list", a =>
        {
            Wymag();
            var zrodla = Mostek.Lista(a, "zrodla"); var sloty = Mostek.Lista(a, "sloty"); var formaty = Mostek.Lista(a, "formaty");
            bool problemy = Mostek.Flaga(a, "problemy"), wGrupie = Mostek.Flaga(a, "wGrupie");
            var szukaj = (Mostek.Tekst(a, "szukaj") ?? "").Trim().ToLowerInvariant();
            var grupy = GrupyPozycji();
            var wszystkie = s.Katalog.Pozycje;
            var filtrySloty = wszystkie.GroupBy(p => p.Typ).Select(g => new { typ = g.Key, n = g.Count() }).OrderBy(x => x.typ).ToList();
            var filtryZrodla = wszystkie.GroupBy(p => p.ZrodloId ?? "").Select(g => new { id = g.Key, nazwa = s.Projekt.Zrodla.Find(z => z.Id == g.Key)?.Nazwa ?? g.Key, n = g.Count() }).OrderBy(x => x.nazwa).ToList();
            var pozycje = new List<object>();
            foreach (var p in wszystkie)
            {
                bool bezMipow = p.Tekstury.Any(t => t.Mipy <= 1), bc1Alfa = p.Tekstury.Any(t => t.Format == "BC1" && t.Alfa > 0.02f), bc7 = p.Tekstury.Any(t => t.Format == "BC7");
                var werdykt = Werdykt(grupy.TryGetValue(p.Id, out var l) ? l : null);
                if (zrodla.Count > 0 && !zrodla.Contains(p.ZrodloId ?? "")) continue;
                if (sloty.Count > 0 && !sloty.Contains(p.Typ)) continue;
                if (formaty.Count > 0 && !formaty.Contains(p.Gen9 ? "gen9" : "legacy")) continue;
                if (problemy && !(bezMipow || bc1Alfa)) continue;
                if (wGrupie && werdykt == null) continue;
                if (szukaj.Length > 0 && !($"{p.Typ}_{p.Numer:d3} {p.Paczka} {p.Kontener} {Zrodlo(p)} {p.Id}").ToLowerInvariant().Contains(szukaj)) continue;
                pozycje.Add(new
                {
                    id = p.Id, zrodloId = p.ZrodloId, zrodlo = Zrodlo(p), kontener = p.Kontener, typ = p.Typ, numer = p.Numer, sufiks = p.Sufiks,
                    gen9 = p.Gen9, props = p.Props, thumb = Widoki.Miniatura(p), tekstur = p.Tekstury.Count,
                    bajty = p.BajtyYdd + p.Tekstury.Sum(t => t.Bajty), wArchiwum = Widoki.WArchiwum(p),
                    bezMipow, bc1Alfa, bc7, grupa = werdykt,
                });
            }
            return new
            {
                razem = wszystkie.Count, tekstury = wszystkie.Sum(p => p.Tekstury.Count), pokazane = pozycje.Count,
                filtry = new { sloty = filtrySloty, zrodla = filtryZrodla, formaty = new { legacy = wszystkie.Count(p => !p.Gen9), gen9 = wszystkie.Count(p => p.Gen9) } },
                pozycje,
            };
        });

        m.Rejestruj("catalog.item", a =>
        {
            Wymag();
            var id = Mostek.Tekst(a, "id", true);
            var p = s.ZnajdzPozycje(id) ?? throw new BladMostka("not_found", id);
            var poz = Widoki.Czlonek(p, null, true, Zrodlo);
            var z = s.ZrodloPozycji(p);
            poz["zrodloSciezka"] = z?.Sciezka;
            var wg = s.Katalog.Pozycje.ToDictionary(x => x.Id);
            var grupy = GrupyPozycji().TryGetValue(id, out var l) ? l : new();
            return new
            {
                pozycja = poz,
                grupy = grupy.Select(x => new
                {
                    id = x.g.Id, werdykt = x.g.Werdykt, ignoruj = x.r.Ignoruj, powod = Widoki.Powod(x.g.Pary.FirstOrDefault()?.Powod ?? x.g.Powod),
                    inni = x.g.Pozycje.Where(i => i != id && wg.ContainsKey(i)).Select(i => new { id = i, nazwa = $"{wg[i].Typ}_{wg[i].Numer:d3}", sufiks = wg[i].Sufiks, zrodlo = Zrodlo(wg[i]) }).ToList(),
                    stan = x.r.Ignoruj ? "ignoruj" : x.r.Zwyciezca == id && x.r.Odrzucone.Count > 0 ? "zostaje" : x.r.Odrzucone.Contains(id) ? "odrzucona" : "neutral",
                }).ToList(),
            };
        });
    }
}
