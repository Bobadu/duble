// PngWriter.cs — minimalny koder PNG (RGB i RGBA, bez filtrow, bez przeplotu).
//
// PO CO WLASNY: raport ma osadzac miniatury jako data:image/png;base64, a caly projekt
// stoi wylacznie na CodeWalker.Core — nie chcemy ciagnac System.Drawing.Common ani
// zadnej biblioteki graficznej dla dwoch funkcji. PNG bez filtrow to okolo 60 linii:
// naglowek + zlib(deflate) + CRC.
using System;
using System.IO;
using System.IO.Compression;

namespace Duble.Core.Formats;

public static class PngWriter
{
    static readonly byte[] Sygnatura = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    static readonly uint[] TablicaCrc = ZbudujCrc();

    static uint[] ZbudujCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    static uint Crc(byte[] dane, int od, int ile)
    {
        uint c = 0xFFFFFFFFu;
        for (int i = od; i < od + ile; i++) c = TablicaCrc[(c ^ dane[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    static uint Adler32(byte[] dane)
    {
        uint a = 1, b = 0;
        foreach (var x in dane) { a = (a + x) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }

    static void Be32(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
    }

    static void Kawalek(Stream s, string typ, byte[] tresc)
    {
        var buf = new byte[4 + tresc.Length];
        for (int i = 0; i < 4; i++) buf[i] = (byte)typ[i];
        Buffer.BlockCopy(tresc, 0, buf, 4, tresc.Length);
        Be32(s, (uint)tresc.Length);
        s.Write(buf, 0, buf.Length);
        Be32(s, Crc(buf, 0, buf.Length));
    }

    /// <summary>Koduje obraz RGB (3 bajty na piksel, bez alfy) do PNG.</summary>
    public static byte[] Rgb(byte[] rgb, int w, int h) => Koduj(rgb, w, h, 3, 2);

    /// <summary>Koduje obraz RGBA (4 bajty na piksel, z alfa) do PNG — tekstury do podgladu 3D.</summary>
    public static byte[] Rgba(byte[] rgba, int w, int h) => Koduj(rgba, w, h, 4, 6);

    static byte[] Koduj(byte[] px, int w, int h, int bpp, byte typKoloru)
    {
        // scanline = bajt filtra (0 = None) + w*bpp bajtow
        var surowe = new byte[h * (1 + w * bpp)];
        for (int y = 0; y < h; y++)
        {
            int zrodlo = y * w * bpp;
            int cel = y * (1 + w * bpp);
            surowe[cel] = 0;
            Buffer.BlockCopy(px, zrodlo, surowe, cel + 1, w * bpp);
        }

        byte[] skompresowane;
        using (var ms = new MemoryStream())
        {
            // naglowek zlib: 0x78 0x01 (deflate, okno 32K, brak slownika) — (0x7801 % 31 == 0)
            ms.WriteByte(0x78); ms.WriteByte(0x01);
            using (var df = new DeflateStream(ms, CompressionLevel.Optimal, true))
                df.Write(surowe, 0, surowe.Length);
            Be32(ms, Adler32(surowe));
            skompresowane = ms.ToArray();
        }

        using var wy = new MemoryStream();
        wy.Write(Sygnatura, 0, Sygnatura.Length);

        var ihdr = new byte[13];
        ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
        ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
        ihdr[8] = 8;    // 8 bitow na kanal
        ihdr[9] = typKoloru;    // 2 = RGB, 6 = RGBA
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        Kawalek(wy, "IHDR", ihdr);
        Kawalek(wy, "IDAT", skompresowane);
        Kawalek(wy, "IEND", Array.Empty<byte>());
        return wy.ToArray();
    }
}
