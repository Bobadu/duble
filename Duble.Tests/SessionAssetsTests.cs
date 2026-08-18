using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Duble.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

/// <summary>
/// The session over real game files: comparing, saving the result, and building the assets the interface asks
/// for — a full-size texture as PNG and a garment as GLB. Skipped without the game.
/// </summary>
public class SessionAssetsTests
{
    readonly ITestOutputHelper output;

    public SessionAssetsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Comparing_saves_the_result_and_textures_and_meshes_are_built_on_demand()
    {
        if (!TestPaths.HasGame) { output.WriteLine("SKIPPED: no studio_body\\dlc.rpf"); return; }

        using var app = new TestApp("session-assets");
        var session = app.Session;
        session.New("P", Path.Combine(app.Temp, "P.duble"));

        var source = session.Project.AddSource(TestPaths.Dlc("studio_body"), "src1");
        var garments = app.Services.GetRequiredService<IGarmentIndexer>()
            .Index(source.Path, source.Name, new IndexOptions { ThumbnailFolder = session.Project.ThumbnailFolder })
            .Value.Garments;
        foreach (var garment in garments) garment.SourceId = source.Id;
        session.EditCatalog(catalog => catalog.Upsert(garments));

        session.Compare(default, null);
        Assert.NotNull(session.Comparison);
        Assert.True(File.Exists(session.Project.ComparisonFile));
        Assert.Contains("\"duplikaty\":", JsonSerializer.Serialize(session.Summary(), Bridge.Json));
        session.Save();

        // a texture: decoded from the game file on the first ask, from the cache afterwards
        var sha = session.Catalog.Garments.SelectMany(garment => garment.Textures).First(t => t.IsDecoded).Sha256;
        Assert.NotNull(session.FindTexture(sha));
        using (var png = session.Asset("tex", sha))
        {
            Assert.NotNull(png);
            var header = new byte[8];
            png.ReadExactly(header, 0, 8);
            Assert.Equal(0x89, header[0]);
            Assert.Equal((byte)'P', header[1]);
        }
        var cached = Path.Combine(session.Project.TextureFolder, sha + ".png");
        Assert.True(File.Exists(cached));
        using (var again = session.Asset("tex", sha)) Assert.Equal(new FileInfo(cached).Length, again.Length);
        Assert.Null(session.Asset("tex", "NO-SUCH-SHA"));

        // a mesh: the GLB of one garment with the texture of a chosen variant, cached under mesh\
        var uppr = session.Catalog.Garments.First(garment => garment.Slot == "uppr" && garment.Number == 15);
        using (var glb = session.Asset("mesh", uppr.Id, "w=a"))
        {
            Assert.NotNull(glb);
            var magic = new byte[4];
            glb.ReadExactly(magic, 0, 4);
            Assert.Equal("glTF", Encoding.ASCII.GetString(magic));
        }
        var meshes = Directory.GetFiles(session.Project.MeshFolder, "*.glb");
        Assert.Single(meshes);
        using (var again = session.Asset("mesh", uppr.Id, "w=a")) Assert.Equal(new FileInfo(meshes[0]).Length, again.Length);
        Assert.Null(session.Asset("mesh", "no|such|garment|0|u", null));

        // reopening finds the comparison; switching the source off leaves nothing to compare
        session.Close();
        session.Open(Path.Combine(app.Temp, "P.duble"));
        Assert.NotNull(session.Comparison);

        session.Project.Sources[0].Enabled = false;
        session.Compare(default, null);
        Assert.Empty(session.Comparison.Groups);
    }

    [Fact]
    public void An_asset_key_cannot_walk_out_of_the_cache_folder()
    {
        using var app = new TestApp("session-assets-safety");
        app.NewProject();

        Assert.Null(app.Session.Asset("thumb", @"..\..\Windows\win.ini"));
        Assert.Null(app.Session.Asset("thumb", "sub/dir"));
        Assert.Null(app.Session.Asset("no-such-category", "abc"));
    }
}
