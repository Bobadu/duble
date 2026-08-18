using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

/// <summary>
/// Decoding texture pixels. CodeWalker returns null for BC7 — about 5% of the textures we measured — so Duble
/// decodes those itself, and these tests are what say it gets real pixels rather than noise.
/// </summary>
public class TextureDecoderTests
{
    static readonly IServiceProvider Services = new ServiceCollection().AddDubleCore().BuildServiceProvider();

    static IReadOnlyList<Garment> Index(string source, string name)
        => Services.GetRequiredService<IGarmentIndexer>().Index(source, name, new IndexOptions()).Value.Garments;

    readonly ITestOutputHelper output;

    public TextureDecoderTests(ITestOutputHelper output) => this.output = output;

    /// <summary>Writes the pixels out as a PNG, so a person can look at them and see clothing rather than noise.</summary>
    void WritePreview(byte[] bgra, int width, int height, string name)
    {
        var rgb = new byte[width * height * 3];
        for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
        {
            rgb[j] = bgra[i + 2];
            rgb[j + 1] = bgra[i + 1];
            rgb[j + 2] = bgra[i];
        }

        var path = Path.Combine(Path.GetTempPath(), "duble-tests", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, PngWriter.Rgb(rgb, width, height));
        output.WriteLine("PNG: " + path);
    }

    [Fact, Trait("Speed", "Slow")]
    public void A_legacy_bc7_texture_decodes_to_pixels()
    {
        if (!TestPaths.HasLegacyPacks) { output.WriteLine("SKIPPED: no downloads"); return; }
        CodeWalkerRuntime.Initialize();

        Texture bc7 = null;
        string file = null;
        foreach (var candidate in Directory.EnumerateFiles(TestPaths.Downloads("vrp_clothes_f_civil01"), "*.ytd", SearchOption.AllDirectories))
        {
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, File.ReadAllBytes(candidate), 13);
            var texture = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
            if (texture != null && TextureFingerprinter.FormatName(texture) == "BC7") { bc7 = texture; file = candidate; break; }
        }
        if (bc7 == null) { output.WriteLine("SKIPPED: no BC7 texture in civil01"); return; }
        output.WriteLine("BC7: " + file);

        var pixels = TextureDecoder.Pixels(bc7, 0, out int width, out int height);
        Assert.NotNull(pixels);
        Assert.Equal(bc7.Width, width);
        Assert.Equal(bc7.Height, height);
        Assert.Equal(width * height * 4, pixels.Length);
        Assert.Contains(pixels, b => b != 0);   // not a solid black image

        // and a lower mip, which means the offsets into FullData are right
        var smaller = TextureDecoder.Pixels(bc7, 2, out int smallWidth, out int smallHeight);
        Assert.NotNull(smaller);
        Assert.Equal(width >> 2, smallWidth);
        Assert.Equal(smallWidth * smallHeight * 4, smaller.Length);

        WritePreview(pixels, width, height, "bc7-legacy.png");
    }

    [Fact, Trait("Speed", "Slow")]
    public void An_enhanced_bc7_texture_decodes_too()
    {
        var dlc = TestPaths.Dlc("studio_wardrobe");
        if (dlc == null || !File.Exists(dlc)) { output.WriteLine("SKIPPED: no studio_wardrobe"); return; }

        var texture = Index(dlc, "studio_wardrobe").SelectMany(g => g.Textures).FirstOrDefault(t => t.Format == "BC7");
        if (texture == null) { output.WriteLine("SKIPPED: no BC7 texture in studio_wardrobe"); return; }
        Assert.True(texture.IsDecoded, "a BC7 texture has a fingerprint like any other");

        var ytd = new YtdFile();
        RpfFile.LoadResourceFile(ytd, Services.GetRequiredService<IArchiveCache>().Read(texture.Path).Value, 13);

        var pixels = TextureDecoder.Pixels(ytd.TextureDict.Textures.data_items.First(), 0, out int width, out int height);
        Assert.NotNull(pixels);
        Assert.Equal(width * height * 4, pixels.Length);

        WritePreview(pixels, width, height, "bc7-enhanced.png");
    }

    [Fact]
    public void A_preview_png_is_rgba_and_no_larger_than_asked_for()
    {
        if (!TestPaths.HasLegacyPacks) { output.WriteLine("SKIPPED: no downloads"); return; }

        var file = Directory.EnumerateFiles(TestPaths.Downloads("vrp_clothes_f_civil03"), "*.ytd", SearchOption.AllDirectories).First();
        var ytd = new YtdFile();
        RpfFile.LoadResourceFile(ytd, File.ReadAllBytes(file), 13);

        var png = TextureDecoder.PngRgba(ytd.TextureDict.Textures.data_items.First(), 256);
        Assert.NotNull(png);
        Assert.Equal(6, png[25]);   // colour type 6 = RGBA

        int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        Assert.True(width <= 256 && width > 0, "side " + width);
    }
}
