// Glb.cs — zapis siatki (najwyzszy LOD) z teksturami do glTF-Binary 2.0, do podgladu 3D w aplikacji (three.js).
//
// Uklad osi: GTA ma Z w gore, glTF Y w gore -> (x, y, z) => (x, z, -y). UV zostaja (poczatek u gory).
// Jedna siatka = jeden mesh z wieloma prymitywami (po jednym na geometrie), material per prymityw:
// baseColorTexture z PNG, metallic 0, roughness 0.9, doubleSided; przezroczyste: alphaMode MASK, cutoff 0.5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CodeWalker.GameFiles;

namespace Duble.Core.Formats;

public sealed class SiatkaGeo
{
    public string Name { get; set; }
    public float[] Pozycje { get; set; }    // 3 na wierzcholek (juz w ukladzie glTF)
    public float[] Normalne { get; set; }   // 3 na wierzcholek albo null
    public float[] Uv { get; set; }         // 2 na wierzcholek albo null
    public uint[] Indeksy { get; set; }     // trojkaty
    public string TextureInfo { get; set; }    // klucz w slowniku PNG albo null
    public string Normalna { get; set; }    // klucz mapy normalnych albo null
    public bool Przezroczysta { get; set; }
}

public static class Glb
{
    public static byte[] Zapisz(IReadOnlyList<SiatkaGeo> geos, IReadOnlyDictionary<string, byte[]> pngi)
    {
        var bin = new MemoryStream();
        var bufferViews = new List<object>(); var accessors = new List<object>();
        var images = new List<object>(); var textures = new List<object>(); var materials = new List<object>();
        var primitives = new List<object>();
        var indeksObrazu = new Dictionary<string, int>();

        int DodajWidok(byte[] dane, int? target)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            var v = new Dictionary<string, object> { ["buffer"] = 0, ["byteOffset"] = (int)bin.Length, ["byteLength"] = dane.Length };
            if (target != null) v["target"] = target.Value;
            bin.Write(dane, 0, dane.Length);
            bufferViews.Add(v);
            return bufferViews.Count - 1;
        }
        int DodajFloaty(float[] dane, int skladowych, bool minMax)
        {
            var bajty = new byte[dane.Length * 4]; Buffer.BlockCopy(dane, 0, bajty, 0, bajty.Length);
            int bv = DodajWidok(bajty, 34962);
            int n = dane.Length / skladowych;
            var a = new Dictionary<string, object> { ["bufferView"] = bv, ["componentType"] = 5126, ["count"] = n, ["type"] = skladowych == 3 ? "VEC3" : "VEC2" };
            if (minMax)
            {
                var mn = Enumerable.Repeat(float.MaxValue, skladowych).ToArray(); var mx = Enumerable.Repeat(float.MinValue, skladowych).ToArray();
                for (int i = 0; i < dane.Length; i++) { int k = i % skladowych; mn[k] = Math.Min(mn[k], dane[i]); mx[k] = Math.Max(mx[k], dane[i]); }
                a["min"] = mn; a["max"] = mx;
            }
            accessors.Add(a);
            return accessors.Count - 1;
        }
        int DodajIndeksy(uint[] idx)
        {
            var bajty = new byte[idx.Length * 4]; Buffer.BlockCopy(idx, 0, bajty, 0, bajty.Length);
            int bv = DodajWidok(bajty, 34963);
            accessors.Add(new Dictionary<string, object> { ["bufferView"] = bv, ["componentType"] = 5125, ["count"] = idx.Length, ["type"] = "SCALAR" });
            return accessors.Count - 1;
        }
        int? Obraz(string klucz)
        {
            if (klucz == null || pngi == null || !pngi.TryGetValue(klucz, out var png) || png == null) return null;
            if (indeksObrazu.TryGetValue(klucz, out int juz)) return juz;
            int bv = DodajWidok(png, null);
            images.Add(new Dictionary<string, object> { ["bufferView"] = bv, ["mimeType"] = "image/png", ["name"] = klucz });
            textures.Add(new Dictionary<string, object> { ["source"] = images.Count - 1, ["sampler"] = 0 });
            indeksObrazu[klucz] = textures.Count - 1;
            return textures.Count - 1;
        }

