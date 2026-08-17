using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CodeWalker.GameFiles;
using Xunit;

namespace Duble.Tests;

public class Rsc7HeaderTests
{
    [Fact]
    public void Stored_deflate_rozpakowuje_sie_do_tego_samego()
    {
        var rnd = new Random(7);
        foreach (int n in new[] { 0, 1, 100, 65535, 65536, 200_000 })
        {
            var dane = new byte[n]; rnd.NextBytes(dane);
            var st = Rsc7Header.StoredDeflate(dane);
            using var ms = new MemoryStream(st); using var ds = new DeflateStream(ms, CompressionMode.Decompress); using var wy = new MemoryStream();
            ds.CopyTo(wy);
            Assert.Equal(dane, wy.ToArray());
        }
    }

    [Fact]
    public void Owin_daje_naglowek_z_wersja_i_flagami_wpisu()
    {
        // Version wpisu wynika z flag: (sys>>28)<<4 | (gfx>>28)  ->  0x9, 0xF = 159 (gen9 ydd)
        var wpis = new RpfResourceFileEntry { SystemFlags = 0x92345678u, GraphicsFlags = 0xFABCDEF0u, Name = "x.ydd" };
        Assert.Equal(159, wpis.Version);
        var dane = new byte[] { 1, 2, 3, 4, 5 };
        var owin = Rsc7Header.Owin(wpis, dane);
        Assert.True(Rsc7Header.JestRsc7(owin));
        Assert.Equal(159, Rsc7Header.Wersja(owin));
        Assert.Equal(0x92345678u, BitConverter.ToUInt32(owin, 8));
        Assert.Equal(0xFABCDEF0u, BitConverter.ToUInt32(owin, 12));
        Assert.True(Rsc7Header.Gen9(owin, ".ydd") == true);
        Assert.Equal(dane, ResourceBuilder.Decompress(owin.Skip(16).ToArray()));
    }

    [Theory]
    [InlineData(165, ".ydd", false)]
    [InlineData(159, ".ydd", true)]
    [InlineData(13, ".ytd", false)]
    [InlineData(5, ".ytd", true)]
    [InlineData(99, ".ydd", null)]
    public void Gen9_z_wersji_naglowka(int wersja, string ext, bool? oczekiwane)
    {
        var b = new byte[24]; BitConverter.GetBytes(0x37435352u).CopyTo(b, 0); BitConverter.GetBytes(wersja).CopyTo(b, 4);
        Assert.Equal(oczekiwane, Rsc7Header.Gen9(b, ext));
        Assert.Null(Rsc7Header.Gen9(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, ext));
    }
}
