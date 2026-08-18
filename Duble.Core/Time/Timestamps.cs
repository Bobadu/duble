using System.Globalization;

namespace Duble.Core.Time;

/// <summary>
/// The one timestamp format Duble writes: catalogs, projects, comparisons, undo logs and calibration reports
/// all carry the same stamp, so a person reading two of them side by side is reading the same thing.
///
/// It is local time in a fixed layout rather than an ISO instant on purpose — these stamps are shown to the
/// user, never parsed or sorted by machine.
/// </summary>
public static class Timestamps
{
    public const string Format = "yyyy-MM-dd HH:mm:ss";

    public static string Stamp(this IClock clock) => clock.Now.ToString(Format, CultureInfo.InvariantCulture);
}
