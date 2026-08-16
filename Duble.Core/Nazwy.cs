// Nazwy.cs — rozbior nazw plikow ubran.
//
// Konwencja R* (w folderze/kontenerze):  <typ>_<NNN>_<u|r>[_k].ydd   <typ>_diff_<NNN>_<litera>_<rasa>[_k].ytd
// Propsy:                                 p_<anchor>_<NNN>[_k].ydd    p_<anchor>_diff_<NNN>_<litera>[_k].ytd
// FiveM (zasoby stream\):                 <ped>_<paczka>^<nazwa jak wyzej>  — czesc przed '^' to kontener.
// Ogonek "_k" (np. jbib_022_u_1) dokladaja eksportery przy kolizji nazw; wchodzi do Sufiksu.
using System.Text.RegularExpressions;

namespace Duble;

public sealed record NazwaModelu(string Typ, int Numer, string Sufiks, bool Props, string Kontener);
public sealed record NazwaTekstury(string Typ, int Numer, string Litera, string Rasa, bool Props, string Kontener);

public static class Nazwy
{
    static readonly Regex ReModel = new(@"^([a-z]{4})_(\d{3})_([ur])(_\d+)?\.ydd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex ReTekstura = new(@"^([a-z]{4})_diff_(\d{3})_([a-z])_([a-z]+)(_\d+)?\.ytd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RePropModel = new(@"^p_([a-z]+)_(\d{3})(_\d+)?\.ydd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RePropTekstura = new(@"^p_([a-z]+)_diff_(\d{3})_([a-z])(_\d+)?\.ytd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"mp_f_freemode_01_paczka^jbib_000_u.ydd" -> ("mp_f_freemode_01_paczka", "jbib_000_u.ydd"); bez '^' -> (null, nazwa).</summary>
    public static (string prefiks, string nazwa) RozdzielFiveM(string nazwaPliku)
    {
        if (nazwaPliku == null) return (null, null);
        int i = nazwaPliku.IndexOf('^');
        return i < 0 ? (null, nazwaPliku) : (nazwaPliku.Substring(0, i), nazwaPliku.Substring(i + 1));
    }

    public static NazwaModelu Model(string nazwaPliku)
    {
        var (prefiks, nazwa) = RozdzielFiveM(nazwaPliku);
        if (nazwa == null) return null;
        var m = ReModel.Match(nazwa);
        if (m.Success)
            return new NazwaModelu(m.Groups[1].Value.ToLowerInvariant(), int.Parse(m.Groups[2].Value),
                (m.Groups[3].Value + m.Groups[4].Value).ToLowerInvariant(), false, prefiks);
        var pm = RePropModel.Match(nazwa);
        if (pm.Success)
            return new NazwaModelu("p_" + pm.Groups[1].Value.ToLowerInvariant(), int.Parse(pm.Groups[2].Value),
                "u" + pm.Groups[3].Value.ToLowerInvariant(), true, prefiks);
        return null;
    }

    public static NazwaTekstury Tekstura(string nazwaPliku)
    {
        var (prefiks, nazwa) = RozdzielFiveM(nazwaPliku);
        if (nazwa == null) return null;
        var m = ReTekstura.Match(nazwa);
        if (m.Success)
            return new NazwaTekstury(m.Groups[1].Value.ToLowerInvariant(), int.Parse(m.Groups[2].Value),
                m.Groups[3].Value.ToLowerInvariant(), m.Groups[4].Value.ToLowerInvariant(), false, prefiks);
        var pm = RePropTekstura.Match(nazwa);
        if (pm.Success)
            return new NazwaTekstury("p_" + pm.Groups[1].Value.ToLowerInvariant(), int.Parse(pm.Groups[2].Value),
                pm.Groups[3].Value.ToLowerInvariant(), "uni", true, prefiks);
        return null;
    }
}
