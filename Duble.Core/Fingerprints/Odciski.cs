// Odciski.cs — liczenie odciskow geometrii i tekstur.
//
// ZALOZENIA POTWIERDZONE POMIAREM NA NASZYCH ZRODLACH (15.08):
//  - pozycja wierzcholka to zawsze skladowa 0 (Float3, offset 0) — sprawdzone przez
//    porownanie min/max z wierzcholkow z BoundingBox drawable'a: zgadza sie co do 0,001
//  - pliki gen9 wymagaja RpfManager.IsGen9 = true; bez tego blok Texture czyta sie
//    po staremu i zwraca smieci (Format = 0x406B77D8, wymiary 0x0). W trybie gen9 CodeWalker
//    czyta TAKZE legacy poprawnie (po wersji z naglowka RSC7) — dlatego Format.cs ustawia
//    tryb gen9 raz na zawsze (pomiar 16.08).
//  - CodeWalker dekoduje BC1/BC2/BC3/BC4/BC5 i formaty nieskompresowane, ale NIE BC7
//    (`case BC7: //TODO`, zwraca null). BC7 (ok. 5% tekstur w paczkach z internetu) dekodujemy
//    przez BCnEncoder.Net — Tekstury.cs (od 16.08); wczesniej takie tekstury nie mialy odcisku.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using Duble.Core.Formats;
using Duble.Core.Model;

namespace Duble.Core.Fingerprints;

public static class Odciski
{
    // ===================== GEOMETRIA =====================

    /// <summary>Odcisk modelu: liczby + histogram ksztaltu + hash pozycji.</summary>
    public static Geo Geometria(Drawable d)
    {
        var g = new Geo();
        if (d == null) return g;

        var dm = d.DrawableModels;
        if (dm != null)
        {
            foreach (var arr in new[] { dm.High, dm.Med, dm.Low, dm.VLow })
                if (arr != null && arr.Length > 0) g.Lody++;
        }
        g.Kosci = d.Skeleton?.Bones?.Items?.Length ?? 0;
        g.Bbox = new[]
        {
            d.BoundingBoxMax.X - d.BoundingBoxMin.X,
            d.BoundingBoxMax.Y - d.BoundingBoxMin.Y,
            d.BoundingBoxMax.Z - d.BoundingBoxMin.Z
        };

        // Odcisk liczymy WYLACZNIE z najwyzszego LOD — nizsze bywaja generowane
        // automatycznie przez rozne narzedzia i roznia sie tam, gdzie ciuch jest ten sam.
        var modele = dm?.High;
        if (modele == null || modele.Length == 0) return g;

        var poz = new List<(float x, float y, float z)>();
        foreach (var m in modele)
        {
            if (m?.Geometries == null) continue;
            foreach (var geo in m.Geometries)
            {
                if (geo == null) continue;
                g.Geometrie++;
                g.Trojkaty += (int)(geo.IndicesCount / 3);
                var vd = geo.VertexBuffer?.Data1 ?? geo.VertexBuffer?.Data2;
                if (vd?.VertexBytes == null || vd.Info == null) continue;
                g.Stride = vd.Info.Stride;
                int stride = vd.Info.Stride;
                int off = vd.Info.GetComponentOffset(0);   // skladowa 0 = pozycja
                int n = vd.VertexCount;
                g.Wierzcholki += n;
                var b = vd.VertexBytes;
                for (int v = 0; v < n; v++)
                {
                    int o = v * stride + off;
                    if (o + 12 > b.Length) break;
                    poz.Add((BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8)));
                }
            }
        }
        if (poz.Count == 0) return g;

        // --- histogram odleglosci od srodka ciezkosci ---
        double sx = 0, sy = 0, sz = 0;
        foreach (var p in poz) { sx += p.x; sy += p.y; sz += p.z; }
        double cx = sx / poz.Count, cy = sy / poz.Count, cz = sz / poz.Count;

        var odl = new double[poz.Count];
        double suma = 0;
        for (int i = 0; i < poz.Count; i++)
        {
            double dx = poz[i].x - cx, dy = poz[i].y - cy, dz = poz[i].z - cz;
            odl[i] = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            suma += odl[i];
        }
        double srednia = suma / poz.Count;
        var hist = new float[Geo.Kubelki];
        if (srednia > 1e-9)
        {
            foreach (var o in odl)
            {
                int k = (int)(o / srednia / Geo.ZakresHist * Geo.Kubelki);
                if (k < 0) k = 0; else if (k >= Geo.Kubelki) k = Geo.Kubelki - 1;
                hist[k]++;
            }
            for (int i = 0; i < Geo.Kubelki; i++) hist[i] /= poz.Count;
        }
        g.Hist = hist;

