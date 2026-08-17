#nullable enable
using System;

namespace Duble.Core.Time;

/// <summary>
/// The current time, injected so that the timestamps Duble writes — catalog, project, undo log — are
/// deterministic in tests.
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

/// <summary>The machine clock in local time: Duble writes timestamps for people to read, not for machines to sort.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
