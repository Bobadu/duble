using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using Duble.Cli.CommandLine;
using Duble.Cli.Commands;

namespace Duble.Cli.Tools;

/// <summary>Commands that work on one .ytd: a thumbnail, the full-size textures, or building one from DDS.</summary>
public static class TextureCommands
{
    const int ThumbnailSide = 256;

    public static CliCommand Preview { get; } = new(
        "preview",
        "Write one texture's thumbnail to a PNG, and print what the fingerprint saw",
        "<file.ytd>",
        new[] { CatalogOptions.Out },
        RunPreview);

    public static CliCommand Export { get; } = new(
        "textures",
        "Write every texture in a .ytd to a PNG at full size",
        "<file.ytd>",
        new[] { CatalogOptions.Out },
        RunExport);

    public static CliCommand Build { get; } = new(
        "ytd",
        "Build a .ytd from DDS files; each texture takes its file name",
        "<out.ytd> <file.dds>...",
        Array.Empty<CliOption>(),
        RunBuild);

    /// <summary>
    /// The channel order is what this is really for. The decoder hands back BGRA; get it wrong and skin comes
    /// out blue — which no number in the fingerprint would show you.
    /// </summary>
    static int RunPreview(CommandContext context)
    {
        if (context.Arguments.Positional.Count < 1) return context.Misuse("give a .ytd file");
        var source = context.Arguments.Positional[0];

        byte[]? thumbnail = null;
        var fingerprint = context.Service<ITextureFingerprinter>().Compute(File.ReadAllBytes(source),
            (pixels, width, height) => thumbnail = Thumbnail.FromPixels(pixels, width, height, ThumbnailSide));

        if (fingerprint.IsFailure)
        {
            context.Output.Error(fingerprint.Error.ToString());
            return ExitCode.Failed;
        }

        var texture = fingerprint.Value;
        context.Output.Line($"{texture.Name}  {texture.Width}x{texture.Height} {texture.Format} "
            + $"mips={texture.MipLevels} alpha={texture.AlphaShare:P1}");

        if (thumbnail == null)
        {
            context.Output.Error($"the pixels would not decode ({texture.Format})");
            return ExitCode.Failed;
        }

        var path = context.Arguments.Value(CatalogOptions.Out.Name) ?? Path.ChangeExtension(source, ".png");
        File.WriteAllBytes(path, PngWriter.Rgb(thumbnail, ThumbnailSide, ThumbnailSide));
        context.Output.Line($"PNG: {path}");
        return ExitCode.Ok;
    }

    static int RunExport(CommandContext context)
    {
        if (context.Arguments.Positional.Count < 1) return context.Misuse("give a .ytd file");
        var source = context.Arguments.Positional[0];

        var read = GameFiles.ReadTextures(File.ReadAllBytes(source));
        if (read.IsFailure)
        {
            context.Output.Error($"{source}: {read.Error.Message}");
            return ExitCode.Failed;
        }

        var folder = context.Arguments.Value(CatalogOptions.Out.Name)
            ?? Path.GetDirectoryName(Path.GetFullPath(source))!;
        Directory.CreateDirectory(folder);

        int written = 0;
        foreach (var texture in read.Value.TextureDict.Textures.data_items)
        {
            var format = TextureFingerprinter.FormatName(texture);
            var pixels = TextureDecoder.Pixels(texture, 0, out int width, out int height);
            if (pixels == null)
            {
                context.Output.Warning($"{texture.Name}: no decoder for {format}");
                continue;
            }

            var path = Path.Combine(folder, texture.Name + ".png");
            File.WriteAllBytes(path, PngWriter.Rgb(GameFiles.ToRgb(pixels, width, height), width, height));
            context.Output.Line($"{texture.Name}  {width}x{height} {format} -> {path}");
            written++;
        }

        return written > 0 ? ExitCode.Ok : ExitCode.Failed;
    }

    static int RunBuild(CommandContext context)
    {
        if (context.Arguments.Positional.Count < 2) return context.Misuse("give the .ytd to write and at least one .dds");

        var target = context.Arguments.Positional[0];
        var textures = new List<Texture>();

        foreach (var file in context.Arguments.Positional.Skip(1))
        {
            var texture = DDSIO.GetTexture(File.ReadAllBytes(file));
            if (texture == null)
            {
                context.Output.Error($"{file}: not a DDS this can read");
                return ExitCode.Failed;
            }

            texture.Name = Path.GetFileNameWithoutExtension(file);
            texture.NameHash = JenkHash.GenHash(texture.Name.ToLowerInvariant());
            textures.Add(texture);
            context.Output.Line($"{texture.Name}  {texture.Width}x{texture.Height} "
                + $"{TextureFingerprinter.FormatName(texture)} mips={texture.Levels}");
        }

        var dictionary = new TextureDictionary();
        dictionary.BuildFromTextureList(textures);

        // written as Enhanced: CodeWalkerRuntime put the library in gen9 mode, and Save() follows that flag
        var bytes = new YtdFile { TextureDict = dictionary }.Save();
        File.WriteAllBytes(target, bytes);

        context.Output.Line($"YTD: {target} ({bytes.Length} B, Enhanced, {textures.Count} textures)");
        return ExitCode.Ok;
    }
}
