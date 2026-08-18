using System.Linq;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// The engine's dictionaries: reason sentences, the quality breakdown, and the substitution rules they rely on.
/// The Polish text is checked character for character — the golden master compares against it.
/// </summary>
public class TextsTests
{
    [Fact]
    public void Both_dictionaries_carry_the_same_keys()
    {
        var pl = Texts.Dictionary("pl");
        var en = Texts.Dictionary("en");
        Assert.NotEmpty(pl);
        Assert.Empty(pl.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(pl.Keys));
    }

    [Fact]
    public void A_parameter_starting_with_an_at_sign_is_itself_translated()
    {
        var reason = new Reason("NO_TEXTURES", ("geo", "@geo.identical"));
        Assert.Equal("geometria identyczna, ale brak tekstur do porównania", Texts.Reason(reason, "pl"));
        Assert.Equal("identical geometry, but no textures to compare", Texts.Reason(reason, "en"));
    }

    [Fact]
    public void Reason_sentences_keep_their_polish_diacritics()
    {
        Assert.Equal("ten sam model, te same tekstury (10/10 i 10/10 tekstur wspólnych)",
            Texts.Reason(new Reason("SAME_MODEL_SAME_TEX", ("a", 10), ("na", 10), ("b", 10), ("nb", 10)), "pl"));
        Assert.Equal("model tylko PODOBNY (odległość 0.034), ale tekstury te same (3/3 i 3/3 tekstur wspólnych)",
            Texts.Reason(new Reason("SIMILAR_MODEL_SAME_TEX", ("dist", "0.034"), ("a", 3), ("na", 3), ("b", 3), ("nb", 3)), "pl"));
        Assert.Equal("zwycięzca 93 pkt, przegrani 71, 40 pkt",
            Texts.Reason(new Reason("WINNER", ("winner", "93"), ("losers", "71, 40")), "pl"));
    }

    [Fact]
    public void A_missing_key_shows_the_key_rather_than_throwing()
    {
        Assert.Equal("[reason.NO_SUCH_CODE]", Texts.T("en", "reason.NO_SUCH_CODE"));
    }

    [Fact]
    public void A_language_duble_does_not_speak_is_reported_as_english()
    {
        Assert.Equal("pl", Texts.Tag("pl-PL"));
        Assert.Equal("en", Texts.Tag("de"));
        Assert.Equal("en", Texts.Tag("en-GB"));
        Assert.Equal("pl", Texts.Tag(null));
    }

    [Fact]
    public void The_quality_breakdown_reads_as_a_sentence()
    {
        var score = new QualityScore
        {
            ResolutionPx = 1024, Resolution = 40, MipmapShare = 1.0, Mipmaps = 20,
            VariantCount = 10, Variants = 10, Format = 10, WrongFormatCount = 0, LodLevels = 3, Lod = 10,
        };
        Assert.Equal("rozdzielczość 1024px:40 | mipy 100 %:20 | wariantów 10:10 | format:10 | LOD 3:10", score.Text("pl"));

        score.WrongFormatCount = 2;
        score.Format = 8;
        Assert.Equal("rozdzielczość 1024px:40 | mipy 100 %:20 | wariantów 10:10 | format:8 (2 BC1 z alfą) | LOD 3:10", score.Text("pl"));

        Assert.Equal("brak tekstur", new QualityScore { NoTextures = true }.Text("pl"));
    }
}
