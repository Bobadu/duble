using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using Duble.Cli.CommandLine;
using Duble.Cli.Commands;

namespace Duble.Cli.Tools;

/// <summary>
/// `duble obj`: the highest LOD of a .ydd as a Wavefront OBJ, with normals and UVs, for looking at geometry in
/// Blender — comparing a vanilla body against a replacement, say.
///
/// It also prints what it found on the way: the LODs, the bounding box, and each geometry's shader with the
/// textures it asks for. That last one is how you find a pink square in the game: a shader naming a texture
/// that is not in the .ytd beside it.
/// </summary>
public static class ObjExportCommand
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static CliCommand Command { get; } = new(
        "obj",
        "Export a model's geometry to OBJ, and describe what is in it",
        "<file.ydd>",
        new[] { CatalogOptions.Out },
        Run);

    static int Run(CommandContext context)
    {
        if (context.Arguments.Positional.Count < 1) return context.Misuse("give a .ydd file");
        var source = context.Arguments.Positional[0];

        var bytes = File.ReadAllBytes(source);
        var read = GameFiles.ReadModel(bytes);
        if (read.IsFailure)
        {
            context.Output.Error($"{source}: {read.Error.Message}");
            return ExitCode.Failed;
        }

        var format = Rsc7Header.IsEnhanced(bytes, ".ydd") is bool enhanced
            ? GameFormats.FromHeader(enhanced).ToLabel()
            : "unknown";

        var drawable = read.Value.Drawables.First();
        Describe(context.Output, drawable, format);

        var path = context.Arguments.Value(CatalogOptions.Out.Name) ?? Path.ChangeExtension(source, ".obj");
        using var writer = new StreamWriter(path);
        writer.WriteLine($"# duble obj — {Path.GetFileName(source)} ({format})");
        var (geometries, vertices, triangles) = Write(writer, drawable, context.Output);

        context.Output.Line($"OBJ: {path} ({geometries} geometries, {vertices} vertices, {triangles} triangles)");
        return ExitCode.Ok;
    }

    static void Describe(Output output, Drawable drawable, string format)
    {
        output.Line($"format: {format}");

        var models = drawable.DrawableModels;
        foreach (var (name, lod) in new[]
                 {
                     ("high", models?.High), ("med", models?.Med), ("low", models?.Low), ("vlow", models?.VLow),
                 })
        {
            if (lod == null) continue;
            int vertices = 0, triangles = 0;
            foreach (var geometry in lod.SelectMany(model => model?.Geometries ?? Array.Empty<DrawableGeometry>()))
            {
                vertices += (int)(geometry?.VertexBuffer?.VertexCount ?? 0);
                triangles += (int)((geometry?.IndicesCount ?? 0) / 3);
            }
            output.Detail($"LOD {name,-4} models={lod.Length} vertices={vertices} triangles={triangles}");
        }

        var min = drawable.BoundingBoxMin;
        var max = drawable.BoundingBoxMax;
        output.Detail($"bbox min=({min.X:F3},{min.Y:F3},{min.Z:F3}) max=({max.X:F3},{max.Y:F3},{max.Z:F3}) "
            + $"bones={drawable.Skeleton?.Bones?.Items?.Length ?? 0}");
    }

    /// <summary>
    /// Writes the highest LOD. OBJ indices are 1-based and count from the start of the FILE, not of the group,
    /// so each geometry's indices are offset by everything written before it.
    /// </summary>
    static (int Geometries, int Vertices, int Triangles) Write(TextWriter writer, Drawable drawable, Output output)
    {
        int firstVertex = 1, firstNormal = 1, firstUv = 1;
        int geometries = 0, vertices = 0, triangles = 0;

        foreach (var model in drawable.DrawableModels?.High ?? Array.Empty<DrawableModel>())
            foreach (var geometry in model?.Geometries ?? Array.Empty<DrawableGeometry>())
            {
                var buffer = geometry?.VertexBuffer?.Data1 ?? geometry?.VertexBuffer?.Data2;
                if (buffer?.VertexBytes == null || buffer.Info == null) continue;

                var info = buffer.Info;
                int stride = (int)info.Stride;
                int count = (int)buffer.VertexCount;
                var data = buffer.VertexBytes;

                bool hasNormals = ((info.Flags >> 3) & 1) == 1;
                bool hasUv = ((info.Flags >> 6) & 1) == 1;
                int positionAt = info.GetComponentOffset(0);
                int normalAt = info.GetComponentOffset(3);
                int uvAt = info.GetComponentOffset(6);
                var uvType = info.GetComponentType(6);

                writer.WriteLine($"g geo_{geometries} shader_{geometry!.ShaderID}");
                output.Detail($"geo {geometries}: vertices={count} stride={stride} normals={hasNormals} uv={hasUv} ({uvType})");
                DescribeShader(output, drawable, geometry);

                for (int v = 0; v < count; v++)
                {
                    int at = v * stride + positionAt;
                    writer.WriteLine(string.Format(Invariant, "v {0:R} {1:R} {2:R}",
                        BitConverter.ToSingle(data, at), BitConverter.ToSingle(data, at + 4), BitConverter.ToSingle(data, at + 8)));
                }

                if (hasNormals)
                    for (int v = 0; v < count; v++)
                    {
                        int at = v * stride + normalAt;
                        writer.WriteLine(string.Format(Invariant, "vn {0:R} {1:R} {2:R}",
                            BitConverter.ToSingle(data, at), BitConverter.ToSingle(data, at + 4), BitConverter.ToSingle(data, at + 8)));
                    }

                if (hasUv)
                    for (int v = 0; v < count; v++)
                    {
                        int at = v * stride + uvAt;
                        float u, w;
                        if (uvType == VertexComponentType.Half2)
                        {
                            u = (float)BitConverter.ToHalf(data, at);
                            w = (float)BitConverter.ToHalf(data, at + 2);
                        }
                        else
                        {
                            u = BitConverter.ToSingle(data, at);
                            w = BitConverter.ToSingle(data, at + 4);
                        }
                        // OBJ counts V from the bottom, the game from the top
                        writer.WriteLine(string.Format(Invariant, "vt {0:R} {1:R}", u, 1 - w));
                    }

                var indices = geometry.IndexBuffer?.Indices;
                if (indices != null)
                {
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        string Corner(int corner)
                        {
                            int index = indices[i + corner];
                            return $"{firstVertex + index}/{(hasUv ? (firstUv + index).ToString() : "")}/{(hasNormals ? (firstNormal + index).ToString() : "")}";
                        }
                        writer.WriteLine($"f {Corner(0)} {Corner(1)} {Corner(2)}");
                    }
                    triangles += indices.Length / 3;
                }

                firstVertex += count;
                if (hasNormals) firstNormal += count;
                if (hasUv) firstUv += count;
                vertices += count;
                geometries++;
            }

        return (geometries, vertices, triangles);
    }

    static void DescribeShader(Output output, Drawable drawable, DrawableGeometry geometry)
    {
        var shaders = drawable.ShaderGroup?.Shaders?.data_items;
        if (shaders == null || geometry.ShaderID >= shaders.Length || shaders[geometry.ShaderID] == null) return;

        var shader = shaders[geometry.ShaderID];
        var parameters = shader.ParametersList?.Parameters;
        var hashes = shader.ParametersList?.Hashes;
        if (parameters == null) return;

        var textures = new List<string>();
        for (int i = 0; i < parameters.Length; i++)
            if (parameters[i].DataType == 0 && parameters[i].Data is TextureBase texture)
                textures.Add($"{(hashes != null && i < hashes.Length ? hashes[i].ToString() : "?")}={texture.Name ?? "(none)"}");

        output.Detail($"   shader {shader.Name} ({shader.FileName}) textures: {string.Join(", ", textures)}");
    }
}
