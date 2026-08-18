using System.Linq;
using Duble.Cli.CommandLine;

namespace Duble.Cli.Commands;

/// <summary>`duble list`: what the catalog holds, by pack and by slot.</summary>
public static class ListCommand
{
    public static CliCommand Command { get; } = new(
        "list",
        "Show what the catalog holds, by pack",
        "",
        new[] { CatalogOptions.Catalog, CliPaths.HomeOption },
        Run);

    static int Run(CommandContext context)
    {
        var file = context.Arguments.Value(CatalogOptions.Catalog.Name, context.Paths.Catalog)!;
        var catalog = context.Service<ICatalogStore>().Load(file);

        if (catalog.Garments.Count == 0)
        {
            context.Output.Line($"the catalog is empty ({file})");
            return ExitCode.Ok;
        }

        foreach (var pack in catalog.Garments.GroupBy(garment => garment.PackName).OrderBy(pack => pack.Key))
        {
            context.Output.Line($"{pack.Key,-28} {pack.Count(),5} garments, {pack.Sum(g => g.Textures.Count),6} textures");
            foreach (var slot in pack.GroupBy(garment => garment.Slot).OrderBy(slot => slot.Key))
                context.Output.Line($"    {slot.Key,-10} {slot.Count(),4}");
        }

        return ExitCode.Ok;
    }
}