        // --- hash z posortowanych pozycji zaokraglonych do 1 mm ---
        // 1 mm, nie 0,1 mm: ponowny eksport przez Blendera/Maxa wnosi szum wiekszy niz
        // 0,1 mm, a ciuch ma ~0,5 m, wiec 1 mm to nadal 0,2% rozmiaru — bardzo selektywne.
        var klucze = new long[poz.Count];
        for (int i = 0; i < poz.Count; i++)
        {
            long qx = Zaokraglij(poz[i].x), qy = Zaokraglij(poz[i].y), qz = Zaokraglij(poz[i].z);
            klucze[i] = (qx << 32) | (qy << 16) | qz;
        }
        Array.Sort(klucze);
        var bajty = new byte[klucze.Length * 8];
        Buffer.BlockCopy(klucze, 0, bajty, 0, bajty.Length);
        g.HashPozycji = Convert.ToHexString(SHA256.HashData(bajty)).Substring(0, 32);
        return g;
    }

    static long Zaokraglij(float v)
    {
        long mm = (long)Math.Round(v * 1000.0);
        if (mm < -32768) mm = -32768; else if (mm > 32767) mm = 32767;
        return mm + 32768;   // przesuniecie na zakres bez znaku, zeby zmiescic w 16 bitach
    }

    /// <summary>Odleglosc L1 miedzy histogramami ksztaltu. 0 = identyczne, max 2.</summary>
    public static double OdlegloscGeo(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return double.MaxValue;
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += Math.Abs(a[i] - b[i]);
        return s;
    }

    /// <summary>Najwieksza wzgledna roznica wymiarow pudelka. 0 = ten sam rozmiar.</summary>
    public static double OdlegloscBbox(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != 3 || b.Length != 3) return double.MaxValue;
        double max = 0;
        for (int i = 0; i < 3; i++)
        {
            double m = Math.Max(Math.Abs(a[i]), Math.Abs(b[i]));
            if (m < 1e-6) continue;
            max = Math.Max(max, Math.Abs(a[i] - b[i]) / m);
        }
        return max;
    }

    // ===================== TEKSTURY =====================

    static readonly double[,] Cos = ZbudujCos();
    const int NPh = 64;    // bok obrazu wejsciowego DCT
    const int KPh = 16;    // bok bloku wspolczynnikow (16x16 = 256 bitow)
    const int BokKoloru = 8;   // siatka sygnatury koloru (8x8 x RGB = 192 bajty)

    static double[,] ZbudujCos()
    {
        var t = new double[KPh, NPh];
        for (int u = 0; u < KPh; u++)
            for (int x = 0; x < NPh; x++)
                t[u, x] = Math.Cos((2.0 * x + 1.0) * u * Math.PI / (2.0 * NPh));
        return t;
    }

    /// <summary>Krotka nazwa formatu do raportu.</summary>
    public static string NazwaFormatu(Texture t)
    {
        var f = t.Format.ToString();
        return f switch
        {
            "D3DFMT_DXT1" => "BC1",
            "D3DFMT_DXT3" => "BC2",
            "D3DFMT_DXT5" => "BC3",
            "D3DFMT_ATI1" => "BC4",
            "D3DFMT_ATI2" => "BC5",
            "D3DFMT_BC7" => "BC7",
            "D3DFMT_A8R8G8B8" => "RGBA8",
            "D3DFMT_A8B8G8R8" => "RGBA8",
            _ => f.Replace("D3DFMT_", "")
        };
    }

    /// <summary>
    /// Dekoduje teksture i wypelnia odcisk: PHash, sygnature koloru, udzial alfy.
    /// Zwraca piksele RGB miniatury (do raportu) albo null, gdy sie nie udalo.
    /// </summary>
    public static byte[] Tekstura(Texture t, Tekstura wy, int bokMiniatury = 128, Action<byte[], int, int> piksele = null)
    {
        wy.Nazwa = t.Name;
        wy.W = t.Width;
        wy.H = t.Height;
        wy.Mipy = t.Levels;
        wy.Format = NazwaFormatu(t);
        wy.Zdekodowana = false;

        var px = Dekoduj(t, out int w, out int h);
        if (px == null) return null;
        piksele?.Invoke(px, w, h);   // np. miniatura do cache przy indeksowaniu (bez drugiego dekodowania)

        // --- PHash: 64x64 w skali szarosci -> DCT -> blok 16x16 -> mediana ---
        // DCT liczymy ROZDZIELNIE (najpierw wiersze, potem kolumny): 82 tys. mnozen
        // zamiast miliona przy wersji naiwnej, przy identycznym wyniku.
        var szare = SkalujSzarosc(px, w, h, NPh, NPh);

        double sr = 0;
        foreach (var s0 in szare) sr += s0;
        sr /= szare.Length;
        double war = 0;
        foreach (var s0 in szare) war += (s0 - sr) * (s0 - sr);
        wy.Wariancja = (float)Math.Sqrt(war / szare.Length);

        var posrednie = new double[NPh * KPh];         // [x, v]
        for (int x = 0; x < NPh; x++)
        {
            int wiersz = x * NPh;
            for (int v = 0; v < KPh; v++)
            {
                double s = 0;
                for (int y = 0; y < NPh; y++) s += szare[wiersz + y] * Cos[v, y];
                posrednie[x * KPh + v] = s;
            }
        }
        var wsp = new double[KPh * KPh];
        for (int u = 0; u < KPh; u++)
            for (int v = 0; v < KPh; v++)
            {
                double s = 0;
                for (int x = 0; x < NPh; x++) s += posrednie[x * KPh + v] * Cos[u, x];
                wsp[u * KPh + v] = s;
            }

        // mediana liczona BEZ skladowej stalej (0,0) — inaczej zdominowalaby prog
        var bezDc = wsp.Skip(1).OrderBy(x => x).ToArray();
        double mediana = (bezDc[bezDc.Length / 2 - 1] + bezDc[bezDc.Length / 2]) / 2.0;
        var hash = new ulong[4];
        for (int i = 0; i < 256; i++) if (wsp[i] > mediana) hash[i >> 6] |= 1UL << (i & 63);
        wy.PHash = hash;

        // --- sygnatura koloru 8x8 RGB ---
        var maly = SkalujRgb(px, w, h, BokKoloru, BokKoloru);
        wy.Kolor = Convert.ToBase64String(maly);

        // --- udzial pikseli z alfa ---
        int zAlfa = 0, wszystkie = w * h;
        for (int i = 3; i < px.Length; i += 4) if (px[i] < 250) zAlfa++;
        wy.Alfa = wszystkie > 0 ? (float)zAlfa / wszystkie : 0f;

        wy.Zdekodowana = true;
        return SkalujRgb(px, w, h, bokMiniatury, bokMiniatury);
    }

    /// <summary>
    /// Miniatura do raportu: RGB z ALFA ZLOZONA NA SZACHOWNICE.
    ///
    /// Bez tego polowa tekstur ubran wychodzi czarna — atlas ma wielkie obszary
    /// przezroczyste, a pod nimi zwykle leza czarne piksele. Odciski licza sie dalej
    /// z surowego RGB (sa juz skalibrowane), skladanie dotyczy WYLACZNIE podgladu.
    /// </summary>
    public static byte[] Miniatura(Texture t, int bok)
    {
        var px = Dekoduj(t, out int w, out int h);
        return px == null ? null : MiniaturaZPikseli(px, w, h, bok);
    }

    /// <summary>Jak Miniatura, ale z gotowych pikseli BGRA (uzywane przy indeksowaniu — dekodujemy raz).</summary>
    public static byte[] MiniaturaZPikseli(byte[] px, int w, int h, int bok)
    {
        var rgba = SkalujRgba(px, w, h, bok, bok);
        var wy = new byte[bok * bok * 3];
        for (int y = 0; y < bok; y++)
            for (int x = 0; x < bok; x++)
            {
                int o = (y * bok + x) * 4;
                double a = rgba[o + 3] / 255.0;
                // szachownica 8 px, w ciepłej szarosci — zgodna z paleta raportu
                bool jasne = ((x >> 3) + (y >> 3)) % 2 == 0;
                byte t1 = (byte)(jasne ? 0xD6 : 0xB4), t2 = (byte)(jasne ? 0xD2 : 0xB0), t3 = (byte)(jasne ? 0xCA : 0xA8);
                int c = (y * bok + x) * 3;
                wy[c] = (byte)(rgba[o] * a + t1 * (1 - a));
                wy[c + 1] = (byte)(rgba[o + 1] * a + t2 * (1 - a));
                wy[c + 2] = (byte)(rgba[o + 2] * a + t3 * (1 - a));
            }
        return wy;
    }

    /// <summary>Usrednianie po prostokatach z BGRA do RGBA (z zachowaniem alfy).</summary>
    static byte[] SkalujRgba(byte[] px, int w, int h, int tw, int th)
    {
        var wy = new byte[tw * th * 4];
        for (int y = 0; y < th; y++)
        {
            int y0 = y * h / th, y1 = Math.Max(y0 + 1, (y + 1) * h / th);
            for (int x = 0; x < tw; x++)
            {
                int x0 = x * w / tw, x1 = Math.Max(x0 + 1, (x + 1) * w / tw);
                long r = 0, g = 0, b = 0, a = 0; int n = 0;
                for (int yy = y0; yy < y1; yy++)
                    for (int xx = x0; xx < x1; xx++)
                    {
                        int o = (yy * w + xx) * 4;
                        b += px[o]; g += px[o + 1]; r += px[o + 2]; a += px[o + 3];
                        n++;
                    }
                int c = (y * tw + x) * 4;
                wy[c] = (byte)(r / n); wy[c + 1] = (byte)(g / n); wy[c + 2] = (byte)(b / n); wy[c + 3] = (byte)(a / n);
            }
        }
        return wy;
    }

    /// <summary>
    /// Dekoduje teksture do BGRA. Wybiera NAJMNIEJSZY mip o boku >= 128 — dzieki temu
    /// dla 1024^2 z pelnym lancuchem mipow dekodujemy 128^2 zamiast 1024^2 (64x taniej),
    /// a jakosc odcisku jest ta sama. Skalowanie do 32x32 robimy ZAWSZE sami, tym samym
    /// filtrem, zeby tekstury z mipami i bez mipow dawaly porownywalne odciski.
    /// </summary>
    static byte[] Dekoduj(Texture t, out int w, out int h)
    {
        w = h = 0;
        if (t == null || t.Width <= 0 || t.Height <= 0) return null;
        int mip = 0;
        for (int m = 0; m < Math.Max(1, (int)t.Levels); m++)
        {
            int mw = Math.Max(1, t.Width >> m), mh = Math.Max(1, t.Height >> m);
            if (mw >= 128 && mh >= 128) mip = m; else break;
        }
        for (; mip >= 0; mip--)   // przy bledzie schodzimy na coraz wiekszy mip, az do 0
        {
            try
            {
                var px = Tekstury.Piksele(t, mip, out int mw, out int mh);   // DDSIO + BC7 (BCnEncoder.Net)
                if (px == null) return null;                                 // format bez dekodera
                w = mw; h = mh;
                return px;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Usrednianie po prostokatach (box filter) z BGRA do RGB.</summary>
    static byte[] SkalujRgb(byte[] px, int w, int h, int tw, int th)
    {
        var wy = new byte[tw * th * 3];
        for (int y = 0; y < th; y++)
        {
            int y0 = y * h / th, y1 = Math.Max(y0 + 1, (y + 1) * h / th);
            for (int x = 0; x < tw; x++)
            {
                int x0 = x * w / tw, x1 = Math.Max(x0 + 1, (x + 1) * w / tw);
                long r = 0, g = 0, b = 0; int n = 0;
                for (int yy = y0; yy < y1; yy++)
                    for (int xx = x0; xx < x1; xx++)
                    {
                        int o = (yy * w + xx) * 4;
                        b += px[o]; g += px[o + 1]; r += px[o + 2];   // DDSIO oddaje BGRA
                        n++;
                    }
                int c = (y * tw + x) * 3;
                wy[c] = (byte)(r / n); wy[c + 1] = (byte)(g / n); wy[c + 2] = (byte)(b / n);
            }
        }
        return wy;
    }

    static double[] SkalujSzarosc(byte[] px, int w, int h, int tw, int th)
    {
        var rgb = SkalujRgb(px, w, h, tw, th);
        var wy = new double[tw * th];
        for (int i = 0; i < wy.Length; i++)
            wy[i] = 0.299 * rgb[i * 3] + 0.587 * rgb[i * 3 + 1] + 0.114 * rgb[i * 3 + 2];
        return wy;
    }

    /// <summary>Odleglosc Hamminga miedzy odciskami 256-bitowymi. -1 = brak ktoregos odcisku.</summary>
    public static int Hamming(ulong[] a, ulong[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return -1;
        int s = 0;
        for (int i = 0; i < a.Length; i++) s += System.Numerics.BitOperations.PopCount(a[i] ^ b[i]);
        return s;
    }

    /// <summary>Srednia roznica sygnatur koloru w kanale (0..255).</summary>
    public static double OdlegloscKoloru(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return double.MaxValue;
        var x = Convert.FromBase64String(a);
        var y = Convert.FromBase64String(b);
        if (x.Length != y.Length) return double.MaxValue;
        double s = 0;
        for (int i = 0; i < x.Length; i++) s += Math.Abs(x[i] - y[i]);
        return s / x.Length;
    }
}
