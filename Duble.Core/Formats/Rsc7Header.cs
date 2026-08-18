#nullable enable
// The on-disk shape of an RSC7 resource: a 16-byte header followed by a deflate payload.
//
// RpfFile.ExtractFile hands back a resource DECOMPRESSED and WITHOUT its header, but reading goes through
// RpfFile.LoadResourceFile, which needs that header — it takes the version and the page flags from it. Rather
// than compress the payload again, which is expensive and pointless here, it is wrapped in "stored" deflate
// blocks: the same bytes, in chunks of 65535.
//
// An older Duble handed LoadResourceFile the bare bytes from an archive and read rubbish, which is why .rpf
// sources did not work at all.
using System;
using CodeWalker.GameFiles;

namespace Duble.Core.Formats;

/// <summary>Reading and writing the RSC7 header that marks a GTA V resource file.</summary>
public static class Rsc7Header
{
    const uint Magic = 0x37435352; // "RSC7"

    public static bool IsRsc7(byte[]? data) => data != null && data.Length >= 16 && BitConverter.ToUInt32(data, 0) == Magic;

    /// <summary>The resource version from the header, or -1 when there is no header.</summary>
    public static int Version(byte[]? data) => IsRsc7(data) ? BitConverter.ToInt32(data!, 4) : -1;

    /// <summary>
    /// Which build the file was made for: true for gen9 (Enhanced), false for Legacy, null when it cannot be
    /// told — no header, or a version this does not know.
    /// </summary>
    public static bool? IsEnhanced(byte[]? data, string? extension)
    {
        int version = Version(data);
        if (version < 0) return null;

        return (extension ?? "").ToLowerInvariant() switch
        {
            ".ydd" or ".ydr" or ".yft" => version == 159 ? true : version == 165 ? false : null,
            ".ytd" => version == 5 ? true : version == 13 ? false : null,
            _ => null,
        };
    }

    /// <summary>Puts the header back on an extracted entry; a binary entry is handed back unchanged.</summary>
    public static byte[]? Wrap(RpfFileEntry entry, byte[]? data)
        => entry is RpfResourceFileEntry resource ? Wrap(resource, data) : data;

    /// <summary>Puts the header back on an extracted resource, so it reads exactly like a file on disk.</summary>
    public static byte[]? Wrap(RpfResourceFileEntry entry, byte[]? decompressed)
    {
        if (decompressed == null) return null;

        var payload = StoredDeflate(decompressed);
        var output = new byte[16 + payload.Length];
        BitConverter.GetBytes(Magic).CopyTo(output, 0);
        BitConverter.GetBytes((uint)entry.Version).CopyTo(output, 4);
        BitConverter.GetBytes(entry.SystemFlags.Value).CopyTo(output, 8);
        BitConverter.GetBytes(entry.GraphicsFlags.Value).CopyTo(output, 12);
        payload.CopyTo(output, 16);
        return output;
    }

    /// <summary>A raw deflate stream made only of "stored" blocks (BTYPE=00): LEN, NLEN, then the bytes.</summary>
    public static byte[] StoredDeflate(byte[] data)
    {
        int length = data.Length;
        int blocks = Math.Max(1, (length + 65534) / 65535);
        var output = new byte[length + blocks * 5];

        int read = 0, written = 0;
        for (int block = 0; block < blocks; block++)
        {
            int size = Math.Min(65535, length - read);
            bool last = block == blocks - 1;

            output[written++] = (byte)(last ? 1 : 0);
            output[written++] = (byte)(size & 0xFF);
            output[written++] = (byte)(size >> 8);
            output[written++] = (byte)(~size & 0xFF);
            output[written++] = (byte)((~size >> 8) & 0xFF);

            Buffer.BlockCopy(data, read, output, written, size);
            read += size;
            written += size;
        }

        return output;
    }
}
