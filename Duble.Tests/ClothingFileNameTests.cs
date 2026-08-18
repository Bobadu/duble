using Xunit;

namespace Duble.Tests;

/// <summary>R*'s clothing file names, which is how Duble knows a slot, a number and a colour variant apart.</summary>
public class ClothingFileNameTests
{
    [Theory]
    [InlineData("jbib_027_u.ydd", "jbib", 27, "u", false, null)]
    [InlineData("UPPR_015_R.ydd", "uppr", 15, "r", false, null)]
    [InlineData("jbib_022_u_1.ydd", "jbib", 22, "u_1", false, null)]
    [InlineData("p_head_003.ydd", "p_head", 3, "u", true, null)]
    [InlineData("p_eyes_000_1.ydd", "p_eyes", 0, "u_1", true, null)]
    [InlineData("mp_f_freemode_01_paczka^jbib_000_u.ydd", "jbib", 0, "u", false, "mp_f_freemode_01_paczka")]
    [InlineData("mp_f_freemode_01_p_paczka^p_head_002.ydd", "p_head", 2, "u", true, "mp_f_freemode_01_p_paczka")]
    public void A_model_name_gives_the_slot_number_and_suffix(
        string fileName, string slot, int number, string suffix, bool isProp, string container)
    {
        var parsed = ClothingFileName.ParseModel(fileName);
        Assert.NotNull(parsed);
        Assert.Equal(slot, parsed.Slot);
        Assert.Equal(number, parsed.Number);
        Assert.Equal(suffix, parsed.Suffix);
        Assert.Equal(isProp, parsed.IsProp);
        Assert.Equal(container, parsed.Container);
    }

    [Theory]
    [InlineData("jbib_diff_027_a_uni.ytd", "jbib", 27, "a", "uni", false, null)]
    [InlineData("uppr_diff_015_a_whi.ytd", "uppr", 15, "a", "whi", false, null)]
    [InlineData("jbib_diff_022_b_uni_1.ytd", "jbib", 22, "b", "uni", false, null)]
    [InlineData("p_head_diff_003_a.ytd", "p_head", 3, "a", "uni", true, null)]
    [InlineData("mp_f_freemode_01_paczka^jbib_diff_000_c_uni.ytd", "jbib", 0, "c", "uni", false, "mp_f_freemode_01_paczka")]
    public void A_texture_name_also_gives_the_colour_letter_and_the_race(
        string fileName, string slot, int number, string letter, string race, bool isProp, string container)
    {
        var parsed = ClothingFileName.ParseTexture(fileName);
        Assert.NotNull(parsed);
        Assert.Equal(slot, parsed.Slot);
        Assert.Equal(number, parsed.Number);
        Assert.Equal(letter, parsed.Letter);
        Assert.Equal(race, parsed.Race);
        Assert.Equal(isProp, parsed.IsProp);
        Assert.Equal(container, parsed.Container);
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("jbib_27_u.ydd")]              // the number has to be three digits
    [InlineData("jbib_027_x.ydd")]             // the suffix is only ever u or r
    [InlineData("jbib_diff_027_a_uni.ydd")]    // a texture name under a model's extension
    [InlineData("mp_f_freemode_01_mp_f_civil01.ymt")]
    public void Anything_else_parses_as_nothing(string fileName)
    {
        Assert.Null(ClothingFileName.ParseModel(fileName));
        Assert.Null(ClothingFileName.ParseTexture(fileName));
    }
}
