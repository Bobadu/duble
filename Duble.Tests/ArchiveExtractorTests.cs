using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

/// <summary>
/// Unpacking an archive into a plain folder: RSC7 files with their headers, nested .rpf archives as
/// subfolders, and — the point of the whole thing — a copy that indexes to exactly what the archive did.
/// </summary>
public class ArchiveExtractorTests
{
    static readonly IServiceProvider Services = new ServiceCollection().AddDubleCore().BuildServiceProvider();
    static readonly IArchiveExtractor Extractor = Services.GetRequiredService<IArchiveExtractor>();

    static IReadOnlyList<Garment> Index(string source, string name)
        => Services.GetRequiredService<IGarmentIndexer>().Index(source, name, new IndexOptions()).Value.Garments;

    readonly ITestOutputHelper output;

    public ArchiveExtractorTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void An_unpacked_archive_indexes_to_the_same_garments_as_the_archive_did()
    {
        if (!TestPaths.HasGame) { output.WriteLine("SKIPPED: no studio_body\\dlc.rpf"); return; }

        var temp = TestPaths.Temp("unpack");
        try
        {
            var progress = new List<ProgressReport>();
            var result = Extractor.ExtractArchive(TestPaths.Dlc("studio_body"), Path.Combine(temp, "studio_body"),
                                                  new SyncProgress<ProgressReport>(progress.Add));

            output.WriteLine($"files={result.Files} archives={result.Archives} bytes={result.Bytes} errors={result.Errors.Count}");
            foreach (var error in result.Errors.Take(5)) output.WriteLine("  " + error);
            Assert.Empty(result.Errors);
            Assert.True(result.Archives >= 2);                          // dlc.rpf and the body.rpf nested in it
            Assert.Contains(progress, report => report.Stage == "unpack" && report.Total > 0);

            var models = Directory.GetFiles(temp, "*.ydd", SearchOption.AllDirectories);
            Assert.NotEmpty(models);
            // a nested archive becomes a folder named *.rpf, which is what indexing treats as a container
            Assert.All(models, file => Assert.Contains(".rpf\\", Path.GetRelativePath(temp, file)));

            var uppr = models.First(file => Path.GetFileName(file).StartsWith("uppr_015"));
            var bytes = File.ReadAllBytes(uppr);
            Assert.True(Rsc7Header.IsRsc7(bytes));
            Assert.Equal(159, Rsc7Header.Version(bytes));

            // binary files (.meta, .xml…) come out as they went in
            Assert.Contains(Directory.GetFiles(temp, "*", SearchOption.AllDirectories),
                            file => !file.EndsWith(".ydd") && !file.EndsWith(".ytd"));

            var fromArchive = Index(TestPaths.Dlc("studio_body"), "x");
            var fromCopy = Index(Path.Combine(temp, "studio_body"), "x");
            Assert.Equal(fromArchive.Count, fromCopy.Count);

            string Fingerprints(IReadOnlyList<Garment> garments) => string.Join("\n", garments.OrderBy(g => g.Id)
                .Select(g => g.Geometry.PositionHash + "|" + string.Join(",", g.Textures.Select(t => t.PerceptualHash?[0]))));
            Assert.Equal(Fingerprints(fromArchive), Fingerprints(fromCopy));

            // and the copy is loose files, which is what makes apply and undo possible at all
            Assert.All(fromCopy, garment => Assert.DoesNotContain("|", garment.ModelPath));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void Unpacking_a_whole_source_copies_the_loose_files_and_skips_the_bin()
    {
        if (!TestPaths.HasGame) { output.WriteLine("SKIPPED: no studio_body\\dlc.rpf"); return; }

        var temp = TestPaths.Temp("unpack-source");
        try
        {
            var source = Path.Combine(temp, "src", "stream");
            Directory.CreateDirectory(source);
            File.Copy(TestPaths.Dlc("studio_body"), Path.Combine(source, "paczka.rpf"));
            File.WriteAllText(Path.Combine(source, "x.meta"), "<meta/>");

            Directory.CreateDirectory(Path.Combine(temp, "src", "_rejected"));
            File.WriteAllText(Path.Combine(temp, "src", "_rejected", "a.ydd"), "x");

            var result = Extractor.ExtractSource(Path.Combine(temp, "src"), Path.Combine(temp, "copy"));

            Assert.Empty(result.Errors);
            Assert.True(File.Exists(Path.Combine(temp, "copy", "stream", "x.meta")));
            Assert.True(Directory.Exists(Path.Combine(temp, "copy", "stream", "paczka.rpf")));
            Assert.False(Directory.Exists(Path.Combine(temp, "copy", "_rejected")));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void A_resource_entry_gets_its_header_back_and_a_binary_one_is_untouched()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };

        var binary = new RpfBinaryFileEntry { Name = "a.meta" };
        Assert.Same(data, RpfArchiveExtractor.ToRsc7File(binary, data));

        var resource = new RpfResourceFileEntry { Name = "a.ydd", SystemFlags = 0x90000000u, GraphicsFlags = 0xF0000000u };
        var wrapped = RpfArchiveExtractor.ToRsc7File(resource, data);
        Assert.True(Rsc7Header.IsRsc7(wrapped));
        Assert.Equal(resource.Version, Rsc7Header.Version(wrapped));
        Assert.Equal(data, ResourceBuilder.Decompress(wrapped.Skip(16).ToArray()));

        Assert.Null(RpfArchiveExtractor.ToRsc7File(resource, null));
    }
}
