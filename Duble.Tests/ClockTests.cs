#nullable enable
using System;
using Duble.Core.Time;
using Xunit;

namespace Duble.Tests;

public class ClockTests
{
    [Fact]
    public void The_system_clock_reports_the_current_time()
    {
        var before = DateTimeOffset.Now;
        var now = new SystemClock().Now;
        var after = DateTimeOffset.Now;
        Assert.InRange(now, before, after);
    }

    [Fact]
    public void A_fixed_clock_reports_the_same_instant_every_time()
    {
        IClock clock = new FixedClock(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(clock.Now, clock.Now);
        Assert.Equal(2026, clock.Now.Year);
    }
}
