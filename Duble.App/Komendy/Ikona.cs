// Komendy/Ikona.cs — generator ikony aplikacji (.ico) z tego samego rysunku co logo w UI (ui\assets\logo.svg):
// dwa nachodzace zaokraglone kafelki (koral) z wycieciem "D" na grafitowym tle. Uzycie: Duble.exe --dev-icon <plik.ico>
// (raz, wynik zakomitowany jako assets\duble.ico). ICO z wpisami PNG (Vista+), rozmiary 16..256.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Duble.App.Komendy;

public static class Ikona
{
    static readonly int[] Rozmiary = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    public static void Zapisz(string plik)
    {
        var pngi = new List<byte[]>();
        foreach (var r in Rozmiary) pngi.Add(Png(r));
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)Rozmiary.Length);
        int offset = 6 + 16 * Rozmiary.Length;
        for (int i = 0; i < Rozmiary.Length; i++)
        {
            int r = Rozmiary[i];
            bw.Write((byte)(r >= 256 ? 0 : r)); bw.Write((byte)(r >= 256 ? 0 : r)); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((ushort)1); bw.Write((ushort)32); bw.Write(pngi[i].Length); bw.Write(offset);
            offset += pngi[i].Length;
        }
        foreach (var p in pngi) bw.Write(p);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(plik)));
        File.WriteAllBytes(plik, ms.ToArray());
    }

    /// <summary>PNG o boku r: tlo grafit z zaokragleniem 22 %, kafelki koral (viewBox 64 jak w logo.svg).</summary>
    public static byte[] Png(int r)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            double s = r / 64.0;
            var grafit = new SolidColorBrush(Color.FromRgb(0x1B, 0x1A, 0x1F));
            var koral = new SolidColorBrush(Color.FromRgb(0xFF, 0x6F, 0x61));
            var koralJasny = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0x6F, 0x61));   // 55 %
            dc.DrawRoundedRectangle(grafit, null, new Rect(0, 0, r, r), r * 0.22, r * 0.22);
            dc.DrawRoundedRectangle(koralJasny, null, new Rect(8 * s, 8 * s, 34 * s, 34 * s), 9 * s, 9 * s);
            dc.DrawRoundedRectangle(koral, null, new Rect(22 * s, 22 * s, 34 * s, 34 * s), 9 * s, 9 * s);
            // wyciecie "D": prostokat + polkole (jak path M33 31 h7.5 a8 8 0 0 1 0 16 H33 z)
            var d = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(33 * s, 31 * s), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(40.5 * s, 31 * s), true));
            fig.Segments.Add(new ArcSegment(new Point(40.5 * s, 47 * s), new Size(8 * s, 8 * s), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(new Point(33 * s, 47 * s), true));
            d.Figures.Add(fig);
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0xE6, 0x1B, 0x1A, 0x1F)), null, d);
        }
        var rtb = new RenderTargetBitmap(r, r, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