        foreach (var g in geos)
        {
            if (g?.Pozycje == null || g.Indeksy == null || g.Pozycje.Length < 9) continue;
            var attrs = new Dictionary<string, object> { ["POSITION"] = DodajFloaty(g.Pozycje, 3, true) };
            if (g.Normalne != null && g.Normalne.Length == g.Pozycje.Length) attrs["NORMAL"] = DodajFloaty(g.Normalne, 3, false);
            if (g.Uv != null && g.Uv.Length / 2 == g.Pozycje.Length / 3) attrs["TEXCOORD_0"] = DodajFloaty(g.Uv, 2, false);
            var pbr = new Dictionary<string, object> { ["metallicFactor"] = 0.0, ["roughnessFactor"] = 0.9, ["baseColorFactor"] = new[] { 1.0, 1.0, 1.0, 1.0 } };
            var tex = Obraz(g.TextureInfo);
            if (tex != null) pbr["baseColorTexture"] = new Dictionary<string, object> { ["index"] = tex.Value };
            else pbr["baseColorFactor"] = new[] { 0.72, 0.72, 0.74, 1.0 };
            var mat = new Dictionary<string, object> { ["name"] = g.Name ?? "geo", ["pbrMetallicRoughness"] = pbr, ["doubleSided"] = true, ["alphaMode"] = g.Przezroczysta ? "MASK" : "OPAQUE" };
            if (g.Przezroczysta) mat["alphaCutoff"] = 0.5;
            var nrm = Obraz(g.Normalna);
            if (nrm != null) mat["normalTexture"] = new Dictionary<string, object> { ["index"] = nrm.Value };
            materials.Add(mat);
            primitives.Add(new Dictionary<string, object> { ["attributes"] = attrs, ["indices"] = DodajIndeksy(g.Indeksy), ["material"] = materials.Count - 1, ["mode"] = 4 });
        }
        while (bin.Length % 4 != 0) bin.WriteByte(0);

