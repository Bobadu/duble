// Podglad3D.cs — GLB dla pozycji z katalogu albo z surowych plikow (CLI `duble glb`).
//
// Tekstura diffuse: pierwsza tekstura z .ytd wybranego wariantu (litera a/b/c...), zdekodowana do PNG RGBA
// (najwiekszy mip o boku <= 1024). Tekstury osadzone w .ydd (wlosy: diffuse + normal) — z jego slownika.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;

namespace Duble;

public static class Podglad3D
{
    const int MaksBok = 1024;

    public static byte[] Glb(Pozycja p, string litera = null, Action<string> log = null)
    {
        var ydd = Zrodla.Bajty(p.SciezkaYdd) ?? throw new FileNotFoundException("brak modelu: " + p.SciezkaYdd);
        var tex = p.Tekstury.FirstOrDefault(t => litera == null || string.Equals(Nazwy.Tekstura(t.Plik)?.Litera, litera, StringComparison.OrdinalIgnoreCase))
                  ?? p.Tekstury.FirstOrDefault();
        var ytd = tex == null ? null : Zrodla.Bajty(tex.Sciezka);
        return Glb(ydd, ytd, p.Gen9, log);
    }

    public static byte[] Glb(byte[] ydd, byte[] ytd, bool? gen9, Action<string> log = null)
    {
        log ??= _ => { };
        Format.Przygotuj();   // tryb gen9 czyta oba formaty (po naglowku RSC7); parametr gen9 zostaje dla zgodnosci
        Drawable dr;
        try
        {
            var y = new YddFile(); RpfFile.LoadResourceFile(y, ydd, 165);
            dr = y.Drawables?.FirstOrDefault();
        }
        catch (Exception e) { throw new InvalidDataException("nie udalo sie wczytac modelu: " + e.Message, e); }
        var n0 = dr?.DrawableModels?.High?.FirstOrDefault()?.Geometries?.FirstOrDefault()?.VertexBuffer?.VertexCount ?? 0;
        if (n0 <= 0 || n0 > 5_000_000) throw new InvalidDataException("model bez sensownej geometrii (wierzcholkow: " + n0 + ")");
        var geos = Duble.Glb.ZDrawable(dr);
        var pngi = new Dictionary<string, byte[]>();
        // osadzone tekstury (wlosy itp.)
        var td = dr.ShaderGroup?.TextureDictionary?.Textures?.data_items;
        foreach (var klucz in geos.SelectMany(g => new[] { g.Tekstura, g.Normalna }).Where(k => k != null && k.StartsWith("emb:")).Distinct())
        {
            var t = td?.FirstOrDefault(x => string.Equals(x?.Name, klucz.Substring(4), StringComparison.OrdinalIgnoreCase));
            var png = t == null ? null : PngZTekstury(t);
            if (png != null) pngi[klucz] = png; else log($"[uwaga] osadzona tekstura {klucz} bez podgladu");
        }
        if (ytd != null)
        {
            try
            {
                var yt = new YtdFile(); RpfFile.LoadResourceFile(yt, ytd, 13);
                var t0 = yt.TextureDict?.Textures?.data_items?.FirstOrDefault();
                var png = t0 == null ? null : PngZTekstury(t0);
                if (png != null) pngi["diff"] = png; else log("[uwaga] tekstura diffuse bez podgladu (" + (t0 == null ? "pusty ytd" : Odciski.NazwaFormatu(t0)) + ")");
            }
            catch (Exception e) { log("[uwaga] nie odczytalam ytd: " + e.Message); }
        }
        log($"geometrii {geos.Count}, wierzcholkow {geos.Sum(g => g.Pozycje.Length / 3)}, trojkatow {geos.Sum(g => g.Indeksy.Length / 3)}, tekstur {pngi.Count}");
        return Duble.Glb.Zapisz(geos, pngi);
    }

    /// <summary>PNG RGBA z najwiekszego mipa o boku &lt;= MaksBok.</summary>
    static byte[] PngZTekstury(Texture t)
    {
        int mip = 0;
        while ((t.Width >> mip) > MaksBok && (t.Height >> mip) > MaksBok && mip < t.Levels - 1) mip++;
        var px = Tekstury.Piksele(t, mip, out int w, out int h);
        if (px == null) return null;
        var rgba = new byte[px.Length];
        for (int i = 0; i < px.Length; i += 4) { rgba[i] = px[i + 2]; rgba[i + 1] = px[i + 1]; rgba[i + 2] = px[i]; rgba[i + 3] = px[i + 3]; }
        return Png.Rgba(rgba, w, h);
    }
}
