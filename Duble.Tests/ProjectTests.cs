#nullable enable
using System;
using System.IO;
using Duble.Core.Comparison;
using Duble.Core.Decisions;
using Duble.Core.Model;
using Duble.Core.Projects;
using Duble.Core.Storage;
using Xunit;

namespace Duble.Tests;

public class ProjectTests
{
    static readonly DateTimeOffset When = new(2026, 8, 17, 21, 0, 0, TimeSpan.Zero);
    static string Id() => Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public void A_saved_project_comes_back_with_its_sources_decisions_and_settings()
    {
        var tmp = TestPaths.Temp("project");
        try
        {
            var file = Path.Combine(tmp, "Studio.duble");
            var store = new JsonProjectStore();
            var project = Project.Create("Studio", file, When);

            var resource = Path.Combine(tmp, "paczka");
            Directory.CreateDirectory(Path.Combine(resource, "stream"));
            File.WriteAllBytes(Path.Combine(tmp, "dlc.rpf"), new byte[] { 1, 2, 3 });

            var fivem = project.AddSource(resource, Id());
            var archive = project.AddSource(Path.Combine(tmp, "dlc.rpf"), Id());
            var folder = project.AddSource(tmp, Id());

            Assert.Equal(SourceKind.FiveMResource, fivem.Kind);
            Assert.Equal(SourceKind.Archive, archive.Kind);
            Assert.Equal(SourceKind.Folder, folder.Kind);
            Assert.Equal("paczka", fivem.Name);
            Assert.Equal(Path.GetFileName(tmp), archive.Name);   // dlc.rpf says nothing: take the pack folder
            Assert.NotEqual(fivem.Id, archive.Id);

            project.Decisions["abc123"] = new Decision
            {
                Winner = "p|k|jbib|1|u", Rejected = { "p|k|jbib|2|u" }, Note = "this one is better",
            };
            project.Decisions["ign"] = new Decision { Ignored = true };
            project.Settings.Thresholds = new Thresholds { TextureHashDistance = 24 };

            Assert.True(store.Save(project).IsSuccess);
            Assert.True(File.Exists(file));

            var read = store.Load(file);
            Assert.True(read.IsSuccess);
            var back = read.Value;

            Assert.Equal("Studio", back.Name);
            Assert.Equal(Project.CurrentVersion, back.Version);
            Assert.Equal(file, back.Path);
            Assert.Equal(file + ".cache", back.CacheFolder);
            Assert.EndsWith(Path.Combine("Studio.duble.cache", "thumbs"), back.ThumbnailFolder);
            Assert.Equal(3, back.Sources.Count);
            Assert.Equal(SourceKind.FiveMResource, back.Sources[0].Kind);
            Assert.True(back.Sources[0].Enabled);
            Assert.Equal("this one is better", back.Decisions["abc123"].Note);
            Assert.Single(back.Decisions["abc123"].Rejected);
            Assert.True(back.Decisions["ign"].Ignored);
            Assert.Equal(24, back.Settings.Thresholds!.TextureHashDistance);
            Assert.Equal(0.02, back.Settings.Thresholds.GeometryIdentical);

            // the file is for people to read: words, not enum numbers
            Assert.Contains("\"kind\": \"fiveMResource\"", File.ReadAllText(file));
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Source_names_are_unique_and_the_same_folder_is_never_added_twice()
    {
        var tmp = TestPaths.Temp("project-names");
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "a", "stream"));
            Directory.CreateDirectory(Path.Combine(tmp, "b", "stream"));
            var project = Project.Create("X", Path.Combine(tmp, "X.duble"), When);

            var first = project.AddSource(Path.Combine(tmp, "a", "stream"), Id());
            var second = project.AddSource(Path.Combine(tmp, "b", "stream"), Id());
            var again = project.AddSource(Path.Combine(tmp, "b", "stream"), Id());

            Assert.Equal("stream", first.Name);
            Assert.Equal("stream (2)", second.Name);
            Assert.Same(second, again);
            Assert.Equal(2, project.Sources.Count);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void A_missing_or_unreadable_project_comes_back_as_a_failure_not_an_exception()
    {
        var tmp = TestPaths.Temp("project-bad");
        try
        {
            var store = new JsonProjectStore();

            var missing = store.Load(Path.Combine(tmp, "nope.duble"));
            Assert.True(missing.IsFailure);
            Assert.Equal(ErrorCodes.ProjectUnreadable, missing.Error.Code);

            var broken = Path.Combine(tmp, "broken.duble");
            File.WriteAllText(broken, "this is not json");
            Assert.Equal(ErrorCodes.ProjectUnreadable, store.Load(broken).Error.Code);

            // Duble reads exactly the version it writes: anything else is refused rather than guessed at
            var future = Path.Combine(tmp, "future.duble");
            File.WriteAllText(future, """{"version":99,"name":"From tomorrow"}""");
            Assert.Equal(ErrorCodes.ProjectUnsupportedVersion, store.Load(future).Error.Code);

            var older = Path.Combine(tmp, "older.duble");
            File.WriteAllText(older, """{"Wersja":1,"Nazwa":"From before the rewrite"}""");
            Assert.Equal(ErrorCodes.ProjectUnsupportedVersion, store.Load(older).Error.Code);
        }
        finally { Directory.Delete(tmp, true); }
    }

    /// <summary>
    /// The decisions in a .duble file are the one thing re-indexing cannot reproduce, so saving must never
    /// leave the file half-written — it goes to a temporary beside it and is moved into place.
    /// </summary>
    [Fact]
    public void Saving_a_project_leaves_no_temporary_behind_and_a_failed_save_leaves_the_old_file_whole()
    {
        var tmp = TestPaths.Temp("project-save");
        try
        {
            var file = Path.Combine(tmp, "Studio.duble");
            var store = new JsonProjectStore();
            var project = Project.Create("Studio", file, When);
            project.Decisions["abc123"] = new Decision { Note = "keep this" };

            Assert.True(store.Save(project).IsSuccess);
            Assert.False(File.Exists(file + ".tmp"));

            // a directory sitting where the temporary would go makes the write fail
            Directory.CreateDirectory(file + ".tmp");
            var failed = store.Save(project);
            Assert.True(failed.IsFailure);
            Assert.Equal(ErrorCodes.ProjectUnwritable, failed.Error.Code);

            Directory.Delete(file + ".tmp");
            Assert.Equal("keep this", store.Load(file).Value.Decisions["abc123"].Note);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Theory]
    [InlineData(new GameFormat[0], SourceFormat.Unknown)]
    [InlineData(new[] { GameFormat.Legacy, GameFormat.Legacy }, SourceFormat.Legacy)]
    [InlineData(new[] { GameFormat.Enhanced }, SourceFormat.Enhanced)]
    [InlineData(new[] { GameFormat.Legacy, GameFormat.Enhanced }, SourceFormat.Mixed)]
    public void A_sources_format_follows_the_garments_indexing_found_in_it(GameFormat[] formats, SourceFormat expected)
    {
        var garments = Array.ConvertAll(formats, format => new Garment { GameFormat = format });
        Assert.Equal(expected, SourceFormats.Of(garments));
    }

    /// <summary>The label is a key the interface looks up, so it stays in English like every other one.</summary>
    [Theory]
    [InlineData(SourceFormat.Legacy, "legacy")]
    [InlineData(SourceFormat.Enhanced, "gen9")]
    [InlineData(SourceFormat.Mixed, "mixed")]
    [InlineData(SourceFormat.Unknown, null)]
    public void A_format_label_is_a_key_not_a_sentence(SourceFormat format, string? expected)
    {
        Assert.Equal(expected, format.ToLabel());
    }
}
