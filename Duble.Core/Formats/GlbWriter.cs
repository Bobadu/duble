#nullable enable
// Writing a mesh (the highest LOD) with its textures as glTF-Binary 2.0, for the 3D preview in the app
// (three.js).
//
// Axes: GTA has Z up, glTF has Y up, so (x, y, z) becomes (x, z, -y). UVs are left alone — their origin is at
// the top in both. One mesh with several primitives, one per geometry, and a material per primitive:
// baseColorTexture from a PNG, metallic 0, roughness 0.9, double sided; transparent shaders get alphaMode MASK
// with a 0.5 cutoff.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CodeWalker.GameFiles;

namespace Duble.Core.Formats;

/// <summary>One geometry of a drawable, already converted to glTF's axes.</summary>
public sealed class MeshGeometry
{
    public string? Name { get; set; }

    /// <summary>Three floats per vertex.</summary>
    public float[]? Positions { get; set; }

    /// <summary>Three floats per vertex, or null when the buffer has no normals.</summary>
    public float[]? Normals { get; set; }

    /// <summary>Two floats per vertex, or null when the buffer has no UVs.</summary>
    public float[]? Uv { get; set; }

    /// <summary>Triangle indices.</summary>
    public uint[]? Indices { get; set; }

    /// <summary>Key of the base colour PNG in the dictionary passed to Write, or null.</summary>
    public string? Texture { get; set; }

    /// <summary>Key of the normal map PNG, or null.</summary>
    public string? NormalMap { get; set; }

    /// <summary>The shader says this geometry is cut out or otherwise transparent.</summary>
    public bool Transparent { get; set; }
}

