using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

/// <summary>Indeksowanie na prawdziwych danych (pomijane, gdy brak plikow gry / downloads).</summary>
public class GarmentIndexerTests
{
    static readonly IServiceProvider Uslugi = new ServiceCollection().AddDubleCore().BuildServiceProvider();
    static readonly IGarmentIndexer Indeksator = Uslugi.GetRequiredService<IGarmentIndexer>();
    static readonly IArchiveCache Archiwa = Uslugi.GetRequiredService<IArchiveCache>();

    static IReadOnlyList<Garment> Indeksuj(string zrodlo, string nazwa, IndexOptions opcje = null)
        => Indeksator.Index(zrodlo, nazwa, opcje ?? new IndexOptions()).Value.Garments;

    readonly ITestOutputHelper wyj;
    public GarmentIndexerTests(ITestOutputHelper wyj) { this.wyj = wyj; }

    [Fact]
    public void Archiwum_rpf_jako_zrodlo_daje_geometrie_i_tekstury()
    {
        if (!Sciezki.JestGra) { wyj.WriteLine("POMINIETY: brak studio_body\\dlc.rpf"); return; }
        var poz = Indeksuj(Sciezki.Dlc("studio_body"), "studio_body");
        var uppr = poz.FirstOrDefault(p => p.Slot == "uppr" && p.Number == 15);
        Assert.NotNull(uppr);
        Assert.Equal(6080, uppr.Geometry.Vertices);            // KS Body V1 ramiona: 6072 (cialo) + 8 (znak wodny "Ks"), pomiar 16.08
        Assert.Equal(GameFormat.Enhanced, uppr.GameFormat);
        Assert.NotEmpty(uppr.Textures);
        Assert.All(uppr.Textures, t => Assert.True(t.IsDecoded, t.FileName + " " + t.Format));
        Assert.All(poz, p => Assert.Contains("|", p.ModelPath));   // sciezka "archiwum|wewnatrz"
        // Zrodla.Bytes oddaje bajty z naglowkiem RSC7 (do miniatur/GLB)
        var b = Archiwa.Read(uppr.ModelPath).Value;
        Assert.True(Rsc7Header.IsRsc7(b)); Assert.Equal(159, Rsc7Header.Version(b));
    }

