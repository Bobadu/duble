// Taking clothing file names apart.
//
// The R* convention, inside a folder or container:  <slot>_<NNN>_<u|r>[_k].ydd
//                                                   <slot>_diff_<NNN>_<letter>_<race>[_k].ytd
// Props:                                            p_<anchor>_<NNN>[_k].ydd
//                                                   p_<anchor>_diff_<NNN>_<letter>[_k].ytd
// FiveM (stream\ resources):                        <ped>_<pack>^<name as above> — the part before '^' is the
//                                                   container.
// The "_k" tail (jbib_022_u_1) is what exporters add when names collide; it belongs to the suffix.
using System.Text.RegularExpressions;

namespace Duble.Core.Naming;

/// <summary>A model file name taken apart.</summary>
/// <param name="Slot">The four-letter R* code (jbib, uppr, feet…) or p_&lt;anchor&gt; for a prop.</param>
/// <param name="Number">The NNN in the name.</param>
/// <param name="Suffix">"u" (universal) or "r" (race-specific), with an exporter's tail if it has one.</param>
/// <param name="IsProp">Whether this is a prop rather than a garment.</param>
/// <param name="Container">The FiveM part before '^', or null.</param>
public sealed record ModelFileName(string Slot, int Number, string Suffix, bool IsProp, string? Container);
/// <summary>A texture file name taken apart.</summary>
/// <param name="Slot">The four-letter R* code, or p_&lt;anchor&gt; for a prop.</param>
/// <param name="Number">The NNN in the name.</param>
/// <param name="Letter">The colour variant: a, b, c…</param>
/// <param name="Race">The race the texture belongs to, "uni" when it is for everyone.</param>
/// <param name="IsProp">Whether this is a prop rather than a garment.</param>
/// <param name="Container">The FiveM part before '^', or null.</param>
public sealed record TextureFileName(string Slot, int Number, string Letter, string Race, bool IsProp, string? Container);

public static class ClothingFileName
{
    static readonly Regex ModelPattern = new(@"^([a-z]{4})_(\d{3})_([ur])(_\d+)?\.ydd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex TexturePattern = new(@"^([a-z]{4})_diff_(\d{3})_([a-z])_([a-z]+)(_\d+)?\.ytd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex PropModelPattern = new(@"^p_([a-z]+)_(\d{3})(_\d+)?\.ydd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex PropTexturePattern = new(@"^p_([a-z]+)_diff_(\d{3})_([a-z])(_\d+)?\.ytd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"mp_f_freemode_01_pack^jbib_000_u.ydd" gives ("mp_f_freemode_01_pack", "jbib_000_u.ydd"); without a '^', (null, name).</summary>
    public static (string? prefix, string? name) SplitFiveM(string? fileName)
    {
        if (fileName == null) return (null, null);
        int i = fileName.IndexOf('^');
        return i < 0 ? (null, fileName) : (fileName.Substring(0, i), fileName.Substring(i + 1));
    }

    public static ModelFileName? ParseModel(string? fileName)
    {
        var (prefix, name) = SplitFiveM(fileName);
        if (name == null) return null;
        var m = ModelPattern.Match(name);
        if (m.Success)
            return new ModelFileName(m.Groups[1].Value.ToLowerInvariant(), int.Parse(m.Groups[2].Value),
                (m.Groups[3].Value + m.Groups[4].Value).ToLowerInvariant(), false, prefix);
        var pm = PropModelPattern.Match(name);
        if (pm.Success)
            return new ModelFileName("p_" + pm.Groups[1].Value.ToLowerInvariant(), int.Parse(pm.Groups[2].Value),
                "u" + pm.Groups[3].Value.ToLowerInvariant(), true, prefix);
        return null;
    }

    public static TextureFileName? ParseTexture(string? fileName)
    {
        var (prefix, name) = SplitFiveM(fileName);
        if (name == null) return null;
        var m = TexturePattern.Match(name);
        if (m.Success)
            return new TextureFileName(m.Groups[1].Value.ToLowerInvariant(), int.Parse(m.Groups[2].Value),
                m.Groups[3].Value.ToLowerInvariant(), m.Groups[4].Value.ToLowerInvariant(), false, prefix);
        var pm = PropTexturePattern.Match(name);
        if (pm.Success)
            return new TextureFileName("p_" + pm.Groups[1].Value.ToLowerInvariant(), int.Parse(pm.Groups[2].Value),
                pm.Groups[3].Value.ToLowerInvariant(), "uni", true, prefix);
        return null;
    }
}
