// Komendy/Widoki.cs — wspolna serializacja pozycji/czlonka grupy do JSON dla UI (Duplikaty, Katalog, karta pozycji).
//
// podst: id, zrodlo, typ/numer/sufiks, format, punkty, miniatura, liczniki; szczegoly: rozpiska jakosci, sciezki, lista tekstur.
// Bez grupy (Katalog) punkty i rozpiska licza sie z QualityScorer.Score(p).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Duble.App.Komendy;

public static class Widoki
{
    // Widoki jest statyczne (komendy tez) — jedna instancja punktacji wystarczy, do czasu az most
    // dostanie swoje uslugi w etapie 2.
    static readonly IQualityScorer Punktacja = new QualityScorer();

    public static object Reason(Reason p) => p == null ? null : new { kod = p.Code, p = p.Parameters };
    public static object Rozstrz(Resolution r) => new { zwyciezca = r.Winner, odrzucone = r.Rejected, ignoruj = r.Ignored, domyslna = r.IsDefault, notatka = r.Note };
    public static object Punkt(QualityScore p) => p == null ? null : new { razem = p.Total, rozdz = p.Resolution, mipy = p.Mipmaps, warianty = p.Variants, format = p.Format, lod = p.Lod, rozdzPx = p.ResolutionPx, udzialMipow = p.MipmapShare, liczbaWariantow = p.VariantCount, zlyFormat = p.WrongFormatCount, lody = p.LodLevels, brakTekstur = p.NoTextures };

    public static bool WArchiwum(Garment p) => p.ModelPath != null && p.ModelPath.Contains('|');
    public static string Miniatura(Garment p) => p.Textures.FirstOrDefault(t => t.IsDecoded && t.Sha256 != null)?.Sha256;

    /// <summary>Czlonek grupy (g != null: punkty/rozpiska z grupy) albo samodzielna pozycja (g == null: z Jakosc).</summary>
    public static Dictionary<string, object> Czlonek(Garment p, DuplicateGroup g, bool szczegoly, Func<Garment, string> zrodlo)
    {
        double punkty; QualityScore rozpiska = null;
        if (g != null) { punkty = g.Scores.TryGetValue(p.Id, out var pkt) ? pkt : 0.0; if (szczegoly) rozpiska = g.ScoreBreakdown.TryGetValue(p.Id, out var r) ? r : null; }
        else { var j = Punktacja.Score(p); punkty = j.Total; rozpiska = j; }
        var podst = new Dictionary<string, object>
        {
            ["id"] = p.Id, ["zrodloId"] = p.SourceId, ["zrodlo"] = zrodlo(p), ["kontener"] = p.Container, ["typ"] = p.Slot, ["numer"] = p.Number, ["sufiks"] = p.Suffix,
            ["gen9"] = p.GameFormat == GameFormat.Enhanced, ["props"] = p.IsProp, ["punkty"] = punkty, ["thumb"] = Miniatura(p),
            ["tekstur"] = p.Textures.Count, ["wierzcholki"] = p.Geometry?.Vertices ?? 0, ["trojkaty"] = p.Geometry?.Triangles ?? 0, ["lody"] = p.Geometry?.LodLevels ?? 0,
            ["bajty"] = p.ModelSize + p.Textures.Sum(t => t.Size), ["wArchiwum"] = WArchiwum(p),
        };
        if (szczegoly)
        {
            podst["rozpiska"] = Punkt(rozpiska);
            podst["sciezkaYdd"] = p.ModelPath;
            podst["bajtyYdd"] = p.ModelSize;
            // `litera` (wariant koloru) liczy Core — UI mial na to wlasny regexp w dwoch plikach i oba gubily
            // propsy (p_ears_diff_017_a.ytd nie ma po literze rasy)
            podst["tekstury"] = p.Textures.Select(t => new
            {
                sha = t.Sha256, plik = t.FileName, nazwa = t.Name,
                litera = ClothingFileName.ParseTexture(t.FileName)?.Letter,
                w = t.Width, h = t.Height, format = t.Format, mipy = t.MipLevels,
                alfa = t.AlphaShare, zdekodowana = t.IsDecoded, bajty = t.Size,
            }).ToList();
        }
        return podst;
    }
}