        var root = new Dictionary<string, object>
        {
            ["asset"] = new Dictionary<string, object> { ["version"] = "2.0", ["generator"] = "Duble by Bobadu" },
            ["scene"] = 0,
            ["scenes"] = new[] { new Dictionary<string, object> { ["nodes"] = new[] { 0 } } },
            ["nodes"] = new[] { new Dictionary<string, object> { ["mesh"] = 0, ["name"] = "duble" } },
            ["meshes"] = new[] { new Dictionary<string, object> { ["primitives"] = primitives, ["name"] = "duble" } },
            ["materials"] = materials,
            ["accessors"] = accessors,
            ["bufferViews"] = bufferViews,
            ["buffers"] = new[] { new Dictionary<string, object> { ["byteLength"] = (int)bin.Length } },
            ["samplers"] = new[] { new Dictionary<string, object> { ["magFilter"] = 9729, ["minFilter"] = 9987, ["wrapS"] = 10497, ["wrapT"] = 10497 } },
        };
        if (images.Count > 0) { root["images"] = images; root["textures"] = textures; }

        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(root));
        int padJson = (4 - json.Length % 4) % 4;
        var binBajty = bin.ToArray();
        using var wy = new MemoryStream();
        var bw = new BinaryWriter(wy);
        bw.Write(0x46546C67u); bw.Write(2u); bw.Write((uint)(12 + 8 + json.Length + padJson + 8 + binBajty.Length));
        bw.Write((uint)(json.Length + padJson)); bw.Write(0x4E4F534Au); bw.Write(json); for (int i = 0; i < padJson; i++) bw.Write((byte)0x20);
        bw.Write((uint)binBajty.Length); bw.Write(0x004E4942u); bw.Write(binBajty);
        return wy.ToArray();
    }

    /// <summary>Geometrie najwyzszego LOD z Drawable (pozycje/normalne juz w ukladzie glTF).</summary>
    public static List<SiatkaGeo> ZDrawable(Drawable dr)
    {
        var wy = new List<SiatkaGeo>();
        if (dr?.DrawableModels?.High == null) return wy;
        var osadzone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var td = dr.ShaderGroup?.TextureDictionary?.Textures?.data_items;
        if (td != null) foreach (var t in td) if (t?.Name != null) osadzone.Add(t.Name);
        var shs = dr.ShaderGroup?.Shaders?.data_items;
        int gi = 0;
        foreach (var m in dr.DrawableModels.High)
        {
            foreach (var geo in m?.Geometries ?? Array.Empty<DrawableGeometry>())
            {
                var vd = geo?.VertexBuffer?.Data1 ?? geo?.VertexBuffer?.Data2;
                if (vd?.VertexBytes == null || vd.Info == null || geo.IndexBuffer?.Indices == null) continue;
                var info = vd.Info; int stride = (int)info.Stride; int n = (int)vd.VertexCount; var b = vd.VertexBytes;
                bool maNorm = ((info.Flags >> 3) & 1) == 1, maUv = ((info.Flags >> 6) & 1) == 1;
                int offP = info.GetComponentOffset(0), offN = info.GetComponentOffset(3), offT = info.GetComponentOffset(6);
                var typT = info.GetComponentType(6);
                var s = new SiatkaGeo { Name = "geo_" + gi, Pozycje = new float[n * 3], Normalne = maNorm ? new float[n * 3] : null, Uv = maUv ? new float[n * 2] : null };
                for (int v = 0; v < n; v++)
                {
                    int o = v * stride;
                    float x = BitConverter.ToSingle(b, o + offP), y = BitConverter.ToSingle(b, o + offP + 4), z = BitConverter.ToSingle(b, o + offP + 8);
                    s.Pozycje[v * 3] = x; s.Pozycje[v * 3 + 1] = z; s.Pozycje[v * 3 + 2] = -y;
                    if (maNorm)
                    {
                        float nx = BitConverter.ToSingle(b, o + offN), ny = BitConverter.ToSingle(b, o + offN + 4), nz = BitConverter.ToSingle(b, o + offN + 8);
                        s.Normalne[v * 3] = nx; s.Normalne[v * 3 + 1] = nz; s.Normalne[v * 3 + 2] = -ny;
                    }
                    if (maUv)
                    {
                        float tu, tv;
                        if (typT == VertexComponentType.Half2) { tu = (float)BitConverter.ToHalf(b, o + offT); tv = (float)BitConverter.ToHalf(b, o + offT + 2); }
                        else { tu = BitConverter.ToSingle(b, o + offT); tv = BitConverter.ToSingle(b, o + offT + 4); }
                        s.Uv[v * 2] = tu; s.Uv[v * 2 + 1] = tv;
                    }
                }
                var idx = geo.IndexBuffer.Indices;
                s.Indeksy = new uint[idx.Length - idx.Length % 3];
                for (int i = 0; i < s.Indeksy.Length; i++) s.Indeksy[i] = idx[i];

                // shader: nazwa (przezroczystosc) + tekstury diffuse/bump
                s.TextureInfo = "diff";
                if (shs != null && geo.ShaderID < shs.Length && shs[geo.ShaderID] != null)
                {
                    var sh = shs[geo.ShaderID];
                    var nazwaSh = (sh.Name.ToString() + " " + sh.FileName.ToString()).ToLowerInvariant();
                    s.Przezroczysta = nazwaSh.Contains("alpha") || nazwaSh.Contains("cutout") || nazwaSh.Contains("hair") || nazwaSh.Contains("decal");
                    var prs = sh.ParametersList?.Parameters; var hs = sh.ParametersList?.Hashes;
                    if (prs != null && hs != null)
                        for (int k = 0; k < prs.Length && k < hs.Length; k++)
                        {
                            if (prs[k].DataType != 0 || prs[k].Data is not TextureBase tb || string.IsNullOrEmpty(tb.Name)) continue;
                            uint hash = (uint)hs[k];
                            if (hash == (uint)ShaderParamNames.DiffuseSampler && osadzone.Contains(tb.Name)) s.TextureInfo = "emb:" + tb.Name;
                            if (hash == (uint)ShaderParamNames.BumpSampler && osadzone.Contains(tb.Name)) s.Normalna = "emb:" + tb.Name;
                        }
                }
                wy.Add(s); gi++;
            }
        }
        return wy;
    }
}
