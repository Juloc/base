using System.Globalization;
using System.Text.RegularExpressions;

namespace Base.Tests;

/// <summary>
/// The colour tokens of the look (ui/tokens.css) against WCAG AA (COLOR-02): every pair declared there with "@contrast A B N" reaches N:1
/// in light and in dark, for every accent hue (the look of 0.3) and for a set of accent colours (--accent-color, since 0.4.0). The tokens are derived
/// with relative colour syntax, so this test evaluates the formulas of tokens.css itself (oklch(), oklch(from ...), light-dark(), var(), min/max/clamp/calc).
/// A screen may show a colour outside sRGB as it is (wide gamut), clip it, or lower its chroma: the test takes the worst of the three.
/// </summary>
public partial class ContrastTests
{
    [GeneratedRegex(@"^\s*--([a-z0-9-]+):\s*(.+?);\s*(?:/\*.*)?$", RegexOptions.Multiline)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"@contrast\s+([a-z0-9-]+)\s+([a-z0-9-]+)\s+([0-9.]+)")]
    private static partial Regex Pair();

    private readonly record struct Lch(double L, double C, double H);

    /// <summary>One accent of an app: a colour, or only a hue.</summary>
    private sealed record Accent(string Name, Lch? Colour, double Hue);

    private sealed class Evaluator(Dictionary<string, string> tokens, Accent accent, bool dark)
    {
        public Lch Token(string name)
        {
            if (name == "accent-color") return accent.Colour ?? throw new InvalidOperationException();
            Assert.True(tokens.TryGetValue(name, out var value), $"tokens.css has no --{name}");
            return Colour(value!);
        }

        public Lch Colour(string text)
        {
            text = text.Trim();
            if (text.StartsWith("light-dark(", StringComparison.Ordinal))
            {
                var args = Args(text["light-dark(".Length..^1]);
                return Colour(args[dark ? 1 : 0]);
            }
            if (text.StartsWith("var(", StringComparison.Ordinal))
            {
                var args = Args(text["var(".Length..^1]);
                var name = args[0].Trim()[2..];
                if (name == "accent-color" && accent.Colour is null) return Colour(args[1]);   // no colour named: the fallback, the hue
                return Token(name);
            }
            if (text.StartsWith("color(", StringComparison.Ordinal))
            {
                var cp = Split(text["color(".Length..^1].Trim(), ' ');
                Assert.Equal(["from", "xyz-d65"], new[] { cp[0], cp[2] });
                var src = Linear(Colour(cp[1]));
                var xyz = (X: 0.4123907993 * src.R + 0.3575843394 * src.G + 0.1804807884 * src.B, Y: 0.2126390059 * src.R + 0.7151686788 * src.G + 0.0721923154 * src.B, Z: 0.0193308187 * src.R + 0.1191947798 * src.G + 0.9505321522 * src.B);
                double Xyz(string expr) => new Expr(expr, new() { ["x"] = xyz.X, ["y"] = xyz.Y, ["z"] = xyz.Z }, accent.Hue).Value();
                double x = Xyz(cp[3]), y = Xyz(cp[4]), z = Xyz(cp[5]);
                return FromLinear(3.2409699419 * x - 1.5373831776 * y - 0.4986107603 * z, -0.9692436363 * x + 1.8759675015 * y + 0.0415550574 * z, 0.0556300797 * x - 0.2039769589 * y + 1.0569715142 * z);
            }
            Assert.StartsWith("oklch(", text);
            var inner = text["oklch(".Length..^1].Trim();
            var parts = Split(inner, ' ');
            Lch? from = null;
            if (parts[0] == "from") { from = Colour(parts[1]); parts = parts[2..]; }
            Assert.Equal(3, parts.Length);
            double Channel(string expr, double l, double c, double h) => new Expr(expr, new() { ["l"] = l, ["c"] = c, ["h"] = h }, accent.Hue).Value();
            var (fl, fc, fh) = from is { } f ? (f.L, f.C, f.H) : (0, 0, 0);
            return new Lch(Channel(parts[0], fl, fc, fh), Channel(parts[1], fl, fc, fh), Channel(parts[2], fl, fc, fh));
        }

        /// <summary>The arguments of a function at its top level, split by commas.</summary>
        private static List<string> Args(string text) => Split(text, ',').Select(a => a.Trim()).ToList();

        private static string[] Split(string text, char separator)
        {
            var parts = new List<string>(); var depth = 0; var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')') depth--;
                else if (text[i] == separator && depth == 0) { if (i > start) parts.Add(text[start..i]); start = i + 1; }
            }
            if (start < text.Length) parts.Add(text[start..]);
            return [.. parts.Select(p => p.Trim()).Where(p => p.Length > 0)];
        }
    }

    /// <summary>The expression of one colour channel: numbers, l, c, h, + - * /, parentheses, calc(), min(), max(), clamp(), var(--accent-hue).</summary>
    private sealed class Expr(string text, Dictionary<string, double> vars, double accentHue)
    {
        private int _i;
        public double Value() { var v = Sum(); Assert.Equal(text.Length, Skip()); return v; }
        private int Skip() { while (_i < text.Length && text[_i] == ' ') _i++; return _i; }
        private double Sum()
        {
            var v = Product();
            for (;;)
            {
                Skip();
                if (_i < text.Length && text[_i] == '+') { _i++; v += Product(); }
                else if (_i < text.Length && text[_i] == '-') { _i++; v -= Product(); }
                else return v;
            }
        }
        private double Product()
        {
            var v = Factor();
            for (;;)
            {
                Skip();
                if (_i < text.Length && text[_i] == '*') { _i++; v *= Factor(); }
                else if (_i < text.Length && text[_i] == '/') { _i++; v /= Factor(); }
                else return v;
            }
        }
        private double Factor()
        {
            Skip();
            if (text[_i] == '(') { _i++; var v = Sum(); Skip(); Assert.Equal(')', text[_i]); _i++; return v; }
            if (text[_i] == '-') { _i++; return -Factor(); }
            var m = Regex.Match(text[_i..], @"^(?:(\d*\.?\d+)|(var\(--accent-hue\))|([a-z]+)\()?");
            if (m.Groups[1].Success) { _i += m.Length; return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); }
            if (m.Groups[2].Success) { _i += m.Length; return accentHue; }
            if (m.Groups[3].Success)
            {
                _i += m.Length;
                var args = new List<double> { Sum() };
                for (Skip(); text[_i] == ','; Skip()) { _i++; args.Add(Sum()); }
                Assert.Equal(')', text[_i]); _i++;
                return m.Groups[3].Value switch
                {
                    "calc" => args[0],
                    "min" => args.Min(),
                    "max" => args.Max(),
                    "clamp" => Math.Max(args[0], Math.Min(args[1], args[2])),
                    var f => throw new InvalidOperationException("unknown function " + f),
                };
            }
            var word = Regex.Match(text[_i..], @"^[a-z]\b").Value;
            Assert.NotEmpty(word);
            _i += 1;
            return vars[word];
        }
    }

    private static Lch FromHex(string hex)
    {
        double Lin(int at) { var v = Convert.ToInt32(hex.Substring(at, 2), 16) / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        double r = Lin(1), g = Lin(3), b = Lin(5);
        return FromLinear(r, g, b);
    }

    private static (double R, double G, double B) Linear(Lch c) => Linear(c.L, c.C, c.H);

    private static Lch FromLinear(double r, double g, double b)
    {
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        double L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s, A = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s, B = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        return new Lch(L, Math.Sqrt(A * A + B * B), (Math.Atan2(B, A) * 180 / Math.PI + 360) % 360);
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
    private static double[] Luminances(Lch colour)
    {
        var l = Math.Clamp(colour.L, 0, 1);
        var h = (colour.H % 360 + 360) % 360;
        var raw = Linear(l, colour.C, h);
        var clipped = (Math.Clamp(raw.R, 0, 1), Math.Clamp(raw.G, 0, 1), Math.Clamp(raw.B, 0, 1));
        double lo = 0, hi = colour.C;
        if (!InGamut(raw))
            for (var i = 0; i < 30; i++) { var mid = (lo + hi) / 2; if (InGamut(Linear(l, mid, h))) lo = mid; else hi = mid; }
        else lo = colour.C;
        return [Y(raw), Y(clipped), Y(Linear(l, lo, h))];
    }

    /// <summary>Every hue (an app that sets only --accent-hue) and colours that are hard: very light, very dark, in the middle where the text on the fill turns, grey.</summary>
    private static IEnumerable<Accent> Accents()
    {
        for (var hue = 0; hue < 360; hue++) yield return new Accent("hue " + hue, null, hue);
        foreach (var hex in new[] { "#00e5ff", "#eaff00", "#ffffff", "#000000", "#1a237e", "#7c4dff", "#ff6f00", "#2e7d32", "#9a67ea", "#e91e63", "#808080", "#ffd600", "#0d47a1", "#b71c1c" })
        {
            var c = FromHex(hex);
            yield return new Accent(hex, c, c.H);
        }
    }

    private static Dictionary<string, string> RootTokens(string css)
    {
        var root = css[css.IndexOf(":root {", StringComparison.Ordinal)..];
        root = root[..root.IndexOf("\n}", StringComparison.Ordinal)];
        return Declaration().Matches(root).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    }

    [Fact]
    public void Every_declared_pair_reaches_its_contrast_for_every_accent()
    {
        var css = Repo.Read("ui", "tokens.css");
        var tokens = RootTokens(css);
        var pairs = Pair().Matches(css).Select(m => (Fg: m.Groups[1].Value, Bg: m.Groups[2].Value, Min: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))).ToArray();
        Assert.True(tokens.Count >= 30, "the colour tokens were not found: did the format of tokens.css change?");
        Assert.True(pairs.Length >= 25, "the @contrast lines were not found");
        // every colour that is text or a control stands in at least one pair
        foreach (var name in new[] { "text", "text-2", "accent-ink", "on-accent", "on-accent-soft", "on-secondary-soft", "on-tertiary-soft", "danger", "on-danger", "on-danger-soft", "ok", "on-ok-soft", "on-warn-soft", "line-strong" })
            Assert.Contains(pairs, p => p.Fg == name);

        var failures = new List<string>();
        foreach (var (fg, bg, min) in pairs)
            Assert.True(tokens.ContainsKey(fg) && tokens.ContainsKey(bg), $"@contrast names an unknown colour: {fg} on {bg}");
        foreach (var dark in new[] { false, true })
        {
            var worst = new double[pairs.Length]; var worstAt = new string[pairs.Length];
            Array.Fill(worst, double.MaxValue);
            foreach (var accent in Accents())
            {
                var ev = new Evaluator(tokens, accent, dark);
                for (var i = 0; i < pairs.Length; i++)
                    foreach (var a in Luminances(ev.Token(pairs[i].Fg)))
                        foreach (var b in Luminances(ev.Token(pairs[i].Bg)))
                        {
                            var ratio = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
                            if (ratio < worst[i]) { worst[i] = ratio; worstAt[i] = accent.Name; }
                        }
            }
            for (var i = 0; i < pairs.Length; i++)
                if (worst[i] < pairs[i].Min) failures.Add($"{pairs[i].Fg} on {pairs[i].Bg} ({(dark ? "dark" : "light")}): {worst[i]:0.00}:1 at {worstAt[i]}, needs {pairs[i].Min}:1");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void The_evaluator_reads_the_formulas_as_a_browser_does()
    {
        var css = Repo.Read("ui", "tokens.css");
        var tokens = RootTokens(css);
        // a hue-only app: the surfaces keep the lightness and chroma of 0.3 and take the hue
        var hueOnly = new Evaluator(tokens, new Accent("hue", null, 152), dark: false).Token("surface-2");
        Assert.Equal((0.948, 0.016, 152), (hueOnly.L, hueOnly.C, Math.Round(hueOnly.H)));
        // a colour: the fill is the colour itself, the text on cyan is dark, on navy light
        var cyan = FromHex("#00e5ff");
        var fill = new Evaluator(tokens, new Accent("cyan", cyan, cyan.H), dark: false);
        Assert.Equal(cyan.L, fill.Token("accent").L, 6);
        Assert.True(fill.Token("on-accent").L < 0.3);
        var navy = FromHex("#1a237e");
        Assert.True(new Evaluator(tokens, new Accent("navy", navy, navy.H), dark: false).Token("on-accent").L > 0.9);
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
