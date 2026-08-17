using Duble.App;
using Xunit;

namespace Duble.Tests;

public class ArgumentyTests
{
    [Fact]
    public void Parsuje_wszystkie_przelaczniki()
    {
        var a = Argumenty.Parsuj(new[] { "--dev", "--ui-folder", @"C:\ui", "--project", @"C:\p\Studio.duble", "--view", "sources", "--lang", "en", "--theme", "dark", "--screenshot", @"C:\z.png" });
        Assert.True(a.Dev); Assert.Equal(@"C:\ui", a.UiFolder); Assert.Equal(@"C:\p\Studio.duble", a.Project);
        Assert.Equal("sources", a.Widok); Assert.Equal("en", a.Jezyk); Assert.Equal("dark", a.Motyw); Assert.Equal(@"C:\z.png", a.Zrzut);
    }

    [Fact]
    public void Bez_argumentow_wszystko_puste()
    {
        var a = Argumenty.Parsuj(new string[0]);
        Assert.False(a.Dev); Assert.Null(a.UiFolder); Assert.Null(a.Project); Assert.Null(a.Widok); Assert.Null(a.Zrzut);
    }

    [Fact]
    public void Plik_duble_bez_przelacznika_to_projekt()
    {
        var a = Argumenty.Parsuj(new[] { @"C:\p\Moj.duble" });   // dwuklik na pliku .duble
        Assert.Equal(@"C:\p\Moj.duble", a.Project);
    }
}
