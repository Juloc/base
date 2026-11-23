using System.Globalization;
using System.Text.RegularExpressions;

namespace Base.Tests;

/// <summary>
/// The colour tokens of the look (ui/tokens.css) against WCAG AA (COLOR-02): every pair declared there with "@contrast A B N" reaches N:1
/// in light and in dark, for every accent hue. Colours are oklch(); a screen may show a colour outside sRGB as it is (wide gamut), clip it,
/// or lower its chroma: the test takes the worst of the three.
/// </summary>
public partial class ContrastTests
{
    [GeneratedRegex(@"^\s*--([a-z0-9-]+):\s*light-dark\(oklch\(([0-9.]+ [0-9.]+ (?:var\(--accent-hue\)|[0-9.]+))\),\s*oklch\(([0-9.]+ [0-9.]+ (?:var\(--accent-hue\)|[0-9.]+))\)\);", RegexOptions.Multiline)]
    private static partial Regex Token();

    [GeneratedRegex(@"@contrast\s+([a-z0-9-]+)\s+([a-z0-9-]+)\s+([0-9.]+)")]
    private static partial Regex Pair();

    private sealed record Colour(double L, double C, double? H);     // H null: the hue follows --accent-hue

    private static Colour Parse(string text)
    {
        var p = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, p.Length);
        return new Colour(double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture),
            p[2] == "var(--accent-hue)" ? null : double.Parse(p[2], CultureInfo.InvariantCulture));
    }

    private static (double R, double G, double B) Linear(double l, double c, double h)
    {
        double a = c * Math.Cos(h * Math.PI / 180), b = c * Math.Sin(h * Math.PI / 180);
        double l_ = l + 0.3963377774 * a + 0.2158037573 * b, m_ = l - 0.1055613458 * a - 0.0638541728 * b, s_ = l - 0.0894841775 * a - 1.2914855480 * b;
        double L = l_ * l_ * l_, M = m_ * m_ * m_, S = s_ * s_ * s_;
        return (4.0767416621 * L - 3.3077115913 * M + 0.2309699292 * S, -1.2684380046 * L + 2.6097574011 * M - 0.3413193965 * S, -0.0041960863 * L - 0.7034186147 * M + 1.7076147010 * S);
    }

    private static double Y((double R, double G, double B) c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    private static bool InGamut((double R, double G, double B) c) => c is { R: >= -1e-6 and <= 1 + 1e-6, G: >= -1e-6 and <= 1 + 1e-6, B: >= -1e-6 and <= 1 + 1e-6 };

    /// <summary>The relative luminance of a colour as a screen may show it: as it is, clipped to sRGB, or with the chroma lowered until it fits.</summary>
    private static double[] Luminances(Colour colour, double accentHue)
    {
        var h = colour.H ?? accentHue;
        var raw = Linear(colour.L, colour.C, h);
        var clipped = (Math.Clamp(raw.R, 0, 1), Math.Clamp(raw.G, 0, 1), Math.Clamp(raw.B, 0, 1));
        double lo = 0, hi = colour.C;
        if (!InGamut(raw))
            for (var i = 0; i < 30; i++) { var mid = (lo + hi) / 2; if (InGamut(Linear(colour.L, mid, h))) lo = mid; else hi = mid; }
        else lo = colour.C;
        return [Y(raw), Y(clipped), Y(Linear(colour.L, lo, h))];
    }

    [Fact]
    public void Every_declared_pair_reaches_its_contrast_for_every_hue()
    {
        var css = Repo.Read("ui", "tokens.css");
        var tokens = Token().Matches(css).ToDictionary(m => m.Groups[1].Value, m => (Light: Parse(m.Groups[2].Value), Dark: Parse(m.Groups[3].Value)));
        var pairs = Pair().Matches(css).Select(m => (Fg: m.Groups[1].Value, Bg: m.Groups[2].Value, Min: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))).ToArray();
        Assert.True(tokens.Count >= 20, "the colour tokens were not found: did the format of tokens.css change?");
        Assert.True(pairs.Length >= 25, "the @contrast lines were not found");
        // every colour that is text or a control stands in at least one pair
        foreach (var name in new[] { "text", "text-2", "accent", "on-accent", "on-accent-soft", "danger", "on-danger", "on-danger-soft", "ok", "on-ok-soft", "warn", "on-warn-soft", "line-strong" })
            Assert.Contains(pairs, p => p.Fg == name);

        var failures = new List<string>();
        foreach (var (fg, bg, min) in pairs)
        {
            Assert.True(tokens.ContainsKey(fg) && tokens.ContainsKey(bg), $"@contrast names an unknown colour: {fg} on {bg}");
            foreach (var dark in new[] { false, true })
            {
                var worst = double.MaxValue; var worstHue = 0;
                for (var hue = 0; hue < 360; hue++)
                    foreach (var a in Luminances(dark ? tokens[fg].Dark : tokens[fg].Light, hue))
                        foreach (var b in Luminances(dark ? tokens[bg].Dark : tokens[bg].Light, hue))
                        {
                            var ratio = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
                            if (ratio < worst) { worst = ratio; worstHue = hue; }
                        }
                if (worst < min) failures.Add($"{fg} on {bg} ({(dark ? "dark" : "light")}): {worst:0.00}:1 at hue {worstHue}, needs {min}:1");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

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
