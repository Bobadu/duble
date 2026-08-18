using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

/// <summary>
/// Indexing over real packs. The packs are not in the repository, so a test without its data says so and
/// passes — `dotnet test` has to work straight after a clone.
/// </summary>
public class GarmentIndexerTests
{
    static readonly IServiceProvider Services = new ServiceCollection().AddDubleCore().BuildServiceProvider();
    static readonly IGarmentIndexer Indexer = Services.GetRequiredService<IGarmentIndexer>();
    static readonly IArchiveCache Archives = Services.GetRequiredService<IArchiveCache>();

    static IReadOnlyList<Garment> Index(string source, string name, IndexOptions options = null)
        => Indexer.Index(source, name, options ?? new IndexOptions()).Value.Garments;

    readonly ITestOutputHelper output;

    public GarmentIndexerTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void An_rpf_archive_as_a_source_yields_geometry_and_textures()
    {
        if (!TestPaths.HasGame) { output.WriteLine("SKIPPED: no studio_body\\dlc.rpf"); return; }

        var garments = Index(TestPaths.Dlc("studio_body"), "studio_body");
        var uppr = garments.FirstOrDefault(g => g.Slot == "uppr" && g.Number == 15);

        Assert.NotNull(uppr);
        // KS Body V1 arms: 6072 vertices of body plus 8 for the "Ks" watermark
        Assert.Equal(6080, uppr.Geometry.Vertices);
        Assert.Equal(GameFormat.Enhanced, uppr.GameFormat);
        Assert.NotEmpty(uppr.Textures);
        Assert.All(uppr.Textures, texture => Assert.True(texture.IsDecoded, texture.FileName + " " + texture.Format));
        Assert.All(garments, garment => Assert.Contains("|", garment.ModelPath));   // "archive|path inside it"

        // reading it back gives the bytes with their RSC7 header, which is what previews need
        var bytes = Archives.Read(uppr.ModelPath).Value;
        Assert.True(Rsc7Header.IsRsc7(bytes));
        Assert.Equal(159, Rsc7Header.Version(bytes));
    }

    [Fact]
    public void An_rpf_sitting_in_a_folder_is_a_container_of_its_own()
    {
        if (!TestPaths.HasGame) { output.WriteLine("SKIPPED: no studio_body\\dlc.rpf"); return; }

        var temp = TestPaths.Temp("rpf-in-folder");
        try
        {
            File.Copy(TestPaths.Dlc("studio_body"), Path.Combine(temp, "dlc.rpf"));
            var garments = Index(temp, "test");

            Assert.NotEmpty(garments);
            // the container is the DEEPEST archive: x64\body.rpf inside dlc.rpf, not dlc.rpf itself
            Assert.All(garments, garment => Assert.Equal("body.rpf", garment.Container));
            Assert.All(garments, garment => Assert.Equal(GameFormat.Enhanced, garment.GameFormat));
            Assert.Contains(garments, g => g.Slot == "uppr" && g.Number == 15 && g.Geometry.Vertices == 6080);
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void A_legacy_folder_indexes_as_legacy()
    {
        if (!TestPaths.HasLegacyPacks) { output.WriteLine("SKIPPED: no downloads"); return; }

        var garments = Index(TestPaths.Downloads("vrp_clothes_f_civil03"), "vrp_clothes_f_civil03");
        Assert.Equal(62, garments.Count);
        Assert.All(garments, garment => Assert.Equal(GameFormat.Legacy, garment.GameFormat));
    }

    [Fact]
    public void A_second_run_reuses_fingerprints_and_writes_the_thumbnails()
    {
        if (!TestPaths.HasLegacyPacks) { output.WriteLine("SKIPPED: no downloads"); return; }

        var source = TestPaths.Downloads("vrp_clothes_f_civil03");
        var thumbnails = TestPaths.Temp("thumbnails");
        try
        {
            var first = Indexer.Index(source, "civil03", new IndexOptions { ThumbnailFolder = thumbnails }).Value;

            Assert.All(first.Garments, g => Assert.False(string.IsNullOrEmpty(g.ChangeStamp)));
            Assert.All(first.Garments.SelectMany(g => g.Textures), t => Assert.False(string.IsNullOrEmpty(t.ChangeStamp)));
            Assert.Equal(0, first.ReusedModels);

            // a thumbnail is per SHA — identical files share one — so compare against the number of distinct ones
            int distinctSha = first.Garments.SelectMany(g => g.Textures)
                .Where(t => t.IsDecoded).Select(t => t.Sha256).Distinct().Count();
            Assert.InRange(Directory.GetFiles(thumbnails, "*.png").Length, distinctSha * 9 / 10, distinctSha);

            var catalog = new Catalog();
            catalog.Upsert(first.Garments);
            var second = Indexer.Index(source, "civil03",
                new IndexOptions { ThumbnailFolder = thumbnails, PreviousCatalog = catalog }).Value;

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
    public void The_bin_folder_is_invisible_to_indexing()
    {
        var temp = TestPaths.Temp("bin");
        try
        {
            Assert.True(BinFolder.Contains(temp, Path.Combine(temp, "_rejected", "p", "k.rpf", "jbib_001_u.ydd")));
            Assert.True(BinFolder.Contains(temp, Path.Combine(temp, "p", "_REJECTED", "jbib_001_u.ydd")));
            Assert.False(BinFolder.Contains(temp, Path.Combine(temp, "p", "k.rpf", "jbib_001_u.ydd")));
            Assert.False(BinFolder.Contains(temp, Path.Combine(temp, "p", "_rejected.ydd")));   // a FOLDER, not a file name

            Directory.CreateDirectory(Path.Combine(temp, "_rejected", "k.rpf"));
            File.WriteAllBytes(Path.Combine(temp, "_rejected", "k.rpf", "jbib_001_u.ydd"), new byte[16]);
            Assert.Empty(Index(temp, "t"));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void Cancelling_stops_the_indexing()
    {
        if (!TestPaths.HasLegacyPacks) { output.WriteLine("SKIPPED: no downloads"); return; }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            Indexer.Index(TestPaths.Downloads("vrp_clothes_f_civil03"), "civil03", new IndexOptions(), null, cancellation.Token));
    }
}
