using System;
using System.IO;
using Duble.App;
using Xunit;

namespace Duble.Tests;

public class GameDetectorTests
{
    const string LibraryFolders = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"271590\"\t\t\"12345\"\n\t\t}\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}\n";

    [Fact]
    public void The_steam_library_paths_are_read_out_of_the_vdf()
    {
        var libraries = GameDetector.ParseLibraryFolders(LibraryFolders);

        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, libraries);
    }

    [Fact]
    public void Only_folders_that_exist_are_offered()
    {
        var temp = TestPaths.Temp("game");
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, "onigiri", "dlcpacks"));

            var folders = GameDetector.FoldersOf(temp, "enhanced");

            var only = Assert.Single(folders);
            Assert.EndsWith(Path.Combine("onigiri", "dlcpacks"), only.Path);
            Assert.Equal("folder", only.Kind);
            Assert.Empty(GameDetector.FoldersOf(temp, "legacy"));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void Detecting_never_throws_and_finds_the_game_the_variable_points_at()
    {
        var games = GameDetector.Detect();

        if (TestPaths.Game != null && Directory.Exists(TestPaths.Game))
            Assert.Contains(games, game => game.Edition == "enhanced"
                && game.Path.TrimEnd('\\').Equals(TestPaths.Game.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
    }
}
