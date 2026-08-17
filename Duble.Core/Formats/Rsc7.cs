// Rsc7.cs — dyskowy format zasobu RSC7 (naglowek 16 B + ladunek deflate) dla wpisow z archiwum.
//
// RpfFile.ExtractFile oddaje zasoby ZDEKOMPRESOWANE i BEZ naglowka; nasze czytanie idzie przez
// RpfFile.LoadResourceFile, ktory naglowka wymaga (bierze z niego wersje i flagi stron). Zamiast
// kompresowac (drogo), pakujemy ladunek w deflate "stored" — kopia bajtow w blokach po 65535.
// (Stary DubleCli podawal LoadResourceFile gole dane z archiwum i czytal smieci — zrodla .rpf nie dzialaly.)
using System;
using CodeWalker.GameFiles;

namespace Duble.Core;

public static class Rsc7
{
    const uint Magia = 0x37435352; // "RSC7"

    public static bool JestRsc7(byte[] dane) => dane != null && dane.Length >= 16 && BitConverter.ToUInt32(dane, 0) == Magia;
    public static int Wersja(byte[] dane) => JestRsc7(dane) ? BitConverter.ToInt32(dane, 4) : -1;

    /// <summary>true = gen9 (Enhanced), false = legacy, null = nie wiadomo (brak naglowka albo obca wersja).</summary>
    public static bool? Gen9(byte[] dane, string rozszerzenie)
    {
        int w = Wersja(dane);
        if (w < 0) return null;
        switch ((rozszerzenie ?? "").ToLowerInvariant())
        {
            case ".ydd": case ".ydr": case ".yft": return w == 159 ? true : w == 165 ? false : null;
            case ".ytd": return w == 5 ? true : w == 13 ? false : null;
            default: return null;
        }
    }

    public static byte[] Owin(RpfFileEntry wpis, byte[] dane)
        => wpis is RpfResourceFileEntry re ? Owin(re, dane) : dane;

    public static byte[] Owin(RpfResourceFileEntry wpis, byte[] daneZdekompresowane)
    {
        if (daneZdekompresowane == null) return null;
        var ladunek = StoredDeflate(daneZdekompresowane);
        var wy = new byte[16 + ladunek.Length];
        BitConverter.GetBytes(Magia).CopyTo(wy, 0);
        BitConverter.GetBytes((uint)wpis.Version).CopyTo(wy, 4);
        BitConverter.GetBytes(wpis.SystemFlags.Value).CopyTo(wy, 8);
        BitConverter.GetBytes(wpis.GraphicsFlags.Value).CopyTo(wy, 12);
        ladunek.CopyTo(wy, 16);
        return wy;
    }

    /// <summary>Surowy strumien deflate zlozony wylacznie z blokow "stored" (BTYPE=00): LEN/NLEN + bajty.</summary>
    public static byte[] StoredDeflate(byte[] dane)
    {
        int n = dane.Length;
        int blokow = Math.Max(1, (n + 65534) / 65535);
        var wy = new byte[n + blokow * 5];
        int i = 0, o = 0;
        for (int b = 0; b < blokow; b++)
        {
            int len = Math.Min(65535, n - i);
            bool ostatni = b == blokow - 1;
            wy[o++] = (byte)(ostatni ? 1 : 0);
            wy[o++] = (byte)(len & 0xFF); wy[o++] = (byte)(len >> 8);
            wy[o++] = (byte)(~len & 0xFF); wy[o++] = (byte)((~len >> 8) & 0xFF);
            Buffer.BlockCopy(dane, i, wy, o, len);
            i += len; o += len;
        }
        return wy;
    }
}
