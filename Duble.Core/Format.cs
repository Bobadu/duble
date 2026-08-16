// Format.cs — tryb czytania CodeWalkera: ZAWSZE gen9.
//
// CodeWalker w trybie gen9 rozpoznaje format kazdego pliku po wersji z naglowka RSC7 (ydd 165/159,
// ytd 13/5) i czyta Legacy poprawnie; tryb legacy NIE czyta gen9. Wiec flage ustawiamy raz, przy
// zaladowaniu biblioteki, i nigdy nie zmieniamy — zero wyscigow miedzy indeksowaniem a podgladem.
// Etykiete Legacy/Enhanced bierzemy z naglowka pliku (Rsc7.Gen9), nie z trybu czytania.
// (Pomiar 16.08: legacy jbib_000_u.ydd czytany w obu trybach daje te same wierzcholki/stride/bbox;
//  gen9 uppr_015_r.ydd w trybie legacy: wyjatek "illegal position".)
using System.Runtime.CompilerServices;
using CodeWalker.GameFiles;

namespace Duble;

public static class Format
{
    [ModuleInitializer]
    internal static void Start() => Przygotuj();

    public static void Przygotuj() => RpfManager.IsGen9 = true;

    public static string Nazwa(bool? gen9) => gen9 == true ? "gen9" : gen9 == false ? "legacy" : "?";
}
