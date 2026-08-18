using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CodeWalker.GameFiles;
using Xunit;

namespace Duble.Tests;

/// <summary>The RSC7 header Duble writes when it unpacks an archive, and what it reads back out of one.</summary>
public class Rsc7HeaderTests
{
    [Fact]
    public void A_stored_deflate_stream_inflates_to_what_went_in()
    {
        var random = new Random(7);
        foreach (int size in new[] { 0, 1, 100, 65535, 65536, 200_000 })
        {
            var data = new byte[size];
            random.NextBytes(data);

            var stored = Rsc7Header.StoredDeflate(data);
            using var input = new MemoryStream(stored);
            using var inflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            inflate.CopyTo(output);

            Assert.Equal(data, output.ToArray());
        }
    }

    [Fact]
    public void Wrapping_an_entry_keeps_its_version_and_its_page_flags()
    {
        // An entry's version comes from its flags: (sys >> 28) << 4 | (gfx >> 28) -> 0x9, 0xF = 159, a gen9 ydd
        var entry = new RpfResourceFileEntry
        {
            SystemFlags = 0x92345678u,
            GraphicsFlags = 0xFABCDEF0u,
            Name = "x.ydd",
        };
        Assert.Equal(159, entry.Version);

        var data = new byte[] { 1, 2, 3, 4, 5 };
        var wrapped = Rsc7Header.Wrap(entry, data);

        Assert.True(Rsc7Header.IsRsc7(wrapped));
        Assert.Equal(159, Rsc7Header.Version(wrapped));
        Assert.Equal(0x92345678u, BitConverter.ToUInt32(wrapped, 8));
        Assert.Equal(0xFABCDEF0u, BitConverter.ToUInt32(wrapped, 12));
        Assert.True(Rsc7Header.IsEnhanced(wrapped, ".ydd"));
        Assert.Equal(data, ResourceBuilder.Decompress(wrapped.Skip(16).ToArray()));
    }

    [Theory]
    [InlineData(165, ".ydd", false)]
    [InlineData(159, ".ydd", true)]
    [InlineData(13, ".ytd", false)]
    [InlineData(5, ".ytd", true)]
    [InlineData(99, ".ydd", null)]        // a version neither game writes: Duble does not guess
    public void The_header_version_says_which_game_the_file_is_for(int version, string extension, bool? expected)
    {
        var header = new byte[24];
        BitConverter.GetBytes(0x37435352u).CopyTo(header, 0);
        BitConverter.GetBytes(version).CopyTo(header, 4);

        Assert.Equal(expected, Rsc7Header.IsEnhanced(header, extension));
        Assert.Null(Rsc7Header.IsEnhanced(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, extension));   // too short to be a header
    }
}
