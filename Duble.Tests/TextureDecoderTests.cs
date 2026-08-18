using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

public class TextureDecoderTests
{
    static readonly IServiceProvider CoreUslugi = new ServiceCollection().AddDubleCore().BuildServiceProvider();
    static IReadOnlyList<Garment> Indeksuj(string zrodlo, string nazwa, IndexOptions opcje = null)
        => CoreUslugi.GetRequiredService<IGarmentIndexer>().Index(zrodlo, nazwa, opcje ?? new IndexOptions()).Value.Garments;

    readonly ITestOutputHelper wyj;
    public TextureDecoderTests(ITestOutputHelper wyj) { this.wyj = wyj; }

    /// <summary>Szuka w paczce Legacy pierwszego .ytd w BC7 (jest ich ~5 %) i dekoduje.</summary>
    [Fact, Trait("Kategoria", "Wolny")]
    public void Bc7_dekoduje_sie_do_pikseli()
    {
        if (!Sciezki.SaLegacy4) { wyj.WriteLine("POMINIETY: brak downloads"); return; }
        CodeWalkerRuntime.Initialize();   // no longer done by a module initializer
        var folder = Sciezki.Downloads("vrp_clothes_f_civil01");
        Texture bc7 = null; string plik = null;
        foreach (var f in Directory.EnumerateFiles(folder, "*.ytd", SearchOption.AllDirectories))
        {
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, File.ReadAllBytes(f), 13);
            var t = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
            if (t != null && TextureFingerprinter.FormatName(t) == "BC7") { bc7 = t; plik = f; break; }
        }
        if (bc7 == null) { wyj.WriteLine("POMINIETY: nie znalazlam BC7 w civil01"); return; }
        wyj.WriteLine("BC7: " + plik);
        var px = TextureDecoder.Pixels(bc7, 0, out int w, out int h);
        Assert.NotNull(px);
        Assert.Equal(bc7.Width, w); Assert.Equal(bc7.Height, h);
        Assert.Equal(w * h * 4, px.Length);
        Assert.Contains(px, b => b != 0);   // nie sama czern
        // mip 2 tez (uklad mipow w FullData: kolejno od najwiekszego)
        var px2 = TextureDecoder.Pixels(bc7, 2, out int w2, out int h2);
        Assert.NotNull(px2); Assert.Equal(w >> 2, w2); Assert.Equal(w2 * h2 * 4, px2.Length);
        // podglad do recznego obejrzenia (Read tool): kolory maja wygladac jak ubranie, nie szum
        var rgb = new byte[w * h * 3];
        for (int i = 0, j = 0; i < px.Length; i += 4, j += 3) { rgb[j] = px[i + 2]; rgb[j + 1] = px[i + 1]; rgb[j + 2] = px[i]; }
        var png = Path.Combine(Path.GetTempPath(), "duble-tests", "bc7-podglad.png");
        Directory.CreateDirectory(Path.GetDirectoryName(png));
        File.WriteAllBytes(png, PngWriter.Rgb(rgb, w, h));
        wyj.WriteLine("PNG: " + png);
    }

    [Fact]
    public void PngRgba_ma_bok_najwyzej_256_i_typ_koloru_6()
    {
        if (!Sciezki.SaLegacy4) { wyj.WriteLine("POMINIETY"); return; }
        var f = Directory.EnumerateFiles(Sciezki.Downloads("vrp_clothes_f_civil03"), "*.ytd", SearchOption.AllDirectories).First();
        var ytd = new YtdFile(); RpfFile.LoadResourceFile(ytd, File.ReadAllBytes(f), 13);
        var t = ytd.TextureDict.Textures.data_items.First();
        var png = TextureDecoder.PngRgba(t, 256);
        Assert.NotNull(png); Assert.Equal(6, png[25]);
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        Assert.True(w <= 256 && w > 0, "bok " + w);
    }

    /// <summary>To samo dla gen9 (uklad FullData w Enhanced) — pierwsza tekstura BC7 z naszego studio_wardrobe.</summary>
    [Fact, Trait("Kategoria", "Wolny")]
    public void Bc7_gen9_dekoduje_sie_do_pikseli()
    {
        var dlc = Sciezki.Dlc("studio_wardrobe");
        if (dlc == null || !File.Exists(dlc)) { wyj.WriteLine("POMINIETY: brak studio_wardrobe"); return; }
        var poz = Indeksuj(dlc, "studio_wardrobe");
        var tex = poz.SelectMany(p => p.Textures).FirstOrDefault(t => t.Format == "BC7");
        if (tex == null) { wyj.WriteLine("POMINIETY: brak BC7 w studio_wardrobe"); return; }
        Assert.True(tex.IsDecoded, "po Zadaniu 7 tekstury BC7 maja odcisk");
        var ytd = new YtdFile();
        RpfFile.LoadResourceFile(ytd, CoreUslugi.GetRequiredService<IArchiveCache>().Read(tex.Path).Value, 13);
        var t0 = ytd.TextureDict.Textures.data_items.First();
        var px = TextureDecoder.Pixels(t0, 0, out int w, out int h);
        Assert.NotNull(px); Assert.Equal(w * h * 4, px.Length);
        var rgb = new byte[w * h * 3];
        for (int i = 0, j = 0; i < px.Length; i += 4, j += 3) { rgb[j] = px[i + 2]; rgb[j + 1] = px[i + 1]; rgb[j + 2] = px[i]; }
        var png = Path.Combine(Path.GetTempPath(), "duble-tests", "bc7-gen9-podglad.png");
        Directory.CreateDirectory(Path.GetDirectoryName(png));
        File.WriteAllBytes(png, PngWriter.Rgb(rgb, w, h));
        wyj.WriteLine("PNG: " + png + " (" + tex.FileName + ")");
    }
}
