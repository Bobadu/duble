using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// Applying and undoing, with real files on disk: what the plan says will happen to each file, the move into
/// the bin, the log that undoes it, undoing one garment or all of them, and cancelling half-way.
/// </summary>
public class ApplyTests
{
    static readonly IUndoStore Store = new JsonUndoStore();
    static readonly IApplyPlanner Planner = new ApplyPlanner();
    static readonly IApplyExecutor Executor = new ApplyExecutor(new SystemClock());

    /// <summary>A garment with files on disk: &lt;source&gt;\&lt;container&gt;\&lt;slot&gt;_NNN_u.ydd and its textures.</summary>
    static Garment Make(string source, string container, string slot, int number, params string[] letters)
    {
        var folder = Path.Combine(source, container);
        Directory.CreateDirectory(folder);

        var model = Path.Combine(folder, $"{slot}_{number:d3}_u.ydd");
        File.WriteAllBytes(model, new byte[100]);

        var garment = new Garment
        {
            Id = $"z1|{container}|{slot}|{number}|u",
            PackName = "z1", Container = container, Slot = slot, Number = number, Suffix = "u",
            SourceId = "id1", ModelPath = model, ModelSize = 100,
        };

        foreach (var letter in letters)
        {
            var file = Path.Combine(folder, $"{slot}_diff_{number:d3}_{letter}_uni.ytd");
            if (!File.Exists(file)) File.WriteAllBytes(file, new byte[50]);
            garment.Textures.Add(new TextureInfo
            {
                FileName = Path.GetFileName(file), Path = file, Sha256 = "S" + slot + number + letter, Size = 50,
            });
        }

        return garment;
    }

    /// <summary>
    /// A catalog covering every case an apply has to tell apart: an ordinary rejection, a texture shared with a
    /// garment that stays, a garment still inside an archive, and one whose model has vanished from disk.
    /// </summary>
    static (string Temp, string Source, string Bin, Catalog Catalog, Func<Garment, BinTarget> Target) World()
    {
        var temp = Sciezki.Tymczasowy("apply");
        var source = Path.Combine(temp, "z1");
        Directory.CreateDirectory(source);
        var bin = Path.Combine(temp, "_odrzucone", "z1");

        var stays = Make(source, "k.rpf", "jbib", 1, "a", "b");
        var rejected = Make(source, "k.rpf", "jbib", 7, "a");

        // two garments under one number: feet_050 stays, feet_050_1 goes, and they share a texture
        var keptBoot = Make(source, "k.rpf", "feet", 50, "a");
        var rejectedBoot = Make(source, "k.rpf", "feet", 50, "a");
        rejectedBoot.Id = "z1|k.rpf|feet|50|u_1";
        rejectedBoot.Suffix = "u_1";
        rejectedBoot.ModelPath = Path.Combine(source, "k.rpf", "feet_050_u_1.ydd");
        File.WriteAllBytes(rejectedBoot.ModelPath, new byte[100]);

        var inArchive = new Garment
        {
            Id = "z1|x.rpf|hair|3|u",
            PackName = "z1", Container = "x.rpf", Slot = "hair", Number = 3, Suffix = "u", SourceId = "id1",
            ModelPath = Path.Combine(source, "x.rpf") + "|x.rpf\\hair_003_u.ydd", ModelSize = 10,
        };

        var missing = Make(source, "k.rpf", "lowr", 9, "a");
        File.Delete(missing.ModelPath);

        var catalog = new Catalog();
        catalog.Upsert(new[] { stays, rejected, keptBoot, rejectedBoot, inArchive, missing });

        Func<Garment, BinTarget> target = _ => new BinTarget
        {
            Root = source, BinFolder = bin, SourceName = "z1", SourceId = "id1",
        };
        return (temp, source, bin, catalog, target);
    }

