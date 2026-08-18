#nullable enable
// A minimal PNG encoder: RGB and RGBA, no filters, no interlacing.
//
// WHY OUR OWN: the report embeds thumbnails as data:image/png;base64, and the whole project stands on
// CodeWalker.Core alone — pulling in System.Drawing.Common, or any imaging library, for two functions is not
// worth it. An unfiltered PNG is about sixty lines: header, zlib(deflate), CRC.
using System;
using System.IO;
using System.IO.Compression;

namespace Duble.Core.Formats;

/// <summary>Writes pixels out as a PNG file.</summary>
public static class PngWriter
{
    static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    static readonly uint[] CrcTable = BuildCrcTable();

    static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    static uint Crc(byte[] data, int offset, int count)
    {
        uint crc = 0xFFFFFFFFu;
        for (int i = offset; i < offset + count; i++) crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    static void WriteBigEndian(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    /// <summary>One PNG chunk: length, four-character type, payload, then a CRC over the type and payload.</summary>
    static void Chunk(Stream stream, string type, byte[] payload)
    {
        var typed = new byte[4 + payload.Length];
        for (int i = 0; i < 4; i++) typed[i] = (byte)type[i];
        Buffer.BlockCopy(payload, 0, typed, 4, payload.Length);

        WriteBigEndian(stream, (uint)payload.Length);
        stream.Write(typed, 0, typed.Length);
        WriteBigEndian(stream, Crc(typed, 0, typed.Length));
    }

    /// <summary>An RGB image (three bytes per pixel, no alpha) as a PNG.</summary>
    public static byte[] Rgb(byte[] rgb, int width, int height) => Encode(rgb, width, height, 3, 2);

    /// <summary>An RGBA image (four bytes per pixel) as a PNG — the textures the 3D preview uses.</summary>
    public static byte[] Rgba(byte[] rgba, int width, int height) => Encode(rgba, width, height, 4, 6);

    static byte[] Encode(byte[] pixels, int width, int height, int bytesPerPixel, byte colorType)
    {
        // a scanline is one filter byte (0 = None) followed by width * bytesPerPixel bytes
        int stride = 1 + width * bytesPerPixel;
        var raw = new byte[height * stride];
        for (int y = 0; y < height; y++)
        {
            raw[y * stride] = 0;
            Buffer.BlockCopy(pixels, y * width * bytesPerPixel, raw, y * stride + 1, width * bytesPerPixel);
        }

        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            // zlib header 0x78 0x01: deflate, 32K window, no dictionary (0x7801 % 31 == 0)
            buffer.WriteByte(0x78);
            buffer.WriteByte(0x01);
            using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, true))
                deflate.Write(raw, 0, raw.Length);
            WriteBigEndian(buffer, Adler32(raw));
            compressed = buffer.ToArray();
        }

        using var output = new MemoryStream();
        output.Write(Signature, 0, Signature.Length);

        var header = new byte[13];
        header[0] = (byte)(width >> 24);
        header[1] = (byte)(width >> 16);
        header[2] = (byte)(width >> 8);
        header[3] = (byte)width;
        header[4] = (byte)(height >> 24);
        header[5] = (byte)(height >> 16);
        header[6] = (byte)(height >> 8);
        header[7] = (byte)height;
        header[8] = 8;            // bits per channel
        header[9] = colorType;    // 2 = RGB, 6 = RGBA
        header[10] = 0;           // deflate
        header[11] = 0;           // adaptive filtering
        header[12] = 0;           // no interlacing

        Chunk(output, "IHDR", header);
        Chunk(output, "IDAT", compressed);
        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }
}
