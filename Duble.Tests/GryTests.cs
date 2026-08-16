using System.IO;
using Duble.App;
using Xunit;

namespace Duble.Tests;

public class GryTests
{
    const string Vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"271590\"\t\t\"12345\"\n\t\t}\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}\n";

    [Fact]
    public void Parsuje_sciezki_bibliotek_steam()
    {
        var l = Gry.ParsujLibraryFolders(Vdf);
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, l);
    }

    [Fact]
    public void Propozycje_tylko_istniejace_foldery()
    {
        var tmp = Sciezki.Tymczasowy("gra");
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "onigiri", "dlcpacks"));
            var p = Gry.PropozycjeDla(tmp, "enhanced");
            Assert.Single(p); Assert.EndsWith(Path.Combine("onigiri", "dlcpacks"), p[0].Sciezka); Assert.Equal("folder", p[0].Typ);
            Assert.Empty(Gry.PropozycjeDla(tmp, "legacy"));
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Wykryj_nie_wywala_sie_i_znajduje_enhanced_gdy_jest_zmienna()
    {
        var gry = Gry.Wykryj();
        if (Sciezki.Gra != null && Directory.Exists(Sciezki.Gra))
            Assert.Contains(gry, g => g.Gra == "enhanced" && g.Sciezka.TrimEnd('\\').Equals(Sciezki.Gra.TrimEnd('\\'), System.StringComparison.OrdinalIgnoreCase));
    }
}