/// <summary>Turns geometries and PNGs into a single .glb file.</summary>
public static class GlbWriter
{
    public static byte[] Write(IReadOnlyList<MeshGeometry> geometries, IReadOnlyDictionary<string, byte[]>? pngs)
    {
        var binary = new MemoryStream();
        var bufferViews = new List<object>();
        var accessors = new List<object>();
        var images = new List<object>();
        var textures = new List<object>();
        var materials = new List<object>();
        var primitives = new List<object>();
        var imageIndexByKey = new Dictionary<string, int>();

        int AddBufferView(byte[] data, int? target)
        {
            while (binary.Length % 4 != 0) binary.WriteByte(0);
            var view = new Dictionary<string, object>
            {
                ["buffer"] = 0, ["byteOffset"] = (int)binary.Length, ["byteLength"] = data.Length,
            };
            if (target != null) view["target"] = target.Value;
            binary.Write(data, 0, data.Length);
            bufferViews.Add(view);
            return bufferViews.Count - 1;
        }

        int AddFloats(float[] data, int components, bool withMinMax)
        {
            var bytes = new byte[data.Length * 4];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            int view = AddBufferView(bytes, 34962);

            var accessor = new Dictionary<string, object>
            {
                ["bufferView"] = view, ["componentType"] = 5126, ["count"] = data.Length / components,
                ["type"] = components == 3 ? "VEC3" : "VEC2",
            };
            if (withMinMax)
            {
                var min = Enumerable.Repeat(float.MaxValue, components).ToArray();
                var max = Enumerable.Repeat(float.MinValue, components).ToArray();
                for (int i = 0; i < data.Length; i++)
                {
                    int component = i % components;
                    min[component] = Math.Min(min[component], data[i]);
                    max[component] = Math.Max(max[component], data[i]);
                }
                accessor["min"] = min;
                accessor["max"] = max;
            }

            accessors.Add(accessor);
            return accessors.Count - 1;
        }

        int AddIndices(uint[] indices)
        {
            var bytes = new byte[indices.Length * 4];
            Buffer.BlockCopy(indices, 0, bytes, 0, bytes.Length);
            int view = AddBufferView(bytes, 34963);
            accessors.Add(new Dictionary<string, object>
            {
                ["bufferView"] = view, ["componentType"] = 5125, ["count"] = indices.Length, ["type"] = "SCALAR",
            });
            return accessors.Count - 1;
        }

        int? AddImage(string? key)
        {
            if (key == null || pngs == null || !pngs.TryGetValue(key, out var png) || png == null) return null;
            if (imageIndexByKey.TryGetValue(key, out int already)) return already;

            int view = AddBufferView(png, null);
            images.Add(new Dictionary<string, object> { ["bufferView"] = view, ["mimeType"] = "image/png", ["name"] = key });
            textures.Add(new Dictionary<string, object> { ["source"] = images.Count - 1, ["sampler"] = 0 });
            imageIndexByKey[key] = textures.Count - 1;
            return textures.Count - 1;
        }

        foreach (var geometry in geometries)
        {
            if (geometry?.Positions == null || geometry.Indices == null || geometry.Positions.Length < 9) continue;

            var attributes = new Dictionary<string, object> { ["POSITION"] = AddFloats(geometry.Positions, 3, true) };
            if (geometry.Normals != null && geometry.Normals.Length == geometry.Positions.Length)
                attributes["NORMAL"] = AddFloats(geometry.Normals, 3, false);
            if (geometry.Uv != null && geometry.Uv.Length / 2 == geometry.Positions.Length / 3)
                attributes["TEXCOORD_0"] = AddFloats(geometry.Uv, 2, false);

            var pbr = new Dictionary<string, object>
            {
                ["metallicFactor"] = 0.0, ["roughnessFactor"] = 0.9, ["baseColorFactor"] = new[] { 1.0, 1.0, 1.0, 1.0 },
            };
            var baseColour = AddImage(geometry.Texture);
            if (baseColour != null) pbr["baseColorTexture"] = new Dictionary<string, object> { ["index"] = baseColour.Value };
            else pbr["baseColorFactor"] = new[] { 0.72, 0.72, 0.74, 1.0 };   // a neutral grey when there is no texture

            var material = new Dictionary<string, object>
            {
                ["name"] = geometry.Name ?? "geo",
                ["pbrMetallicRoughness"] = pbr,
                ["doubleSided"] = true,
                ["alphaMode"] = geometry.Transparent ? "MASK" : "OPAQUE",
            };
            if (geometry.Transparent) material["alphaCutoff"] = 0.5;

            var normalMap = AddImage(geometry.NormalMap);
            if (normalMap != null) material["normalTexture"] = new Dictionary<string, object> { ["index"] = normalMap.Value };

            materials.Add(material);
            primitives.Add(new Dictionary<string, object>
            {
                ["attributes"] = attributes,
                ["indices"] = AddIndices(geometry.Indices),
                ["material"] = materials.Count - 1,
                ["mode"] = 4,
            });
        }

        while (binary.Length % 4 != 0) binary.WriteByte(0);

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
            ["buffers"] = new[] { new Dictionary<string, object> { ["byteLength"] = (int)binary.Length } },
            ["samplers"] = new[]
            {
                new Dictionary<string, object> { ["magFilter"] = 9729, ["minFilter"] = 9987, ["wrapS"] = 10497, ["wrapT"] = 10497 },
            },
        };
        if (images.Count > 0)
        {
            root["images"] = images;
            root["textures"] = textures;
        }

        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(root));
        int jsonPadding = (4 - json.Length % 4) % 4;
        var binaryBytes = binary.ToArray();

        using var output = new MemoryStream();
        var writer = new BinaryWriter(output);
        writer.Write(0x46546C67u);                                                        // "glTF"
        writer.Write(2u);                                                                 // version
        writer.Write((uint)(12 + 8 + json.Length + jsonPadding + 8 + binaryBytes.Length)); // total length
        writer.Write((uint)(json.Length + jsonPadding));
        writer.Write(0x4E4F534Au);                                                        // "JSON"
        writer.Write(json);
        for (int i = 0; i < jsonPadding; i++) writer.Write((byte)0x20);
        writer.Write((uint)binaryBytes.Length);
        writer.Write(0x004E4942u);                                                        // "BIN"
        writer.Write(binaryBytes);
        return output.ToArray();
    }

    /// <summary>The highest LOD's geometries out of a drawable, with positions and normals already in glTF axes.</summary>
    public static List<MeshGeometry> FromDrawable(Drawable? drawable)
    {
        var result = new List<MeshGeometry>();
        if (drawable?.DrawableModels?.High == null) return result;

        var embedded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dictionary = drawable.ShaderGroup?.TextureDictionary?.Textures?.data_items;
        if (dictionary != null)
            foreach (var texture in dictionary)
                if (texture?.Name != null) embedded.Add(texture.Name);

        var shaders = drawable.ShaderGroup?.Shaders?.data_items;
        int index = 0;

        foreach (var model in drawable.DrawableModels.High)
        {
            foreach (var geometry in model?.Geometries ?? Array.Empty<DrawableGeometry>())
            {
                var vertexData = geometry?.VertexBuffer?.Data1 ?? geometry?.VertexBuffer?.Data2;
                if (vertexData?.VertexBytes == null || vertexData.Info == null || geometry!.IndexBuffer?.Indices == null) continue;

                var info = vertexData.Info;
                int stride = (int)info.Stride;
                int vertexCount = (int)vertexData.VertexCount;
                var bytes = vertexData.VertexBytes;

                bool hasNormals = ((info.Flags >> 3) & 1) == 1;
                bool hasUv = ((info.Flags >> 6) & 1) == 1;
                int positionOffset = info.GetComponentOffset(0);
                int normalOffset = info.GetComponentOffset(3);
                int uvOffset = info.GetComponentOffset(6);
                var uvType = info.GetComponentType(6);

                var mesh = new MeshGeometry
                {
                    Name = "geo_" + index,
                    Positions = new float[vertexCount * 3],
                    Normals = hasNormals ? new float[vertexCount * 3] : null,
                    Uv = hasUv ? new float[vertexCount * 2] : null,
                };

                for (int v = 0; v < vertexCount; v++)
                {
                    int offset = v * stride;

                    float x = BitConverter.ToSingle(bytes, offset + positionOffset);
                    float y = BitConverter.ToSingle(bytes, offset + positionOffset + 4);
                    float z = BitConverter.ToSingle(bytes, offset + positionOffset + 8);
                    mesh.Positions[v * 3] = x;
                    mesh.Positions[v * 3 + 1] = z;
                    mesh.Positions[v * 3 + 2] = -y;

                    if (hasNormals)
                    {
                        float nx = BitConverter.ToSingle(bytes, offset + normalOffset);
                        float ny = BitConverter.ToSingle(bytes, offset + normalOffset + 4);
                        float nz = BitConverter.ToSingle(bytes, offset + normalOffset + 8);
                        mesh.Normals![v * 3] = nx;
                        mesh.Normals[v * 3 + 1] = nz;
                        mesh.Normals[v * 3 + 2] = -ny;
                    }

                    if (hasUv)
                    {
                        float u, w;
                        if (uvType == VertexComponentType.Half2)
                        {
                            u = (float)BitConverter.ToHalf(bytes, offset + uvOffset);
                            w = (float)BitConverter.ToHalf(bytes, offset + uvOffset + 2);
                        }
                        else
                        {
                            u = BitConverter.ToSingle(bytes, offset + uvOffset);
                            w = BitConverter.ToSingle(bytes, offset + uvOffset + 4);
                        }
                        mesh.Uv![v * 2] = u;
                        mesh.Uv[v * 2 + 1] = w;
                    }
                }

                var indices = geometry.IndexBuffer.Indices;
                mesh.Indices = new uint[indices.Length - indices.Length % 3];
                for (int i = 0; i < mesh.Indices.Length; i++) mesh.Indices[i] = indices[i];

                // the shader gives transparency and the diffuse and bump textures
                mesh.Texture = "diff";
                if (shaders != null && geometry.ShaderID < shaders.Length && shaders[geometry.ShaderID] != null)
                {
                    var shader = shaders[geometry.ShaderID];
                    var shaderName = (shader.Name.ToString() + " " + shader.FileName.ToString()).ToLowerInvariant();
                    mesh.Transparent = shaderName.Contains("alpha") || shaderName.Contains("cutout")
                                       || shaderName.Contains("hair") || shaderName.Contains("decal");

                    var parameters = shader.ParametersList?.Parameters;
                    var hashes = shader.ParametersList?.Hashes;
                    if (parameters != null && hashes != null)
                        for (int p = 0; p < parameters.Length && p < hashes.Length; p++)
                        {
                            if (parameters[p].DataType != 0 || parameters[p].Data is not TextureBase texture
                                || string.IsNullOrEmpty(texture.Name)) continue;

                            uint hash = (uint)hashes[p];
                            if (hash == (uint)ShaderParamNames.DiffuseSampler && embedded.Contains(texture.Name))
                                mesh.Texture = "emb:" + texture.Name;
                            if (hash == (uint)ShaderParamNames.BumpSampler && embedded.Contains(texture.Name))
                                mesh.NormalMap = "emb:" + texture.Name;
                        }
                }

                result.Add(mesh);
                index++;
            }
        }

        return result;
    }
}
