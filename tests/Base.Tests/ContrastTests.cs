using System.Text.RegularExpressions;

namespace Base.Tests;

/// <summary>Touch targets (MOBILE-01). The contrast checks of the colour tokens (COLOR-02) were removed on the owner's word, see decision 70.</summary>
public class ContrastTests
{
    [Fact]
    public void The_touch_target_is_at_least_44_px()
    {
        var css = Repo.Read("ui", "tokens.css");
        var touch = Regex.Match(css, @"--touch:\s*(\d+)px");
        Assert.True(touch.Success && int.Parse(touch.Groups[1].Value) >= 44, "--touch must be at least 44px");
        Assert.Matches(@"--control-h:\s*var\(--touch\)", css);
        // the compact height applies only on a wide screen that is not driven by a finger
        var compact = Regex.Matches(css, @"@media\s*([^{]+)\{\s*:root\s*\{\s*--control-h:");
        Assert.Single(compact);
        Assert.Contains("not (pointer: coarse)", compact[0].Groups[1].Value);
        // and in the styles nothing a finger presses is given a smaller fixed height
        var styles = Repo.Read("ui", "base.css");
        Assert.Matches(@"\.btn\s*\{[^}]*min-height:\s*var\(--control-h\);\s*min-width:\s*var\(--control-h\)", styles);
        Assert.Matches(@"\.tab\s*\{[^}]*min-height:\s*var\(--touch\)", styles);
        Assert.Matches(@"\.switch\s*\{[^}]*min-height:\s*var\(--touch\)", styles);
    }
}
