using System.Globalization;
using Duble.Cli.CommandLine;

namespace Duble.Cli.Commands;

/// <summary>
/// `duble calibrate`: measures the distances the thresholds judge, over the user's own catalog, and prints
/// what those measurements would support. It changes nothing — the numbers are for a person to read.
/// </summary>
public static class CalibrateCommand
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static CliCommand Command { get; } = new(
        "calibrate",
        "Measure the thresholds against the catalog and propose new ones",
        "",
        new[] { CatalogOptions.Catalog, CliPaths.HomeOption },
        Run);

    static int Run(CommandContext context)
    {
        var file = context.Arguments.Value(CatalogOptions.Catalog.Name, context.Paths.Catalog)!;
        var catalog = context.Service<ICatalogStore>().Load(file);
        if (catalog.Garments.Count == 0)
        {
            context.Output.Error($"the catalog is empty ({file}) — run `duble index` first");
            return ExitCode.Failed;
        }

        Print(context.Output, context.Service<ICalibrator>().Run(catalog));
        return ExitCode.Ok;
    }

    static void Print(Output output, CalibrationReport report)
    {
        output.Line($"garments with geometry: {report.GarmentsWithGeometry} / {report.Garments}");
        output.Detail($"identical files              : {report.GeoSameFile?.Text()}");
        output.Detail($"same position hash           : {report.GeoSameHash?.Text()}");
        output.Detail($"NEAREST FOREIGN MESH, per garment: {report.GeoNearestForeign?.Text()}");
        output.Detail($"same mesh across packs       : {report.GeoPairsAcrossPacks}");

        output.Line();
        output.Line($"pairs with DIFFERENT meshes closer than 0.05: {report.GeoSuspicious}");
        foreach (var pair in report.Suspicious)
            output.Detail($"d={pair.D.ToString("F4", Invariant)} bbox={pair.Bbox.ToString("F3", Invariant)}  "
                + $"{pair.A} ({pair.TriA} tri)  vs  {pair.B} ({pair.TriB} tri)");

        output.Line();
        output.Line($"textures decoded: {report.DecodedTextures} / {report.Textures}");
        output.Detail($"hash, identical files : {report.HashIdentical?.Text("F1")}");
        output.Detail($"hash, colour variants : {report.HashVariants?.Text("F1")}");
        output.Detail($"hash, random pairs    : {report.HashRandom?.Text("F1")}");
        output.Detail($"colour, variants      : {report.ColorVariants?.Text("F2")}");
        output.Detail($"colour, random pairs  : {report.ColorRandom?.Text("F2")}");
        output.Detail($"brightness variance   : {report.Variance?.Text("F1")}");

        if (report.Proposal is not { } proposal) return;

        output.Line();
        output.Line("thresholds these measurements would support:");
        output.Detail($"geometry, identical : distance <= {proposal.GeometryIdentical.ToString("F4", Invariant)}   (a third of the nearest foreign mesh)");
        output.Detail($"geometry, similar   : distance <= {proposal.GeometrySimilar.ToString("F4", Invariant)}");
        output.Detail($"texture hash        : <= {proposal.TextureHashDistance}");
        output.Detail($"texture colour      : <= {proposal.TextureColorDistance.ToString("F2", Invariant)}");
    }
}
