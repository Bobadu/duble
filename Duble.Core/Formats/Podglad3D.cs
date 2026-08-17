// Podglad3D.cs — GLB dla pozycji z katalogu albo z surowych plikow (CLI `duble glb`).
//
// Diffuse texture: the first texture in the .ytd of the chosen variant (letter a/b/c…), decoded to RGBA PNG
// (the largest mip with a side <= 1024). Textures embedded in the .ydd (hair: diffuse + normal) come from its
// own dictionary.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using Duble.Core.Fingerprints;
using Duble.Core.Model;
using Duble.Core.Naming;
using Duble.Core.Sources;

namespace Duble.Core.Formats;

public static class Podglad3D
{
    const int MaksBok = 1024;

    public static byte[] Glb(IArchiveCache archiwa, Garment p, string litera = null, Action<string> log = null)
    {
        var model = archiwa.Read(p.ModelPath);
        var ydd = model.IsSuccess ? model.Value : throw new FileNotFoundException(model.Error.Message, p.ModelPath);
        var tex = p.Textures.FirstOrDefault(t => litera == null || string.Equals(ClothingFileName.ParseTexture(t.FileName)?.Letter, litera, StringComparison.OrdinalIgnoreCase))
                  ?? p.Textures.FirstOrDefault();
        byte[] ytd = null;
        if (tex != null) { var t = archiwa.Read(tex.Path); if (t.IsSuccess) ytd = t.Value; }
        return Glb(ydd, ytd, log);
    }

    public static byte[] Glb(byte[] ydd, byte[] ytd, Action<string> log = null)
    {
        log ??= _ => { };
        CodeWalkerRuntime.Initialize();   // gen9 mode reads both formats, by the RSC7 header of each file
        Drawable dr;
        try
        {
            var y = new YddFile(); RpfFile.LoadResourceFile(y, ydd, 165);
            dr = y.Drawables?.FirstOrDefault();
        }
        catch (Exception e) { throw new InvalidDataException("nie udalo sie wczytac modelu: " + e.Message, e); }
        var n0 = dr?.DrawableModels?.High?.FirstOrDefault()?.Geometries?.FirstOrDefault()?.VertexBuffer?.VertexCount ?? 0;
        if (n0 <= 0 || n0 > 5_000_000) throw new InvalidDataException("model bez sensownej geometrii (wierzcholkow: " + n0 + ")");
        var geos = Duble.Core.Formats.Glb.ZDrawable(dr);
        var pngi = new Dictionary<string, byte[]>();
        // osadzone tekstury (wlosy itp.)
        var td = dr.ShaderGroup?.TextureDictionary?.Textures?.data_items;
        foreach (var klucz in geos.SelectMany(g => new[] { g.TextureInfo, g.Normalna }).Where(k => k != null && k.StartsWith("emb:")).Distinct())
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
                if (png != null) pngi["diff"] = png; else log("[uwaga] tekstura diffuse bez podgladu (" + (t0 == null ? "pusty ytd" : TextureFingerprinter.FormatName(t0)) + ")");
            }
            catch (Exception e) { log("[uwaga] nie odczytalam ytd: " + e.Message); }
        }
        log($"geometrii {geos.Count}, wierzcholkow {geos.Sum(g => g.Pozycje.Length / 3)}, trojkatow {geos.Sum(g => g.Indeksy.Length / 3)}, tekstur {pngi.Count}");
        return Duble.Core.Formats.Glb.Zapisz(geos, pngi);
    }

    static byte[] PngZTekstury(Texture t) => TextureDecoder.PngRgba(t, MaksBok);

}
