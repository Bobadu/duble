#nullable enable
using System.IO;
using System.Linq;
using Duble.Core.Model;
using Duble.Core.Storage;
using Xunit;

namespace Duble.Tests;

public class CatalogTests
{
    static Garment Garment(string pack, string slot, int number, GameFormat format = GameFormat.Legacy) => new()
    {
        Id = $"{pack}|k.rpf|{slot}|{number}|u",
        PackName = pack, Container = "k.rpf", Slot = slot, Number = number, Suffix = "u", GameFormat = format,
    };

    [Theory]
    [InlineData(GameFormat.Enhanced, "gen9")]
    [InlineData(GameFormat.Legacy, "legacy")]
    public void A_format_keeps_the_word_the_interface_and_the_report_use(GameFormat format, string expected)
        => Assert.Equal(expected, format.ToLabel());

    [Theory]
    [InlineData(true, GameFormat.Enhanced)]
    [InlineData(false, GameFormat.Legacy)]
    [InlineData(null, GameFormat.Legacy)]
    public void A_file_header_decides_the_format(bool? enhanced, GameFormat expected)
        => Assert.Equal(expected, GameFormats.FromHeader(enhanced));

    [Fact]
    public void Upsert_replaces_a_garment_with_the_same_id_and_keeps_the_order()
    {
        var catalog = new Catalog();
        catalog.Upsert(new[] { Garment("b", "jbib", 2), Garment("a", "feet", 1) });
        catalog.Upsert(new[] { Garment("a", "feet", 1, GameFormat.Enhanced) });

        Assert.Equal(2, catalog.Garments.Count);
        Assert.Equal(new[] { "a", "b" }, catalog.Garments.Select(g => g.PackName));
        Assert.Equal(GameFormat.Enhanced, catalog.Garments[0].GameFormat);
    }

    [Fact]
    public void RemovePack_drops_only_that_pack_whatever_the_casing()
    {
        var catalog = new Catalog();
        catalog.Upsert(new[] { Garment("keep", "jbib", 1), Garment("drop", "jbib", 2) });
        catalog.RemovePack("DROP");
        Assert.Equal("keep", Assert.Single(catalog.Garments).PackName);
    }

    [Fact]
    public void A_catalog_survives_a_round_trip_through_the_store()
    {
        var folder = Path.Combine(Path.GetTempPath(), "duble-catalog-" + Path.GetRandomFileName());
        var path = Path.Combine(folder, "catalog.json");
        try
        {
            var store = new JsonCatalogStore(new FixedClock(new System.DateTimeOffset(2026, 8, 17, 21, 30, 0, System.TimeSpan.Zero)));
            var written = new Catalog { Sources = { ["pack"] = @"C:\packs\pack" } };
            written.Upsert(new[] { Garment("pack", "uppr", 15, GameFormat.Enhanced) });
            written.Garments[0].Textures.Add(new TextureInfo { FileName = "uppr_diff_015_a_uni.ytd", Width = 1024, Height = 1024, MipLevels = 8, IsDecoded = true });

            Assert.True(store.Save(written, path).IsSuccess);

            var read = store.Load(path);
            Assert.Equal(Catalog.CurrentVersion, read.Version);
            Assert.Equal("2026-08-17 21:30:00", read.Built);
            Assert.Equal(@"C:\packs\pack", read.Sources["pack"]);
            var garment = Assert.Single(read.Garments);
            Assert.Equal(GameFormat.Enhanced, garment.GameFormat);
            Assert.Equal(1024, Assert.Single(garment.Textures).Width);

            // enums are written as words, not as numbers, so the file stays readable
            Assert.Contains("\"gameFormat\":\"enhanced\"", File.ReadAllText(path));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void A_catalog_from_an_older_version_of_Duble_loads_as_empty_so_the_app_reindexes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "duble-catalog-" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "catalog.json");
        try
        {
            File.WriteAllText(path, """{"Wersja":2,"Garments":[{"Id":"pack|k.rpf|jbib|1|u"}]}""");
            var store = new JsonCatalogStore(new FixedClock(System.DateTimeOffset.Now));
            Assert.Empty(store.Load(path).Garments);

            File.WriteAllText(path, "this is not json");
            Assert.Empty(store.Load(path).Garments);

            Assert.Empty(store.Load(Path.Combine(folder, "missing.json")).Garments);
        }
        finally { Directory.Delete(folder, true); }
    }
}
