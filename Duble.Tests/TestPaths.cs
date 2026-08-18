#nullable enable
using System;
using System.IO;

namespace Duble.Tests;

/// <summary>
/// Where the tests find things. The root is the folder holding Duble.sln, searched for upwards from the
/// output folder, so this works both in the public repository (its root) and inside the studio (tools\Duble).
///
/// The test packs are large .rpf files and are not in the repository: DUBLE_TEST_DATA says where they are, and
/// without it a `downloads` folder next to the repository is used, which is how the studio is laid out. A test
/// that needs data which is not there says SKIPPED and passes — `dotnet test` has to work straight after a
/// clone.
/// </summary>
public static class TestPaths
{
    /// <summary>The folder holding Duble.sln.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>The folder holding the test packs, or null when there is none.</summary>
    public static string? TestData { get; } = FindTestData();

    public static string Ui => Path.Combine(Root, "Duble.App", "ui");

    public static string Golden(string file) => Path.Combine(Root, "Duble.Tests", "golden", file);

    public static string Downloads(string pack) => Path.Combine(TestData ?? Path.Combine(Root, "_no-test-data"), pack);

    /// <summary>An installed GTA V Enhanced, from GTAV_ENHANCED.</summary>
    public static string? Game => Environment.GetEnvironmentVariable("GTAV_ENHANCED");

    public static string? Dlc(string pack) => Game == null ? null : Path.Combine(Game, "onigiri", "dlcpacks", pack, "dlc.rpf");

    public static bool HasLegacyPacks => TestData != null
        && Directory.Exists(Downloads("vrp_clothes_f_civil01"))
        && Directory.Exists(Downloads("civil_f_premium"));

    public static bool HasGame => Game != null && File.Exists(Dlc("studio_body")!);

    /// <summary>A temporary folder for one test; the caller deletes it.</summary>
    public static string Temp(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), "duble-tests", name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(folder);
        return folder;
    }

    static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "Duble.sln"))) folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("no folder with Duble.sln above " + AppContext.BaseDirectory);
    }

    static string? FindTestData()
    {
        var configured = Environment.GetEnvironmentVariable("DUBLE_TEST_DATA");
        if (!string.IsNullOrWhiteSpace(configured)) return Directory.Exists(configured) ? configured : null;

        var folder = new DirectoryInfo(Root);
        while (folder != null)
        {
            var candidate = Path.Combine(folder.FullName, "downloads");
            if (Directory.Exists(candidate)) return candidate;
            folder = folder.Parent;
        }
        return null;
    }
}