    [Fact]
    public void The_plan_tells_moved_shared_in_archive_and_missing_apart()
    {
        var (temp, _, bin, catalog, target) = World();
        try
        {
            var plan = Planner.Plan(catalog, new[]
            {
                "z1|k.rpf|jbib|7|u", "z1|k.rpf|feet|50|u_1", "z1|x.rpf|hair|3|u", "z1|k.rpf|lowr|9|u", "no-such-id",
            }, target);

            Assert.Equal(4, plan.Garments.Count);

            var rejected = plan.Garments.Single(g => g.Id == "z1|k.rpf|jbib|7|u");
            Assert.Equal(2, rejected.MoveCount);
            Assert.Equal(150, rejected.Bytes);
            Assert.All(rejected.Files, file => Assert.StartsWith(Path.Combine(bin, "k.rpf"), file.To));

            var boot = plan.Garments.Single(g => g.Id == "z1|k.rpf|feet|50|u_1");
            Assert.Equal(1, boot.MoveCount);
            Assert.Equal(1, boot.SharedCount);      // the model goes, the shared texture stays

            var inArchive = plan.Garments.Single(g => g.Id == "z1|x.rpf|hair|3|u");
            Assert.Equal(1, inArchive.InArchiveCount);
            Assert.Equal(0, inArchive.MoveCount);

            var missing = plan.Garments.Single(g => g.Id == "z1|k.rpf|lowr|9|u");
            Assert.Equal(1, missing.MissingCount);  // the model is gone
            Assert.Equal(1, missing.MoveCount);     // the texture is still there

            Assert.Equal(4, plan.Files);
            Assert.Equal(1, plan.SharedCount);
            Assert.Equal(1, plan.InArchiveCount);
            Assert.Equal(1, plan.MissingCount);

            var bins = plan.BinTotals().ToList();
            Assert.Single(bins);
            Assert.Equal(bin, bins[0].BinFolder);
            Assert.Equal(4, bins[0].Files);
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void A_source_that_is_gone_makes_every_one_of_its_files_missing()
    {
        var (temp, _, _, catalog, target) = World();
        try
        {
            var plan = Planner.Plan(catalog, new[] { "z1|k.rpf|jbib|7|u" }, _ => null);
            Assert.Equal(2, plan.Garments[0].MissingCount);
            Assert.Equal(new[] { "z1" }, plan.MissingSources);

            Assert.Empty(Planner.Plan(catalog, Array.Empty<string>(), target).Garments);
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void Applying_moves_the_files_and_undoing_puts_them_back()
    {
        var (temp, source, bin, catalog, target) = World();
        try
        {
            var plan = Planner.Plan(catalog, new[] { "z1|k.rpf|jbib|7|u", "z1|k.rpf|feet|50|u_1" }, target);
            var progress = new List<ProgressReport>();
            var log = Executor.Execute(plan, "test", new SyncProgress<ProgressReport>(progress.Add));

            Assert.False(log.Aborted);
            Assert.Equal(3, log.Moves.Count);
            Assert.Equal(2, log.Garments.Count);
            Assert.True(File.Exists(Path.Combine(bin, "k.rpf", "jbib_007_u.ydd")));
            Assert.True(File.Exists(Path.Combine(bin, "k.rpf", "jbib_diff_007_a_uni.ytd")));
            Assert.True(File.Exists(Path.Combine(bin, "k.rpf", "feet_050_u_1.ydd")));
            Assert.False(File.Exists(Path.Combine(source, "k.rpf", "jbib_007_u.ydd")));
            Assert.True(File.Exists(Path.Combine(source, "k.rpf", "feet_diff_050_a_uni.ytd")));   // shared, so it stayed
            Assert.True(File.Exists(Path.Combine(source, "k.rpf", "feet_050_u.ydd")));
            Assert.Contains(progress, report => report.Stage == "apply" && report.Total == 3);
            Assert.True(log.CanUndo);
            Assert.True(log.CanRestoreGarment("z1|k.rpf|jbib|7|u"));

            var file = Path.Combine(temp, "history", "log.json");
            Store.Save(log, file);
            var reloaded = Store.Load(file).Value;
            Assert.Equal(3, reloaded.Moves.Count);
            Assert.Equal("test", reloaded.Description);
            Assert.Equal(250, reloaded.Bytes);   // 100 + 50 + 100

            // undo one garment only
            var (restored, skipped) = Executor.Undo(reloaded, new[] { "z1|k.rpf|feet|50|u_1" });
            Assert.Equal(1, restored);
            Assert.Equal(0, skipped);
            Assert.True(File.Exists(Path.Combine(source, "k.rpf", "feet_050_u_1.ydd")));
            Assert.False(File.Exists(Path.Combine(bin, "k.rpf", "feet_050_u_1.ydd")));
            Assert.Null(reloaded.UndoneAt);
            Assert.True(reloaded.PartlyUndone);
            Assert.True(reloaded.CanUndo);
            Assert.False(reloaded.CanRestoreGarment("z1|k.rpf|feet|50|u_1"));

            // something else is sitting where a file would return to, so that one is skipped
            File.WriteAllBytes(Path.Combine(source, "k.rpf", "jbib_007_u.ydd"), new byte[1]);
            (restored, skipped) = Executor.Undo(reloaded);
            Assert.Equal(1, restored);
            Assert.Equal(1, skipped);
            Assert.Null(reloaded.UndoneAt);

            File.Delete(Path.Combine(source, "k.rpf", "jbib_007_u.ydd"));
            (restored, skipped) = Executor.Undo(reloaded);
            Assert.Equal(1, restored);
            Assert.Equal(0, skipped);
            Assert.NotNull(reloaded.UndoneAt);
            Assert.False(reloaded.CanUndo);
            Assert.False(Directory.Exists(Path.Combine(bin, "k.rpf")));   // the empty bin folders were tidied away

            Store.Save(reloaded, file);
            Assert.NotNull(Store.Load(file).Value.UndoneAt);
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void Cancelling_half_way_still_leaves_a_log_that_undoes_what_moved()
    {
        var (temp, _, bin, catalog, target) = World();
        try
        {
            var plan = Planner.Plan(catalog, new[] { "z1|k.rpf|jbib|7|u", "z1|k.rpf|feet|50|u_1" }, target);
            using var cancellation = new CancellationTokenSource();

            // SyncProgress, not Progress: the real one reports asynchronously, and the cancellation would
            // arrive after every file had already moved
            var log = Executor.Execute(plan, "test",
                new SyncProgress<ProgressReport>(report => { if (report.Done == 1) cancellation.Cancel(); }),
                cancellation.Token);

            Assert.True(log.Aborted);
            Assert.Single(log.Moves);
            Assert.Single(Directory.GetFiles(bin, "*", SearchOption.AllDirectories));

            Executor.Undo(log);
            Assert.NotNull(log.UndoneAt);
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void A_path_outside_the_source_keeps_only_its_file_name()
    {
        var temp = Sciezki.Tymczasowy("relative");
        try
        {
            var file = Path.Combine(temp, "a", "b.rpf", "c.ydd");
            Assert.Equal(Path.Combine("a", "b.rpf", "c.ydd"), ApplyPlanner.RelativeTo(temp, file));
            Assert.Equal("c.ydd", ApplyPlanner.RelativeTo(Path.Combine(temp, "elsewhere"), file));
            Assert.Equal("c.ydd", ApplyPlanner.RelativeTo(null, file));
        }
        finally { Directory.Delete(temp, true); }
    }
}
