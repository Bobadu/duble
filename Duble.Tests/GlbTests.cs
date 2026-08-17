using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

public class GlbTests
{
    readonly ITestOutputHelper wyj;
    public GlbTests(ITestOutputHelper wyj) { this.wyj = wyj; }

    static SiatkaGeo Kwadrat() => new SiatkaGeo
    {
        Nazwa = "kwadrat", Tekstura = "diff", Przezroczysta = false,
        Pozycje = new float[] { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0 },
        Normalne = new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 },
        Uv = new float[] { 0, 1, 1, 1, 1, 0, 0, 0 },
        Indeksy = new uint[] { 0, 1, 2, 0, 2, 3 },
    };

    static byte[] Png2x2() => Png.Rgba(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 128 }, 2, 2);

    static JsonElement Json(byte[] glb)
    {
        uint dlJson = BitConverter.ToUInt32(glb, 12);
        return JsonDocument.Parse(Encoding.UTF8.GetString(glb, 20, (int)dlJson)).RootElement;
    }

    [Fact]
    public void Glb_ma_poprawne_naglowki_i_liczby()
    {
        var glb = Glb.Zapisz(new[] { Kwadrat() }, new Dictionary<string, byte[]> { ["diff"] = Png2x2() });
        Assert.Equal(0x46546C67u, BitConverter.ToUInt32(glb, 0));   // "glTF"
        Assert.Equal(2u, BitConverter.ToUInt32(glb, 4));
        Assert.Equal((uint)glb.Length, BitConverter.ToUInt32(glb, 8));
        uint dlJson = BitConverter.ToUInt32(glb, 12); Assert.Equal(0x4E4F534Au, BitConverter.ToUInt32(glb, 16));   // JSON
        var json = Json(glb);
        int offBin = 20 + (int)dlJson;
        uint dlBin = BitConverter.ToUInt32(glb, offBin); Assert.Equal(0x004E4942u, BitConverter.ToUInt32(glb, offBin + 4));   // BIN
        Assert.Equal(glb.Length, offBin + 8 + (int)dlBin);
        Assert.Equal((int)dlBin, json.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32());
        Assert.Equal(0u, dlBin % 4);

        var prim = json.GetProperty("meshes")[0].GetProperty("primitives")[0];
        var acc = json.GetProperty("accessors");
        int iPos = prim.GetProperty("attributes").GetProperty("POSITION").GetInt32();
        Assert.Equal(4, acc[iPos].GetProperty("count").GetInt32());
        Assert.Equal("VEC3", acc[iPos].GetProperty("type").GetString());
        Assert.Equal(new[] { 0f, 0f, 0f }, acc[iPos].GetProperty("min").EnumerateArray().Select(x => x.GetSingle()).ToArray());
        Assert.Equal(new[] { 1f, 1f, 0f }, acc[iPos].GetProperty("max").EnumerateArray().Select(x => x.GetSingle()).ToArray());
        int iIdx = prim.GetProperty("indices").GetInt32();
        Assert.Equal(6, acc[iIdx].GetProperty("count").GetInt32());
        Assert.Equal(5125, acc[iIdx].GetProperty("componentType").GetInt32());   // UNSIGNED_INT
        Assert.True(prim.TryGetProperty("material", out _));
        Assert.Equal("image/png", json.GetProperty("images")[0].GetProperty("mimeType").GetString());
        var mat = json.GetProperty("materials")[0];
        Assert.Equal("OPAQUE", mat.GetProperty("alphaMode").GetString());
        Assert.True(mat.GetProperty("doubleSided").GetBoolean());
        Assert.Equal(1, json.GetProperty("nodes").GetArrayLength());
        Assert.Equal(1, json.GetProperty("scenes")[0].GetProperty("nodes").GetArrayLength());
    }

    [Fact]
    public void Geometria_bez_tekstury_i_przezroczysta_dostaje_odpowiedni_material()
    {
        var a = Kwadrat(); a.Tekstura = null;
        var b = Kwadrat(); b.Przezroczysta = true;
        var glb = Glb.Zapisz(new[] { a, b }, new Dictionary<string, byte[]> { ["diff"] = Png2x2() });
        var json = Json(glb);
        var mats = json.GetProperty("materials");
        Assert.Equal(2, mats.GetArrayLength());
        Assert.False(mats[0].GetProperty("pbrMetallicRoughness").TryGetProperty("baseColorTexture", out _));
        Assert.Equal("MASK", mats[1].GetProperty("alphaMode").GetString());
        Assert.Equal(2, json.GetProperty("meshes")[0].GetProperty("primitives").GetArrayLength());
    }

    [Fact]
    public void Png_rgba_ma_typ_koloru_6()
    {
        var png = Png2x2();
        Assert.Equal(0x89, png[0]); Assert.Equal(6, png[25]);   // bajt typu koloru w IHDR
    }

    [Fact, Trait("Kategoria", "Wolny")]
    public void Glb_z_prawdziwego_modelu_studio_body()
    {
        if (!Sciezki.JestGra) { wyj.WriteLine("POMINIETY: brak studio_body\\dlc.rpf"); return; }
        var poz = Indeks.Zrodlo(Sciezki.Dlc("studio_body"), "studio_body", s => { });
        var uppr = poz.First(p => p.Typ == "uppr" && p.Numer == 15);
        var glb = Podglad3D.Glb(uppr, null, wyj.WriteLine);
        Assert.True(glb.Length > 10000);
        var json = Json(glb);
        Assert.True(json.GetProperty("meshes")[0].GetProperty("primitives").GetArrayLength() >= 1);
        Assert.True(json.GetProperty("images").GetArrayLength() >= 1);
        var plik = Path.Combine(Path.GetTempPath(), "duble-tests", "uppr_015.glb");
        Directory.CreateDirectory(Path.GetDirectoryName(plik)); File.WriteAllBytes(plik, glb);
        wyj.WriteLine("GLB: " + plik);
    }
}
