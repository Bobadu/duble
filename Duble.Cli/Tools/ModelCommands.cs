using System;
using System.IO;
using CodeWalker.GameFiles;
using Duble.Cli.CommandLine;
using Duble.Cli.Commands;

namespace Duble.Cli.Tools;

/// <summary>Commands that work on one .ydd: a 3D preview, or an invisible copy of it.</summary>
public static class ModelCommands
{
    static readonly CliOption Texture = CliOption.Value("--texture", "file.ytd", "Texture to put on the model");

    public static CliCommand Glb { get; } = new(
        "glb",
        "Export a model, with a texture, to glTF-Binary for a 3D viewer",
        "<file.ydd>",
        new[] { Texture, CatalogOptions.Out },
        RunGlb);

    public static CliCommand Hollow { get; } = new(
        "hollow",
        "Write a copy of a model with every vertex collapsed, so it draws nothing",
        "<in.ydd> <out.ydd>",
        Array.Empty<CliOption>(),
        RunHollow);

    static int RunGlb(CommandContext context)
    {
        if (context.Arguments.Positional.Count < 1) return context.Misuse("give a .ydd file");
        var source = context.Arguments.Positional[0];

        var texture = context.Arguments.Value(Texture.Name);
        var preview = context.Service<IMeshPreviewBuilder>().Build(
            File.ReadAllBytes(source),
            texture != null ? File.ReadAllBytes(texture) : null,
            context.Output.Detail);

        if (preview.IsFailure)
        {
            context.Output.Error($"{source}: {preview.Error.Message}");
            return ExitCode.Failed;
        }

        var path = context.Arguments.Value(CatalogOptions.Out.Name) ?? Path.ChangeExtension(source, ".glb");
        File.WriteAllBytes(path, preview.Value);
        context.Output.Line($"GLB: {path} ({preview.Value.Length} B)");
        return ExitCode.Ok;
    }

    /// <summary>
    /// Makes a model INVISIBLE by collapsing every vertex to the origin: the triangles degenerate and draw
    /// nothing.
    ///
    /// What it is for: an empty top for a topless body. R*'s own "empty" items are not empty — accs_014 is a
    /// three-centimetre square inside the chest — and on a torso without a belly that square pokes through as
    /// the pink of a missing texture.
    /// </summary>
    static int RunHollow(CommandContext context)
    {
        if (context.Arguments.Positional.Count < 2) return context.Misuse("give the model to read and the one to write");

        var source = context.Arguments.Positional[0];
        var target = context.Arguments.Positional[1];

        var read = GameFiles.ReadModel(File.ReadAllBytes(source));
        if (read.IsFailure)
        {
            context.Output.Error($"{source}: {read.Error.Message}");
            return ExitCode.Failed;
        }

        int collapsed = 0;
        foreach (var drawable in read.Value.Drawables)
        {
            var models = drawable.DrawableModels;
            foreach (var lod in new[] { models?.High, models?.Med, models?.Low, models?.VLow })
            {
                if (lod == null) continue;
                foreach (var model in lod)
                    foreach (var geometry in model?.Geometries ?? Array.Empty<DrawableGeometry>())
                    {
                        var buffer = geometry?.VertexBuffer?.Data1 ?? geometry?.VertexBuffer?.Data2;
                        if (buffer?.VertexBytes == null || buffer.Info == null) continue;

                        int stride = (int)buffer.Info.Stride;
                        int positionAt = buffer.Info.GetComponentOffset(0);
                        for (int v = 0; v < buffer.VertexCount; v++)
                        {
                            int at = v * stride + positionAt;
                            Array.Clear(buffer.VertexBytes, at, 12);   // three floats: x, y, z
                            collapsed++;
                        }
                    }
            }

            // a box of nothing, but not of zero — the game does not like a degenerate bounding volume
            drawable.BoundingBoxMin = new SharpDX.Vector3(-0.001f, -0.001f, -0.001f);
            drawable.BoundingBoxMax = new SharpDX.Vector3(0.001f, 0.001f, 0.001f);
            drawable.BoundingCenter = SharpDX.Vector3.Zero;
            drawable.BoundingSphereRadius = 0.001f;
        }

        // written as Enhanced: CodeWalkerRuntime put the library in gen9 mode, and Save() follows that flag
        var bytes = read.Value.Save();
        File.WriteAllBytes(target, bytes);

        context.Output.Line($"hollow: collapsed {collapsed} vertices -> {target} ({bytes.Length} B, Enhanced)");
        return ExitCode.Ok;
    }
}