    [Fact]
    public void Plik_rpf_lezacy_w_folderze_jest_kontenerem()
    {
        if (!Sciezki.JestGra) { wyj.WriteLine("POMINIETY: brak studio_body\\dlc.rpf"); return; }
        var tmp = Sciezki.Tymczasowy("rpf-w-folderze");
        try
        {
            File.Copy(Sciezki.Dlc("studio_body"), Path.Combine(tmp, "dlc.rpf"));
            var poz = Indeksuj(tmp, "test");
            Assert.NotEmpty(poz);
            Assert.All(poz, p => Assert.Equal("body.rpf", p.Container));   // kontener = najglebsze archiwum (x64/body.rpf wewnatrz dlc.rpf)
            Assert.All(poz, p => Assert.Equal(GameFormat.Enhanced, p.GameFormat));
            Assert.Contains(poz, p => p.Slot == "uppr" && p.Number == 15 && p.Geometry.Vertices == 6080);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Folder_legacy_ma_gen9_false_i_tyle_pozycji_co_wzorzec()
    {
        if (!Sciezki.SaLegacy4) { wyj.WriteLine("POMINIETY: brak downloads"); return; }
        var poz = Indeksuj(Sciezki.Downloads("vrp_clothes_f_civil03"), "vrp_clothes_f_civil03");
        Assert.Equal(62, poz.Count);
        Assert.All(poz, p => Assert.Equal(GameFormat.Legacy, p.GameFormat));
    }

    [Fact]
    public void A_second_run_reuses_fingerprints_and_writes_the_thumbnails()
    {
        if (!Sciezki.SaLegacy4) { wyj.WriteLine("POMINIETY: brak downloads"); return; }
        var source = Sciezki.Downloads("vrp_clothes_f_civil03");
        var thumbnails = Sciezki.Tymczasowy("thumbnails");
        try
        {
            var indexer = Uslugi.GetRequiredService<IGarmentIndexer>();
            var first = indexer.Index(source, "civil03", new IndexOptions { ThumbnailFolder = thumbnails }).Value;

            Assert.All(first.Garments, g => Assert.False(string.IsNullOrEmpty(g.ChangeStamp)));
            Assert.All(first.Garments.SelectMany(g => g.Textures), t => Assert.False(string.IsNullOrEmpty(t.ChangeStamp)));
            Assert.Equal(0, first.ReusedModels);

            // a thumbnail is per SHA — identical files share one — so compare against the number of distinct ones
            int distinctSha = first.Garments.SelectMany(g => g.Textures)
                .Where(t => t.IsDecoded).Select(t => t.Sha256).Distinct().Count();
            Assert.InRange(Directory.GetFiles(thumbnails, "*.png").Length, distinctSha * 9 / 10, distinctSha);

            var catalog = new Catalog();
            catalog.Upsert(first.Garments);
            var progress = new List<ProgressReport>();
            var second = indexer.Index(source, "civil03",
                new IndexOptions { ThumbnailFolder = thumbnails, PreviousCatalog = catalog },
                new Progress<ProgressReport>(progress.Add)).Value;

            Assert.Equal(first.Garments.Count, second.Garments.Count);
            Assert.Equal(first.Garments.Count, second.ReusedModels);   // nothing changed, so nothing was read again
            Assert.True(second.ReusedTextures > 0);

            // the fingerprints are identical: reusing them changes no result
            var before = first.Garments.OrderBy(g => g.Id)
                .Select(g => g.Geometry!.PositionHash + string.Join(",", g.Textures.Select(t => t.Sha256))).ToList();
            var after = second.Garments.OrderBy(g => g.Id)
                .Select(g => g.Geometry!.PositionHash + string.Join(",", g.Textures.Select(t => t.Sha256))).ToList();
            Assert.Equal(before, after);
        }
        finally { Directory.Delete(thumbnails, true); }
    }

    [Fact]
    public void Kosz_odrzucone_jest_pomijany_przy_indeksowaniu()
    {
        // WKoszu (czysta funkcja) + Zrodlo na folderze z plikami-atrapami: pusty ydd poza koszem trafia do prob wczytania (log go widzi),
        // ten sam plik w _odrzucone jest niewidoczny
        var tmp = Sciezki.Tymczasowy("kosz");
        try
        {
            Assert.True(BinFolder.Contains(tmp, Path.Combine(tmp, "_odrzucone", "p", "k.rpf", "jbib_001_u.ydd")));
            Assert.True(BinFolder.Contains(tmp, Path.Combine(tmp, "p", "_ODRZUCONE", "jbib_001_u.ydd")));
            Assert.False(BinFolder.Contains(tmp, Path.Combine(tmp, "p", "k.rpf", "jbib_001_u.ydd")));
            Assert.False(BinFolder.Contains(tmp, Path.Combine(tmp, "p", "_odrzucone.ydd")));   // nazwa pliku sie nie liczy
            Directory.CreateDirectory(Path.Combine(tmp, "_odrzucone", "k.rpf"));
            File.WriteAllBytes(Path.Combine(tmp, "_odrzucone", "k.rpf", "jbib_001_u.ydd"), new byte[16]);
            Assert.Empty(Indeksuj(tmp, "t"));
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Anulowanie_przerywa_indeksowanie()
    {
        if (!Sciezki.SaLegacy4) { wyj.WriteLine("POMINIETY: brak downloads"); return; }
        var cts = new System.Threading.CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<System.OperationCanceledException>(() =>
            Indeksator.Index(Sciezki.Downloads("vrp_clothes_f_civil03"), "civil03", new IndexOptions(), null, cts.Token));
    }
}
