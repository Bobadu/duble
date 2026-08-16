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
}
