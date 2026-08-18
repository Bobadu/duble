namespace Duble.Core.Model;

/// <summary>
/// Which build of GTA V a file was made for, read from its own RSC7 header (ydd 165 / ytd 13 for Legacy,
/// 159 / 5 for Enhanced). This is a property of the file, not of the mode CodeWalker reads in — Duble always
/// reads in gen9 mode, which handles both.
/// </summary>
public enum GameFormat
{
    /// <summary>GTA V Legacy.</summary>
    Legacy,

    /// <summary>GTA V Enhanced, also called gen9.</summary>
    Enhanced,
}

/// <summary>Turning a file header into a <see cref="GameFormat"/>, and a format into the word Duble shows.</summary>
public static class GameFormats
{
    /// <summary>What an RSC7 header says; a file that is not a resource at all counts as Legacy.</summary>
    public static GameFormat FromHeader(bool? enhanced) => enhanced == true ? GameFormat.Enhanced : GameFormat.Legacy;

    /// <summary>"gen9" / "legacy" — the words the interface, the report and the CLI already use.</summary>
    public static string ToLabel(this GameFormat format) => format == GameFormat.Enhanced ? "gen9" : "legacy";
}
