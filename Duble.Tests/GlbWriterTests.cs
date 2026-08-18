using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

/// <summary>
/// The .glb the 3D preview shows. A viewer will not tell you WHY it refuses a file, so these tests read the
/// container's headers and its JSON directly.
/// </summary>
public class GlbWriterTests
{
    static readonly IServiceProvider Services = new ServiceCollection().AddDubleCore().BuildServiceProvider();
    static readonly IMeshPreviewBuilder Preview = Services.GetRequiredService<IMeshPreviewBuilder>();

    readonly ITestOutputHelper output;

    public GlbWriterTests(ITestOutputHelper output) => this.output = output;

    static MeshGeometry Square() => new()
    {
        Name = "square", Texture = "diff", Transparent = false,
        Positions = new float[] { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0 },
        Normals = new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 },
        Uv = new float[] { 0, 1, 1, 1, 1, 0, 0, 0 },
        Indices = new uint[] { 0, 1, 2, 0, 2, 3 },
    };

    static byte[] TinyPng() => PngWriter.Rgba(
        new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 128 }, 2, 2);

    /// <summary>The JSON chunk of a .glb, which is where everything a viewer reads actually lives.</summary>
    static JsonElement Json(byte[] glb)
    {
        uint jsonLength = BitConverter.ToUInt32(glb, 12);
        return JsonDocument.Parse(Encoding.UTF8.GetString(glb, 20, (int)jsonLength)).RootElement;
    }

    [Fact]
    public void The_container_headers_and_the_accessors_agree_with_the_data()
    {
        var glb = GlbWriter.Write(new[] { Square() }, new Dictionary<string, byte[]> { ["diff"] = TinyPng() });

        Assert.Equal(0x46546C67u, BitConverter.ToUInt32(glb, 0));   // "glTF"
        Assert.Equal(2u, BitConverter.ToUInt32(glb, 4));
        Assert.Equal((uint)glb.Length, BitConverter.ToUInt32(glb, 8));

        uint jsonLength = BitConverter.ToUInt32(glb, 12);
        Assert.Equal(0x4E4F534Au, BitConverter.ToUInt32(glb, 16));   // "JSON"

        int binaryAt = 20 + (int)jsonLength;
        uint binaryLength = BitConverter.ToUInt32(glb, binaryAt);
        Assert.Equal(0x004E4942u, BitConverter.ToUInt32(glb, binaryAt + 4));   // "BIN"
        Assert.Equal(glb.Length, binaryAt + 8 + (int)binaryLength);
        Assert.Equal(0u, binaryLength % 4);   // chunks are four-byte aligned

        var json = Json(glb);
        Assert.Equal((int)binaryLength, json.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32());

        var primitive = json.GetProperty("meshes")[0].GetProperty("primitives")[0];
        var accessors = json.GetProperty("accessors");

        int positions = primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
        Assert.Equal(4, accessors[positions].GetProperty("count").GetInt32());
        Assert.Equal("VEC3", accessors[positions].GetProperty("type").GetString());
        Assert.Equal(new[] { 0f, 0f, 0f },
            accessors[positions].GetProperty("min").EnumerateArray().Select(x => x.GetSingle()).ToArray());
        Assert.Equal(new[] { 1f, 1f, 0f },
            accessors[positions].GetProperty("max").EnumerateArray().Select(x => x.GetSingle()).ToArray());

        int indices = primitive.GetProperty("indices").GetInt32();
        Assert.Equal(6, accessors[indices].GetProperty("count").GetInt32());
        Assert.Equal(5125, accessors[indices].GetProperty("componentType").GetInt32());   // UNSIGNED_INT

        Assert.True(primitive.TryGetProperty("material", out _));
        Assert.Equal("image/png", json.GetProperty("images")[0].GetProperty("mimeType").GetString());

        var material = json.GetProperty("materials")[0];
        Assert.Equal("OPAQUE", material.GetProperty("alphaMode").GetString());
        Assert.True(material.GetProperty("doubleSided").GetBoolean());   // clothing is modelled as single-sided sheets

        Assert.Equal(1, json.GetProperty("nodes").GetArrayLength());
        Assert.Equal(1, json.GetProperty("scenes")[0].GetProperty("nodes").GetArrayLength());
    }

    [Fact]
    public void A_geometry_without_a_texture_and_a_transparent_one_get_the_materials_they_need()
    {
        var untextured = Square();
        untextured.Texture = null;
        var transparent = Square();
        transparent.Transparent = true;

        var json = Json(GlbWriter.Write(new[] { untextured, transparent },
                                        new Dictionary<string, byte[]> { ["diff"] = TinyPng() }));

        var materials = json.GetProperty("materials");
        Assert.Equal(2, materials.GetArrayLength());
        Assert.False(materials[0].GetProperty("pbrMetallicRoughness").TryGetProperty("baseColorTexture", out _));
        Assert.Equal("MASK", materials[1].GetProperty("alphaMode").GetString());
        Assert.Equal(2, json.GetProperty("meshes")[0].GetProperty("primitives").GetArrayLength());
    }

    [Fact]
    public void The_embedded_textures_are_rgba_png()
    {
        var png = TinyPng();
        Assert.Equal(0x89, png[0]);
        Assert.Equal(6, png[25]);   // the colour-type byte of IHDR: 6 = RGBA
    }

    [Fact, Trait("Speed", "Slow")]
    public void A_real_model_produces_a_preview_a_viewer_would_accept()
    {
        if (!TestPaths.HasGame) { output.WriteLine("SKIPPED: no studio_body\\dlc.rpf"); return; }

        var garments = Services.GetRequiredService<IGarmentIndexer>()
            .Index(TestPaths.Dlc("studio_body"), "studio_body", new IndexOptions()).Value.Garments;
        var uppr = garments.First(g => g.Slot == "uppr" && g.Number == 15);

        var glb = Preview.Build(uppr, null, output.WriteLine).Value;
        Assert.True(glb.Length > 10000);

        var json = Json(glb);
        Assert.True(json.GetProperty("meshes")[0].GetProperty("primitives").GetArrayLength() >= 1);
        Assert.True(json.GetProperty("images").GetArrayLength() >= 1);

        var path = Path.Combine(Path.GetTempPath(), "duble-tests", "uppr_015.glb");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, glb);
        output.WriteLine("GLB: " + path);
    }
}
