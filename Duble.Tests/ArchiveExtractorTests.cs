using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

/// <summary>RpfArchiveExtractor archiwum do folderu: pliki RSC7 z naglowkiem, zagniezdzone .rpf jako podfoldery, indeks kopii = indeks archiwum (pomijane bez gry).</summary>
public class ArchiveExtractorTests
{
    static readonly IServiceProvider CoreUslugi = new ServiceCollection().AddDubleCore().BuildServiceProvider();
    static IReadOnlyList<Garment> Indeksuj(string zrodlo, string nazwa, IndexOptions opcje = null)
        => CoreUslugi.GetRequiredService<IGarmentIndexer>().Index(zrodlo, nazwa, opcje ?? new IndexOptions()).Value.Garments;

    readonly ITestOutputHelper wyj;
    public ArchiveExtractorTests(ITestOutputHelper wyj) { this.wyj = wyj; }

    [Fact]
    public void Archiwum_dlc_rozklada_sie_na_foldery_z_plikami_rsc7()
    {
        if (!Sciezki.JestGra) { wyj.WriteLine("POMINIETY: brak studio_body\\dlc.rpf"); return; }
        var tmp = Sciezki.Tymczasowy("rozpakuj");
        try
        {
            var postepy = new System.Collections.Generic.List<ProgressReport>();
            var w = RpfArchiveExtractor.Archiwum(Sciezki.Dlc("studio_body"), Path.Combine(tmp, "studio_body"), postepy.Add);
            wyj.WriteLine($"pliki={w.Files} archiwa={w.Archiwa} bajty={w.Bytes} bledy={w.Bledy.Count}");
            foreach (var b in w.Bledy.Take(5)) wyj.WriteLine("  " + b);
            Assert.Empty(w.Bledy);
            Assert.True(w.Archiwa >= 2);                                   // dlc.rpf + zagniezdzone body.rpf
            Assert.Contains(postepy, p => p.Stage == "rozpakuj" && p.Total > 0);
            var ydd = Directory.GetFiles(tmp, "*.ydd", SearchOption.AllDirectories);
            foreach (var f in ydd.Take(3)) wyj.WriteLine("  " + Path.GetRelativePath(tmp, f));
            Assert.NotEmpty(ydd);
            Assert.All(ydd, f => Assert.Contains(".rpf\\", Path.GetRelativePath(tmp, f)));   // zagniezdzone archiwum = folder *.rpf (kontener); w srodku moga byc podfoldery (mp_f_freemode_01)
            var uppr = ydd.First(f => Path.GetFileName(f).StartsWith("uppr_015"));
            var bajty = File.ReadAllBytes(uppr);
            Assert.True(Rsc7Header.JestRsc7(bajty)); Assert.Equal(159, Rsc7Header.Wersja(bajty));
            Assert.True(bajty.Length < new FileInfo(uppr).Length + 1);      // (sanity) plik na dysku = to, co zapisalismy
            // meta/xml binarne tez sa
            Assert.Contains(Directory.GetFiles(tmp, "*", SearchOption.AllDirectories), f => !f.EndsWith(".ydd") && !f.EndsWith(".ytd"));

            // indeks kopii daje te same pozycje (odciski geometrii) co indeks archiwum
            var zArch = Indeksuj(Sciezki.Dlc("studio_body"), "x");
            var zKopii = Indeksuj(Path.Combine(tmp, "studio_body"), "x");
            Assert.Equal(zArch.Count, zKopii.Count);
            var a = zArch.OrderBy(p => p.Id).Select(p => p.Geometry.PositionHash + "|" + string.Join(",", p.Textures.Select(t => t.PerceptualHash?[0]))).ToList();
            var b2 = zKopii.OrderBy(p => p.Id).Select(p => p.Geometry.PositionHash + "|" + string.Join(",", p.Textures.Select(t => t.PerceptualHash?[0]))).ToList();
            Assert.Equal(a, b2);
            Assert.All(zKopii, p => Assert.DoesNotContain("|", p.ModelPath));   // luzne pliki -> przenoszalne

            // Zrodlo() na folderze z archiwum w srodku: kopia + rozlozone archiwum
            var src = Path.Combine(tmp, "src", "stream"); Directory.CreateDirectory(src);
            File.Copy(Sciezki.Dlc("studio_body"), Path.Combine(src, "paczka.rpf"));
            File.WriteAllText(Path.Combine(src, "x.meta"), "<meta/>");
            Directory.CreateDirectory(Path.Combine(tmp, "src", "_odrzucone")); File.WriteAllText(Path.Combine(tmp, "src", "_odrzucone", "a.ydd"), "x");
            var w2 = RpfArchiveExtractor.SourceName(Path.Combine(tmp, "src"), Path.Combine(tmp, "kopia"));
            Assert.Empty(w2.Bledy);
            Assert.True(File.Exists(Path.Combine(tmp, "kopia", "stream", "x.meta")));
            Assert.True(Directory.Exists(Path.Combine(tmp, "kopia", "stream", "paczka.rpf")));
            Assert.False(Directory.Exists(Path.Combine(tmp, "kopia", "_odrzucone")));
            Assert.Equal(w.Files + 1, w2.Files);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void PlikRsc7_binarny_bez_zmian_zasob_z_naglowkiem()
    {
        var dane = new byte[] { 1, 2, 3, 4, 5 };
        var bin = new RpfBinaryFileEntry { Name = "a.meta" };
        Assert.Same(dane, RpfArchiveExtractor.PlikRsc7(bin, dane));
        var res = new RpfResourceFileEntry { Name = "a.ydd", SystemFlags = 0x90000000u, GraphicsFlags = 0xF0000000u };
        var wy = RpfArchiveExtractor.PlikRsc7(res, dane);
        Assert.True(Rsc7Header.JestRsc7(wy));
        Assert.Equal(res.Version, Rsc7Header.Wersja(wy));
        Assert.Equal(dane, ResourceBuilder.Decompress(wy.Skip(16).ToArray()));
        Assert.Null(RpfArchiveExtractor.PlikRsc7(res, null));
    }
}
