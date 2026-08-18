// Commands/ProjectSettingsCommands.cs — the settings of the PROJECT (bin folder, comparison thresholds), the
// size of its cache, and calibration.
//
// Thresholds arrive one field at a time: what is sent overwrites what is there, the result is validated by
// Core, and an invalid one comes back as bad_args listing the fields. Changing them starts a comparison in the
// background, because everything on screen was worked out with the old ones. Decisions survive it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Duble.App.Commands;

public sealed class ProjectSettingsCommands : CommandModule
{
    readonly JobRunner jobs;
    readonly CatalogWorkflow workflow;
    readonly ICalibrator calibrator;

    public ProjectSettingsCommands(Bridge bridge, Session session, JobRunner jobs, CatalogWorkflow workflow, ICalibrator calibrator)
        : base(bridge, session)
    {
        this.jobs = jobs;
        this.workflow = workflow;
        this.calibrator = calibrator;
    }

    public override void Register()
    {
        Bridge.Register("project.settings.get", _ => State());
        Bridge.Register("project.settings.set", Change);
        Bridge.Register("project.settings.resetProgi", _ => ResetThresholds());
        Bridge.Register("cache.clear", ClearCache);
        Bridge.Register("calibrate.run", _ => StartCalibrating());
    }

    /// <summary>The thresholds as the interface names them; the same names come back in <see cref="ReadThresholds"/>.</summary>
    static object ThresholdsJson(Thresholds thresholds) => new
    {
        geometryIdentical = thresholds.GeometryIdentical,
        geometrySimilar = thresholds.GeometrySimilar,
        geometryTriangleTolerance = thresholds.GeometryTriangleTolerance,
        geometryBoundsTolerance = thresholds.GeometryBoundsTolerance,
        textureHashDistance = thresholds.TextureHashDistance,
        textureColorDistance = thresholds.TextureColorDistance,
        flatTextureVariance = thresholds.FlatTextureVariance,
        flatTextureColorDistance = thresholds.FlatTextureColorDistance,
        fullCoverage = thresholds.FullCoverage,
        partialCoverage = thresholds.PartialCoverage,
    };

    /// <summary>Overwrites in <paramref name="thresholds"/> the fields that were sent. Returns how many.</summary>
    public static int ReadThresholds(Thresholds thresholds, JsonElement sent)
    {
        if (sent.ValueKind != JsonValueKind.Object) return 0;

        int changed = 0;
        foreach (var field in sent.EnumerateObject())
        {
            if (field.Value.ValueKind != JsonValueKind.Number) continue;
            var value = field.Value.GetDouble();
            switch (field.Name)
            {
                case "geometryIdentical": thresholds.GeometryIdentical = value; break;
                case "geometrySimilar": thresholds.GeometrySimilar = value; break;
                case "geometryTriangleTolerance": thresholds.GeometryTriangleTolerance = value; break;
                case "geometryBoundsTolerance": thresholds.GeometryBoundsTolerance = value; break;
                case "textureHashDistance": thresholds.TextureHashDistance = (int)Math.Round(value); break;
                case "textureColorDistance": thresholds.TextureColorDistance = value; break;
                case "flatTextureVariance": thresholds.FlatTextureVariance = (float)value; break;
                case "flatTextureColorDistance": thresholds.FlatTextureColorDistance = value; break;
                case "fullCoverage": thresholds.FullCoverage = value; break;
                case "partialCoverage": thresholds.PartialCoverage = value; break;
                default: continue;
            }
            changed++;
        }
        return changed;
    }

    /// <param name="comparing">true = a fresh comparison started, false = the job runner was busy,
    /// null = nothing that would change the result was touched.</param>
    object State(bool? comparing = null)
    {
        var project = Project;
        var thresholds = project.Settings?.Thresholds;
        return new
        {
            kosz = project.Settings?.BinFolder,
            progi = ThresholdsJson(thresholds ?? Thresholds.Default),
            progiDomyslne = ThresholdsJson(Thresholds.Default),
            progiZmienione = thresholds != null && !thresholds.SameAs(Thresholds.Default),
            cache = CacheJson(),
            folderCache = project.CacheFolder,
            zrodla = project.Sources.Count,
            pozycje = Session.Catalog.Garments.Count,
            porownanie = comparing,
        };
    }

    Dictionary<string, object> CacheJson()
        => Session.CacheSize().ToDictionary(entry => entry.Key, entry => (object)new { pliki = entry.Value.Files, bajty = entry.Value.Bytes });

    object Change(JsonElement args)
    {
        var project = Project;
        project.Settings ??= new ProjectSettings();

        if (args.Has("kosz"))
        {
            var bin = args.Text("kosz");
            project.Settings.BinFolder = string.IsNullOrWhiteSpace(bin) ? null : bin;
        }

        bool thresholdsChanged = false;
        var sent = args.Object("progi");
        if (sent.ValueKind == JsonValueKind.Object)
        {
            var updated = (project.Settings.Thresholds ?? Thresholds.Default).Clone();
            ReadThresholds(updated, sent);

            var invalid = updated.Validate();
            if (invalid.Count > 0) throw new BridgeException(BridgeErrors.BadArguments, string.Join(",", invalid));

            thresholdsChanged = !updated.SameAs(project.Settings.Thresholds ?? Thresholds.Default);
            // storing null for "the defaults" keeps the project file honest about what the user actually chose
            project.Settings.Thresholds = updated.SameAs(Thresholds.Default) ? null : updated;
        }

        Session.SaveProject();
        Bridge.Event("settings.changed", new { zrodlo = "project" });
        return State(thresholdsChanged && Session.Comparison != null ? StartComparing() : null);
    }

    object ResetThresholds()
    {
        var project = Project;
        bool wereChanged = project.Settings?.Thresholds != null && !project.Settings.Thresholds.SameAs(Thresholds.Default);
        if (project.Settings != null) project.Settings.Thresholds = null;

        Session.SaveProject();
        Bridge.Event("settings.changed", new { zrodlo = "project" });
        return State(wereChanged && Session.Comparison != null ? StartComparing() : null);
    }

    object ClearCache(JsonElement args)
    {
        RequireProject();
        var (files, bytes) = Session.ClearCache(args.Flag("tex", true), args.Flag("mesh", true));
        Bridge.Event("settings.changed", new { zrodlo = "cache" });
        return new { usunieto = files, bajty = bytes, cache = CacheJson() };
    }

    object StartCalibrating()
    {
        var name = ProjectName;   // no project, nothing to calibrate on
        var catalog = Session.EnabledCatalog();
        // calibration measures distances between garments, so two of them with usable geometry is the minimum
        if (catalog.Garments.Count(garment => garment.Geometry?.ShapeHistogram != null && garment.Geometry.Vertices > 0) < 2)
            throw new BridgeException(BridgeErrors.NotFound, "too few garments to calibrate");

        var thresholds = Session.Thresholds;
        bool started = jobs.TryStart(JobKinds.Calibration, name, async (cancellation, progress) =>
        {
            await Task.Yield();
            progress(new ProgressReport("calibration", 0, 0, null));
            Bridge.Event("calibrate.done", new { wynik = calibrator.Run(catalog, thresholds, cancellation) });
        });
        if (!started) throw Busy();

        return new { uruchomiono = true };
    }

    bool StartComparing() => jobs.TryStart(JobKinds.Compare, ProjectName, async (cancellation, progress) =>
    {
        await Task.Yield();
        workflow.CompareAndSave(cancellation, progress);
    });
}
