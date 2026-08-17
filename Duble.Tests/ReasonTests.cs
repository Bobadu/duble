using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Duble.Tests;

public class ReasonTests
{
    [Fact]
    public void Slowniki_pl_i_en_maja_te_same_klucze()
    {
        var pl = Texts.Dictionary("pl"); var en = Texts.Dictionary("en");
        Assert.NotEmpty(pl);
        Assert.Empty(pl.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(pl.Keys));
    }

    [Fact]
    public void Formatter_podstawia_parametry_i_tlumaczy_wartosci_z_malpa()
    {
        var p = new Reason("NO_TEXTURES", ("geo", "@geo.identyczna"));
        Assert.Equal("geometria identyczna, ale brak tekstur do porównania", Texts.Reason(p, "pl"));
        Assert.Equal("identical geometry, but no textures to compare", Texts.Reason(p, "en"));
    }

    [Fact]
    public void Formatter_daje_polskie_napisy_z_ogonkami()
    {
        Assert.Equal("ten sam model, te same tekstury (10/10 i 10/10 tekstur wspólnych)",
            Texts.Reason(new Reason("SAME_MODEL_SAME_TEX", ("a", 10), ("na", 10), ("b", 10), ("nb", 10)), "pl"));
        Assert.Equal("model tylko PODOBNY (odległość 0.034), ale tekstury te same (3/3 i 3/3 tekstur wspólnych)",
            Texts.Reason(new Reason("SIMILAR_MODEL_SAME_TEX", ("dist", "0.034"), ("a", 3), ("na", 3), ("b", 3), ("nb", 3)), "pl"));
        Assert.Equal("zwycięzca 93 pkt, przegrani 71, 40 pkt",
            Texts.Reason(new Reason("WINNER", ("zw", "93"), ("przegrani", "71, 40")), "pl"));
    }

    [Fact]
    public void Brak_klucza_nie_wywala_tylko_pokazuje_klucz()
    {
        Assert.Equal("[powod.NIE_MA_TAKIEGO]", Texts.T("en", "powod.NIE_MA_TAKIEGO"));
    }

    [Fact]
    public void Punktacja_tekst_pl()
    {
        var p = new QualityScore { ResolutionPx = 1024, Resolution = 40, MipmapShare = 1.0, Mipmaps = 20, VariantCount = 10, Variants = 10, Format = 10, WrongFormatCount = 0, LodLevels = 3, Lod = 10 };
        Assert.Equal("rozdzielczość 1024px:40 | mipy 100 %:20 | wariantów 10:10 | format:10 | LOD 3:10", p.Text("pl"));
        p.WrongFormatCount = 2; p.Format = 8;
        Assert.Equal("rozdzielczość 1024px:40 | mipy 100 %:20 | wariantów 10:10 | format:8 (2 BC1 z alfą) | LOD 3:10", p.Text("pl"));
        Assert.Equal("brak tekstur", new QualityScore { NoTextures = true }.Text("pl"));
    }
}
