using Duble.Core;
using Xunit;

namespace Duble.Tests;

public class NazwyTests
{
    [Theory]
    [InlineData("jbib_027_u.ydd", "jbib", 27, "u", false, null)]
    [InlineData("UPPR_015_R.ydd", "uppr", 15, "r", false, null)]
    [InlineData("jbib_022_u_1.ydd", "jbib", 22, "u_1", false, null)]
    [InlineData("p_head_003.ydd", "p_head", 3, "u", true, null)]
    [InlineData("p_eyes_000_1.ydd", "p_eyes", 0, "u_1", true, null)]
    [InlineData("mp_f_freemode_01_paczka^jbib_000_u.ydd", "jbib", 0, "u", false, "mp_f_freemode_01_paczka")]
    [InlineData("mp_f_freemode_01_p_paczka^p_head_002.ydd", "p_head", 2, "u", true, "mp_f_freemode_01_p_paczka")]
    public void Model_rozpoznaje_konwencje(string plik, string typ, int numer, string sufiks, bool props, string kontener)
    {
        var n = Nazwy.Model(plik);
        Assert.NotNull(n);
        Assert.Equal(typ, n.Typ); Assert.Equal(numer, n.Numer); Assert.Equal(sufiks, n.Sufiks);
        Assert.Equal(props, n.Props); Assert.Equal(kontener, n.Kontener);
    }

    [Theory]
    [InlineData("jbib_diff_027_a_uni.ytd", "jbib", 27, "a", "uni", false, null)]
    [InlineData("uppr_diff_015_a_whi.ytd", "uppr", 15, "a", "whi", false, null)]
    [InlineData("jbib_diff_022_b_uni_1.ytd", "jbib", 22, "b", "uni", false, null)]
    [InlineData("p_head_diff_003_a.ytd", "p_head", 3, "a", "uni", true, null)]
    [InlineData("mp_f_freemode_01_paczka^jbib_diff_000_c_uni.ytd", "jbib", 0, "c", "uni", false, "mp_f_freemode_01_paczka")]
    public void Tekstura_rozpoznaje_konwencje(string plik, string typ, int numer, string litera, string rasa, bool props, string kontener)
    {
        var n = Nazwy.Tekstura(plik);
        Assert.NotNull(n);
        Assert.Equal(typ, n.Typ); Assert.Equal(numer, n.Numer); Assert.Equal(litera, n.Litera);
        Assert.Equal(rasa, n.Rasa); Assert.Equal(props, n.Props); Assert.Equal(kontener, n.Kontener);
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("jbib_27_u.ydd")]        // numer musi miec 3 cyfry
    [InlineData("jbib_027_x.ydd")]       // sufiks tylko u/r
    [InlineData("jbib_diff_027_a_uni.ydd")]  // tekstura z rozszerzeniem modelu
    [InlineData("mp_f_freemode_01_mp_f_civil01.ymt")]
    public void Smieci_daja_null(string plik)
    {
        Assert.Null(Nazwy.Model(plik));
        Assert.Null(Nazwy.Tekstura(plik));
    }
}
