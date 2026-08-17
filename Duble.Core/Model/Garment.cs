#nullable enable
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Duble.Core.Model;

/// <summary>
/// One garment: a model (.ydd) together with ALL of its textures (.ytd) — the colour variants a, b, c… of the
/// same number. Duplicates are not decided per file: whether two garments are the same is decided by the pair
/// (geometry, set of textures), so the garment is the unit Duble indexes and compares.
/// </summary>
public class Garment
{
    /// <summary>pack|container|slot|number|suffix. Frozen: group ids hash it, and the user's decisions hang off those.</summary>
    public string? Id { get; set; }

    public string? PackName { get; set; }

    /// <summary>The .rpf folder or archive the files sit in, for example civil01_female.rpf.</summary>
    public string? Container { get; set; }

    /// <summary>The R* component code: jbib / hair / feet …, or p_head for a prop. Called a slot in the interface.</summary>
    public string? Slot { get; set; }

    public int Number { get; set; }

    /// <summary>"u" (universal) or "r" (race-specific), possibly with an exporter's tail: "u_1".</summary>
    public string Suffix { get; set; } = "u";

    public bool IsProp { get; set; }

    public GameFormat GameFormat { get; set; }

    public string? ModelPath { get; set; }

    /// <summary>Size and timestamp of the .ydd, as in <see cref="TextureInfo.ChangeStamp"/>.</summary>
    public string? ChangeStamp { get; set; }

    /// <summary>Id of the source in the project (ProjectSource.Id); the CLI leaves this null.</summary>
    public string? SourceId { get; set; }

    public long ModelSize { get; set; }
    public string? ModelSha256 { get; set; }

    public GeometryFingerprint? Geometry { get; set; }
    public List<TextureInfo> Textures { get; set; } = new();

    /// <summary>
    /// Builds the id. FROZEN: group ids are a hash over these strings and every decision the user has made
    /// hangs off a group id, so changing the shape orphans them all.
    /// </summary>
    public static string MakeId(string? pack, string? container, string? slot, int number, string? suffix)
        => $"{pack}|{container}|{slot}|{number}|{suffix}";

    /// <summary>Short, readable description for a report.</summary>
    [JsonIgnore] public string Label => $"{PackName} / {Slot}_{Number:d3}";
}
