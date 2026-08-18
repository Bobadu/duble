// IconGenerator.cs — the application icon (.ico) drawn from the same shape as the logo in the interface
// (ui\assets\logo.svg): two overlapping rounded tiles in coral, with a "D" cut out of the graphite background.
//
// Run once with `Duble.exe --dev-icon <file.ico>`; the result is committed as assets\duble.ico. The file is an
// ICO of PNG entries (Vista and later), sizes 16 to 256.
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Duble.App;

public static class IconGenerator
{
    static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    public static void Write(string file)
    {
        var images = new List<byte[]>();
        foreach (var size in Sizes) images.Add(Draw(size));

        using var ico = new MemoryStream();
        var writer = new BinaryWriter(ico);
        writer.Write((ushort)0);                    // reserved
        writer.Write((ushort)1);                    // type: icon
        writer.Write((ushort)Sizes.Length);

        int offset = 6 + 16 * Sizes.Length;          // header plus one 16-byte directory entry per size
        for (int i = 0; i < Sizes.Length; i++)
        {
            int size = Sizes[i];
            writer.Write((byte)(size >= 256 ? 0 : size));   // 256 is written as 0
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);                   // colours in the palette: none, this is true colour
            writer.Write((byte)0);                   // reserved
            writer.Write((ushort)1);                 // colour planes
            writer.Write((ushort)32);                // bits per pixel
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images) writer.Write(image);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllBytes(file, ico.ToArray());
    }

    /// <summary>One square PNG: a graphite tile rounded by 22 %, the two coral tiles of the logo on top.</summary>
    static byte[] Draw(int size)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            double scale = size / 64.0;             // the logo is drawn in a viewBox of 64, as in logo.svg
            var graphite = new SolidColorBrush(Color.FromRgb(0x1B, 0x1A, 0x1F));
            var coral = new SolidColorBrush(Color.FromRgb(0xFF, 0x6F, 0x61));
            var palerCoral = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0x6F, 0x61));   // 55 %

            context.DrawRoundedRectangle(graphite, null, new Rect(0, 0, size, size), size * 0.22, size * 0.22);
            context.DrawRoundedRectangle(palerCoral, null, new Rect(8 * scale, 8 * scale, 34 * scale, 34 * scale), 9 * scale, 9 * scale);
            context.DrawRoundedRectangle(coral, null, new Rect(22 * scale, 22 * scale, 34 * scale, 34 * scale), 9 * scale, 9 * scale);

            // the "D": a rectangle closed by a half circle, the same as the path M33 31 h7.5 a8 8 0 0 1 0 16 H33 z
            var d = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(33 * scale, 31 * scale), IsClosed = true };
            figure.Segments.Add(new LineSegment(new Point(40.5 * scale, 31 * scale), true));
            figure.Segments.Add(new ArcSegment(new Point(40.5 * scale, 47 * scale), new Size(8 * scale, 8 * scale), 0, false, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(new Point(33 * scale, 47 * scale), true));
            d.Figures.Add(figure);
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(0xE6, 0x1B, 0x1A, 0x1F)), null, d);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);
        return png.ToArray();
    }
}
