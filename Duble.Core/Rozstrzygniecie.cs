// Rozstrzygniecie.cs — kto zostaje, kto odpada: domyslnie z porownania, a gdy uzytkownik zdecydowal (Decyzja) — po jego mysli.
//
// Kontrakt: brak Decyzji = domyslne (DUPLIKAT/NADZBIOR: zwyciezca zostaje, reszta odrzucona; DO WGLADU/PRZEMALOWANIE:
// nikt odrzucony). Decyzja jest autorytatywna (takze pusta lista Odrzucone = nikt); Ignoruj = "to nie duplikat".
using System.Collections.Generic;
using System.Linq;

namespace Duble;

public sealed class Rozstrzygniecie
{
    public string Zwyciezca { get; set; }
    public List<string> Odrzucone { get; set; } = new();
    public bool Ignoruj { get; set; }
    /// <summary>true = z porownania (uzytkownik nic nie zmienial).</summary>
    public bool Domyslna { get; set; }
    public string Notatka { get; set; }

    public static Rozstrzygniecie Policz(Grupa g, Decyzja d)
    {
        var czlonkowie = g.Pozycje ?? new List<string>();
        var r = new Rozstrzygniecie { Zwyciezca = g.Zwyciezca ?? czlonkowie.FirstOrDefault(), Domyslna = d == null, Notatka = d?.Notatka };
        if (d == null)
        {
            if (g.Werdykt == Porownanie.Duplikat || g.Werdykt == Porownanie.Nadzbior)
                r.Odrzucone = czlonkowie.Where(x => x != r.Zwyciezca).ToList();
            return r;
        }
        r.Ignoruj = d.Ignoruj;
        if (!string.IsNullOrEmpty(d.Zwyciezca) && czlonkowie.Contains(d.Zwyciezca)) r.Zwyciezca = d.Zwyciezca;
        if (!d.Ignoruj && d.Odrzucone != null)
            r.Odrzucone = d.Odrzucone.Where(x => x != r.Zwyciezca && czlonkowie.Contains(x)).Distinct().ToList();
        return r;
    }

    /// <summary>Po ponownym porownaniu (Zastosuj, ponowne indeksowanie, wylaczenie zrodla) grupy zmieniaja sklad i id — decyzja
    /// uzytkownika zostalaby w slowniku pod martwym id, a nowa (mniejsza) grupa wrocilaby do domyslnego "do odrzucenia".
    /// Dlatego nowa grupa BEZ decyzji dziedziczy decyzje najmniejszej starej grupy, ktorej jest podzbiorem: zwyciezca (jesli nadal
    /// w grupie), odrzuceni (czesc wspolna), Ignoruj, notatka. Zwraca liczbe dodanych decyzji.</summary>
    public static int PrzeniesDecyzje(Dictionary<string, Decyzja> decyzje, IEnumerable<Grupa> stare, IEnumerable<Grupa> nowe)
    {
        if (decyzje == null || decyzje.Count == 0 || stare == null || nowe == null) return 0;
        string IdGrupy(Grupa g) => g.Id ?? Grupa.PoliczId(g.Pozycje ?? new List<string>());
        var stareZDecyzja = stare.Where(g => g.Pozycje != null && g.Pozycje.Count > 0)
                                 .Select(g => (id: IdGrupy(g), czl: new HashSet<string>(g.Pozycje)))
                                 .Where(x => decyzje.ContainsKey(x.id)).ToList();
        if (stareZDecyzja.Count == 0) return 0;
        int dodane = 0;
        foreach (var g in nowe)
        {
            if (g.Pozycje == null || g.Pozycje.Count == 0) continue;
            var id = IdGrupy(g);
            if (decyzje.ContainsKey(id)) continue;
            var nadgrupa = stareZDecyzja.Where(x => x.id != id && g.Pozycje.All(x.czl.Contains)).OrderBy(x => x.czl.Count).FirstOrDefault();
            if (nadgrupa.id == null) continue;
            var d = decyzje[nadgrupa.id];
            decyzje[id] = new Decyzja
            {
                Zwyciezca = d.Zwyciezca != null && g.Pozycje.Contains(d.Zwyciezca) ? d.Zwyciezca : null,
                Odrzucone = (d.Odrzucone ?? new List<string>()).Where(g.Pozycje.Contains).ToList(),
                Ignoruj = d.Ignoruj, Notatka = d.Notatka,
            };
            dodane++;
        }
        return dodane;
    }
}
