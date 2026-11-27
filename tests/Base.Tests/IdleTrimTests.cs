using Base.Web;

namespace Base.Tests;

public class IdleTrimTests
{
    [Fact]
    public void Memory_is_given_back_only_after_work_and_only_when_the_app_is_quiet()
    {
        Assert.True(IdleTrim.ShouldTrim(sinceTrim: 40 << 20, lastMinute: 100_000));       // a burst of work, then quiet: trim
        Assert.False(IdleTrim.ShouldTrim(sinceTrim: 1 << 20, lastMinute: 0));             // nothing worth it since the last trim
        Assert.False(IdleTrim.ShouldTrim(sinceTrim: 40 << 20, lastMinute: 5 << 20));      // still busy: the collector runs by itself
    }

    [Fact]
    public void A_trim_runs_on_any_platform_without_failing() => IdleTrim.Trim();   // the effect is measured on a real app (caddy's dev/measure.sh), not here: other tests run beside it
}
