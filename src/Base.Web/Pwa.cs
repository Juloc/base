using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Base.Web;

/// <summary>
/// The installable frame of every app, made from <see cref="BaseAppOptions"/> and served by the base (0.4): <c>/manifest.webmanifest</c>, the icons
/// (<c>/icon.svg</c>, <c>/icon-maskable.svg</c>, PNGs for 192, 512, maskable 512 and the apple-touch-icon) and <c>/sw.js</c>. A file of the same name in the
/// app's own <c>wwwroot</c> wins: the base then maps no endpoint for that path, so the static files serve it (an endpoint would be chosen before them).
/// An app with its own artwork or worker keeps it. Anonymous, nothing private in any of them.
/// </summary>
internal static class Pwa
{
    /// <summary>The files of the shell the worker keeps (SEC-08): the page and its look. A folder ends with a slash. The app adds its own with <c>ShellPaths</c>.</summary>
    public static readonly string[] DefaultShell =
    [
        "/", "/index.html", "/app.css", "/app.js", "/manifest.webmanifest", "/icon.svg", "/icon-maskable.svg",
        "/icon-192.png", "/icon-512.png", "/icon-maskable-512.png", "/apple-touch-icon.png", "/base/", "/i18n/",
    ];

    public static void Check(BaseAppOptions o)
    {
        if (o.AccentColor is { } c && !Regex.IsMatch(c, "^#[0-9a-fA-F]{6}$")) throw new InvalidOperationException("AccentColor is a colour written #rrggbb.");
        foreach (var p in o.ShellPaths)
            if (!Regex.IsMatch(p, "^/[A-Za-z0-9._/-]*$") || p.Contains("..") || p.StartsWith("/api/", StringComparison.Ordinal) || p == "/api")
                throw new InvalidOperationException($"The shell path '{p}' is not a plain path of a static file or folder (it begins with a slash, a folder ends with one, never under /api).");
    }

    public static void MapPwa(this IEndpointRouteBuilder app, AppInfo info, BaseAppOptions options, UiFiles ui)
    {
        var (bg, fg, page) = Colours(options);
        var glyph = Glyph(ui, options.Icon);
        var files = new Dictionary<string, (string Type, Lazy<byte[]> Bytes)>(StringComparer.Ordinal)
        {
            ["/manifest.webmanifest"] = ("application/manifest+json; charset=utf-8", new(() => Encoding.UTF8.GetBytes(Manifest(info, options, page)))),
            ["/icon.svg"] = ("image/svg+xml", new(() => Encoding.UTF8.GetBytes(Svg(glyph, bg, fg, maskable: false)))),
            ["/icon-maskable.svg"] = ("image/svg+xml", new(() => Encoding.UTF8.GetBytes(Svg(glyph, bg, fg, maskable: true)))),
            ["/icon-192.png"] = ("image/png", new(() => Raster.Png(192, glyph, bg, fg, Raster.Kind.Any))),
            ["/icon-512.png"] = ("image/png", new(() => Raster.Png(512, glyph, bg, fg, Raster.Kind.Any))),
            ["/icon-maskable-512.png"] = ("image/png", new(() => Raster.Png(512, glyph, bg, fg, Raster.Kind.Maskable))),
            ["/apple-touch-icon.png"] = ("image/png", new(() => Raster.Png(180, glyph, bg, fg, Raster.Kind.Apple))),
            ["/sw.js"] = ("text/javascript; charset=utf-8", new(() => Encoding.UTF8.GetBytes(ServiceWorker(info, options)))),
        };
        var own = app.ServiceProvider.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;
        foreach (var (path, (type, bytes)) in files.Where(f => !own.GetFileInfo(f.Key).Exists))
            app.MapGet(path, (HttpContext ctx) =>
            {
                var data = bytes.Value;
                var etag = "\"" + Convert.ToHexStringLower(SHA256.HashData(data))[..20] + "\"";
                ctx.Response.Headers.ETag = etag;
                ctx.Response.Headers.CacheControl = "no-cache";                  // the browser asks every time (ETag), so an update arrives at once
                if (ctx.Request.Headers.IfNoneMatch == etag) return Results.StatusCode(StatusCodes.Status304NotModified);
                return Results.Bytes(data, type);
            }).ExcludeFromDescription();
    }

    // ---------- colours ----------

    /// <summary>The fill of the icon, the colour on it (black or white by luminance) and the page colour the manifest names (the light page of the app).</summary>
    public static ((double R, double G, double B) Fill, (double R, double G, double B) On, (double R, double G, double B) Page) Colours(BaseAppOptions o)
    {
        double hue;
        (double, double, double) fill;
        if (o.AccentColor is { } hex)
        {
            fill = Parse(hex);
            hue = ToOklch(fill).H;
        }
        else
        {
            hue = o.AccentHue;
            fill = FromOklch(0.483, 0.15, hue);                                  // the light-scheme accent of tokens.css for a hue
        }
        var luminance = 0.2126 * Lin(fill.Item1) + 0.7152 * Lin(fill.Item2) + 0.0722 * Lin(fill.Item3);
        var on = luminance > 0.1795 ? (0.0, 0.0, 0.0) : (1.0, 1.0, 1.0);        // the same threshold as --on-accent
        return (fill, on, FromOklch(0.983, 0.008, hue));
    }

    public static string Hex((double R, double G, double B) c) => $"#{Byte(c.R):x2}{Byte(c.G):x2}{Byte(c.B):x2}";
    private static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
    private static (double, double, double) Parse(string hex) =>
        (int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber) / 255.0, int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber) / 255.0, int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber) / 255.0);
    private static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    private static double Gamma(double c) => c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;

    private static (double L, double C, double H) ToOklch((double R, double G, double B) c)
    {
        double r = Lin(c.R), g = Lin(c.G), b = Lin(c.B);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        double L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        double A = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        double B = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        var h = Math.Atan2(B, A) * 180 / Math.PI;
        return (L, Math.Sqrt(A * A + B * B), h < 0 ? h + 360 : h);
    }

    /// <summary>oklch to sRGB; a colour outside the screen gamut loses chroma until it fits.</summary>
    public static (double R, double G, double B) FromOklch(double L, double C, double H)
    {
        for (var i = 0; i < 40; i++)
        {
            var a = C * Math.Cos(H * Math.PI / 180);
            var b = C * Math.Sin(H * Math.PI / 180);
            var l = Math.Pow(L + 0.3963377774 * a + 0.2158037573 * b, 3);
            var m = Math.Pow(L - 0.1055613458 * a - 0.0638541728 * b, 3);
            var s = Math.Pow(L - 0.0894841775 * a - 1.2914855480 * b, 3);
            double r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
            double g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
            double bl = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
            const double eps = 0.0005;
            if (r is >= -eps and <= 1 + eps && g is >= -eps and <= 1 + eps && bl is >= -eps and <= 1 + eps || C < 1e-4)
                return (Gamma(Math.Clamp(r, 0, 1)), Gamma(Math.Clamp(g, 0, 1)), Gamma(Math.Clamp(bl, 0, 1)));
            C *= 0.92;
        }
        return (Gamma(L), Gamma(L), Gamma(L));
    }

    // ---------- manifest ----------

    public static string Manifest(AppInfo info, BaseAppOptions o, (double R, double G, double B) page)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            var german = o.Description?.De is { Length: > 0 };
            w.WriteStartObject();
            w.WriteString("id", "/");
            w.WriteString("name", o.Name);
            w.WriteString("short_name", ShortName(o.Name));
            w.WriteString("description", (german ? o.Description!.De : o.Description?.En) ?? o.Name);
            w.WriteString("lang", german || o.Description is null ? "de" : "en");
            w.WriteString("start_url", o.Start);
            w.WriteString("scope", "/");
            w.WriteString("display", "standalone");
            w.WriteString("background_color", Hex(page));                         // the splash: the page colour with the icon on it
            w.WriteString("theme_color", Hex(page));
            w.WriteStartArray("icons");
            foreach (var (src, sizes, type, purpose) in new[]
            {
                ("/icon-192.png", "192x192", "image/png", "any"), ("/icon-512.png", "512x512", "image/png", "any"),
                ("/icon-maskable-512.png", "512x512", "image/png", "maskable"), ("/icon.svg", "any", "image/svg+xml", "any"),
            })
            {
                w.WriteStartObject();
                w.WriteString("src", src); w.WriteString("sizes", sizes); w.WriteString("type", type); w.WriteString("purpose", purpose);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>What fits under an icon: the name when it is short, else its first word, else its first 12 characters.</summary>
    public static string ShortName(string name)
    {
        if (name.Length <= 12) return name;
        var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return first.Length <= 12 ? first : first[..12];
    }

    // ---------- the service worker ----------

    /// <summary>
    /// The worker (SEC-08): it keeps only the static shell, never an answer that needs a session. The cache is named after the app, its version and the version of
    /// the base, so a new version starts with an empty cache and the old caches are deleted when it takes over. A waiting worker does not take over by itself:
    /// the page offers "new version" and sends 'skip-waiting' when the person agrees.
    /// </summary>
    public static string ServiceWorker(AppInfo info, BaseAppOptions o)
    {
        var all = DefaultShell.Concat(o.ShellPaths).Distinct().ToArray();
        string Js(IEnumerable<string> xs) => "[" + string.Join(", ", xs.Select(x => "'" + x + "'")) + "]";
        var cache = $"{info.Id}-{info.Version}{(info.Commit is null ? "" : "+" + info.Commit)}-base{info.BaseVersion}";
        return $$"""
            /* Service worker of {{info.Name}} (made by the base): the app can be installed and starts without a network.
             * It keeps ONLY the static shell (the files below), never an answer that needs a session: no API answer and no other page (SEC-08).
             * The shell is asked from the network first, so a change arrives at once; what was fetched is the fallback without a network.
             * The cache is named after the versions of the app and of the base; old caches are deleted when this worker takes over. */
            const CACHE = '{{cache}}';
            const SHELL = new Set({{Js(all.Where(p => !p.EndsWith('/') || p == "/"))}});
            const SHELL_FOLDERS = {{Js(all.Where(p => p.EndsWith('/') && p != "/"))}};

            const isShell = (path) => SHELL.has(path) || SHELL_FOLDERS.some((p) => path.startsWith(p));

            // a new worker waits until the person agrees (the page shows "new version" and sends this message)
            self.addEventListener('message', (e) => { if (e.data === 'skip-waiting') self.skipWaiting(); });
            self.addEventListener('activate', (e) => {
              e.waitUntil(caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))).then(() => self.clients.claim()));
            });
            self.addEventListener('fetch', (e) => {
              const req = e.request, url = new URL(req.url);
              if (req.method !== 'GET' || url.origin !== self.location.origin || !isShell(url.pathname)) return;
              e.respondWith(
                fetch(req).then((res) => {
                  if (res.ok) { const copy = res.clone(); caches.open(CACHE).then((c) => c.put(req, copy)); }
                  return res;
                }).catch(() => caches.match(req).then((m) => m || (req.mode === 'navigate' ? caches.match('/') : Response.error())))
              );
            });

            """;
    }

    // ---------- the mark ----------

    /// <summary>The drawing of the app's icon: the parts of its glyph in <c>ui/icons.js</c> (a 24 px grid, strokes of 2).</summary>
    public static string Glyph(UiFiles ui, string name)
    {
        if (ui.TryGet("/icons.js", out var bytes))
        {
            var text = Encoding.UTF8.GetString(bytes);
            foreach (var key in new[] { name, "grid" })
            {
                var m = Regex.Match(text, "^  " + Regex.Escape(key) + @": (?:'(?<a>.*)'|`(?<b>.*)`),\s*$", RegexOptions.Multiline);
                if (m.Success) return (m.Groups["a"].Success ? m.Groups["a"].Value : m.Groups["b"].Value).Replace("${F}", "").Replace("${D}", " class=\"d\"");
            }
        }
        return "<circle cx=\"12\" cy=\"12\" r=\"7\"/>";
    }

    public static string Svg(string glyph, (double R, double G, double B) bg, (double R, double G, double B) fg, bool maskable)
    {
        var fraction = maskable ? 0.40 : 0.52;                                  // a maskable icon keeps its mark inside the safe circle
        var scale = 512 * fraction / 24;
        var at = (512 - 24 * scale) / 2;
        var f = (double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 512 512\"><style>.d{{fill:{Hex(fg)};stroke:none}}</style>"
             + (maskable ? $"<rect width=\"512\" height=\"512\" fill=\"{Hex(bg)}\"/>" : $"<rect width=\"512\" height=\"512\" rx=\"112\" fill=\"{Hex(bg)}\"/>")
             + $"<g transform=\"translate({f(at)} {f(at)}) scale({f(scale)})\" fill=\"none\" stroke=\"{Hex(fg)}\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{glyph}</g></svg>";
    }
}

/// <summary>Draws the glyph into a PNG without any library: shapes are flattened to line pieces, a pixel's coverage comes from its distance to them.</summary>
internal static class Raster
{
    public enum Kind { Any, Maskable, Apple }

    public static byte[] Png(int size, string glyph, (double R, double G, double B) bg, (double R, double G, double B) fg, Kind kind)
    {
        var fraction = kind == Kind.Maskable ? 0.40 : 0.52;
        var scale = size * fraction / 24;
        var at = (size - 24 * scale) / 2;
        var strokeR = scale;                                                       // half of a stroke of 2 units
        var dist = new float[size * size];
        Array.Fill(dist, float.MaxValue);
        var fill = new float[size * size];                                         // coverage of filled dots

        foreach (var shape in Shapes(glyph))
        {
            var pts = shape.Points.Select(p => (X: at + p.X * scale, Y: at + p.Y * scale)).ToList();
            if (shape.Dot is { } dot)
            {
                var (cx, cy, r) = (at + dot.X * scale, at + dot.Y * scale, dot.R * scale);
                for (var y = Math.Max(0, (int)(cy - r - 2)); y <= Math.Min(size - 1, (int)(cy + r + 2)); y++)
                    for (var x = Math.Max(0, (int)(cx - r - 2)); x <= Math.Min(size - 1, (int)(cx + r + 2)); x++)
                        fill[y * size + x] = Math.Max(fill[y * size + x], (float)Math.Clamp(r + 0.5 - Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy)), 0, 1));
                continue;
            }
            for (var i = 0; i + 1 < pts.Count; i++) Segment(dist, size, pts[i], pts[i + 1], strokeR);
        }

        var radius = kind == Kind.Any ? size * 112 / 512.0 : 0;                    // only the plain icon has rounded corners; a mask or the phone rounds the others
        var raw = new byte[size * (size * 4 + 1)];
        for (var y = 0; y < size; y++)
        {
            raw[y * (size * 4 + 1)] = 0;
            for (var x = 0; x < size; x++)
            {
                var cover = Math.Max(Math.Clamp(strokeR + 0.5 - dist[y * size + x], 0, 1), fill[y * size + x]);
                var alpha = radius == 0 ? 1.0 : Inside(x + 0.5, y + 0.5, size, radius);
                var o = y * (size * 4 + 1) + 1 + x * 4;
                raw[o] = Q(bg.R + (fg.R - bg.R) * cover); raw[o + 1] = Q(bg.G + (fg.G - bg.G) * cover); raw[o + 2] = Q(bg.B + (fg.B - bg.B) * cover);
                raw[o + 3] = Q(alpha);
            }
        }
        return Encode(size, raw);
    }

    private static byte Q(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    /// <summary>Coverage of a rounded square of the given corner radius at a pixel centre.</summary>
    private static double Inside(double x, double y, int size, double r)
    {
        var dx = Math.Abs(x - size / 2.0) - (size / 2.0 - r);
        var dy = Math.Abs(y - size / 2.0) - (size / 2.0 - r);
        var d = Math.Sqrt(Math.Max(dx, 0) * Math.Max(dx, 0) + Math.Max(dy, 0) * Math.Max(dy, 0)) + Math.Min(Math.Max(dx, dy), 0) - r;
        return Math.Clamp(0.5 - d, 0, 1);
    }

    private static void Segment(float[] dist, int size, (double X, double Y) a, (double X, double Y) b, double r)
    {
        int x0 = Math.Max(0, (int)(Math.Min(a.X, b.X) - r - 2)), x1 = Math.Min(size - 1, (int)(Math.Max(a.X, b.X) + r + 2));
        int y0 = Math.Max(0, (int)(Math.Min(a.Y, b.Y) - r - 2)), y1 = Math.Min(size - 1, (int)(Math.Max(a.Y, b.Y) + r + 2));
        double vx = b.X - a.X, vy = b.Y - a.Y, len2 = vx * vx + vy * vy;
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                double px = x + 0.5 - a.X, py = y + 0.5 - a.Y;
                var t = len2 == 0 ? 0 : Math.Clamp((px * vx + py * vy) / len2, 0, 1);
                var dx = px - t * vx; var dy = py - t * vy;
                var d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d < dist[y * size + x]) dist[y * size + x] = d;
            }
    }

    // ---------- shapes of a glyph ----------

    public sealed record Shape(List<(double X, double Y)> Points, (double X, double Y, double R)? Dot = null);

    private static readonly Regex Element = new(@"<(?<tag>path|circle|ellipse|rect)\b(?<attrs>[^>]*?)/?>", RegexOptions.Compiled);
    private static readonly Regex Attribute = new(@"(?<k>[a-z-]+)=""(?<v>[^""]*)""", RegexOptions.Compiled);

    public static IEnumerable<Shape> Shapes(string glyph)
    {
        foreach (Match m in Element.Matches(glyph))
        {
            var a = Attribute.Matches(m.Groups["attrs"].Value).ToDictionary(x => x.Groups["k"].Value, x => x.Groups["v"].Value);
            double N(string k, double d = 0) => a.TryGetValue(k, out var v) ? double.Parse(v, CultureInfo.InvariantCulture) : d;
            var dot = a.TryGetValue("class", out var cls) && cls.Contains('d');
            switch (m.Groups["tag"].Value)
            {
                case "path":
                    foreach (var sub in Flatten(a.GetValueOrDefault("d", ""))) yield return new Shape(sub);
                    break;
                case "circle" when dot:
                    yield return new Shape([], (N("cx"), N("cy"), N("r")));
                    break;
                case "circle":
                    yield return new Shape(Ellipse(N("cx"), N("cy"), N("r"), N("r")));
                    break;
                case "ellipse":
                    yield return new Shape(Ellipse(N("cx"), N("cy"), N("rx"), N("ry")));
                    break;
                case "rect":
                    yield return new Shape(Rect(N("x"), N("y"), N("width"), N("height"), N("rx", N("ry"))));
                    break;
            }
        }
    }

    private static List<(double X, double Y)> Ellipse(double cx, double cy, double rx, double ry) =>
        [.. Enumerable.Range(0, 97).Select(i => (cx + rx * Math.Cos(i * Math.PI / 48), cy + ry * Math.Sin(i * Math.PI / 48)))];

    private static List<(double X, double Y)> Rect(double x, double y, double w, double h, double r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        var pts = new List<(double, double)>();
        void Corner(double cx, double cy, double from) { for (var i = 0; i <= 8; i++) { var t = (from + i * 10) * Math.PI / 180; pts.Add((cx + r * Math.Cos(t), cy + r * Math.Sin(t))); } }
        Corner(x + w - r, y + h - r, 0); Corner(x + r, y + h - r, 90); Corner(x + r, y + r, 180); Corner(x + w - r, y + r, 270);
        pts.Add(pts[0]);
        return pts;
    }

    /// <summary>An SVG path as polylines. Handles M L H V C S A Z in both cases (all that the icon file uses).</summary>
    public static List<List<(double X, double Y)>> Flatten(string d)
    {
        var result = new List<List<(double X, double Y)>>();
        List<(double X, double Y)>? cur = null;
        double x = 0, y = 0, sx = 0, sy = 0, cpx = 0, cpy = 0;
        char cmd = ' ', prev = ' ';
        var i = 0;
        bool Number(out double v)
        {
            while (i < d.Length && (d[i] is ' ' or ',' or '\n' or '\t')) i++;
            var s = i;
            if (i < d.Length && d[i] is '-' or '+') i++;
            var dot = false;
            while (i < d.Length && (char.IsAsciiDigit(d[i]) || (d[i] == '.' && !dot))) { if (d[i] == '.') dot = true; i++; }
            if (i < d.Length && d[i] is 'e' or 'E') { i++; if (i < d.Length && d[i] is '-' or '+') i++; while (i < d.Length && char.IsAsciiDigit(d[i])) i++; }
            return double.TryParse(d.AsSpan(s, i - s), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
        bool Flag(out bool v)
        {
            while (i < d.Length && (d[i] is ' ' or ',')) i++;
            v = i < d.Length && d[i] == '1';
            if (i < d.Length && d[i] is '0' or '1') { i++; return true; }
            return false;
        }

        while (true)
        {
            while (i < d.Length && (d[i] is ' ' or ',' or '\n' or '\t')) i++;
            if (i >= d.Length) break;
            if (char.IsAsciiLetter(d[i])) cmd = d[i++];
            else if (cmd is 'M') cmd = 'L'; else if (cmd is 'm') cmd = 'l';
            var rel = char.IsAsciiLetterLower(cmd);
            var c = char.ToUpperInvariant(cmd);
            if (c == 'Z') { if (cur is not null) { cur.Add((sx, sy)); } x = sx; y = sy; cur = null; prev = cmd; continue; }
            switch (c)
            {
                case 'M':
                    if (!Number(out var mx) || !Number(out var my)) return result;
                    x = rel ? x + mx : mx; y = rel ? y + my : my; sx = x; sy = y;
                    cur = [(x, y)]; result.Add(cur);
                    break;
                case 'L':
                    {
                        if (!Number(out var lx) || !Number(out var ly)) return result;
                        x = rel ? x + lx : lx; y = rel ? y + ly : ly; Begin(); cur!.Add((x, y));
                        break;
                    }
                case 'H':
                    { if (!Number(out var hx)) return result; x = rel ? x + hx : hx; Begin(); cur!.Add((x, y)); break; }
                case 'V':
                    { if (!Number(out var vy)) return result; y = rel ? y + vy : vy; Begin(); cur!.Add((x, y)); break; }
                case 'C' or 'S':
                    {
                        double x1, y1;
                        if (c == 'C')
                        {
                            if (!Number(out x1) || !Number(out y1)) return result;
                            if (rel) { x1 += x; y1 += y; }
                        }
                        else if (char.ToUpperInvariant(prev) is 'C' or 'S') { x1 = 2 * x - cpx; y1 = 2 * y - cpy; }
                        else { x1 = x; y1 = y; }
                        if (!Number(out var x2) || !Number(out var y2) || !Number(out var x3) || !Number(out var y3)) return result;
                        if (rel) { x2 += x; y2 += y; x3 += x; y3 += y; }
                        Begin();
                        for (var s = 1; s <= 20; s++)
                        {
                            var t = s / 20.0; var u = 1 - t;
                            cur!.Add((u * u * u * x + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3, u * u * u * y + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3));
                        }
                        cpx = x2; cpy = y2; x = x3; y = y3;
                        break;
                    }
                case 'A':
                    {
                        if (!Number(out var rx) || !Number(out var ry) || !Number(out var rot) || !Flag(out var large) || !Flag(out var sweep) || !Number(out var ex) || !Number(out var ey)) return result;
                        if (rel) { ex += x; ey += y; }
                        Begin();
                        Arc(cur!, x, y, rx, ry, rot, large, sweep, ex, ey);
                        x = ex; y = ey;
                        break;
                    }
                default:
                    return result;                                                   // a command the icon file does not use: stop with what we have
            }
            prev = cmd;
            continue;

            void Begin() { if (cur is null) { cur = [(x, y)]; result.Add(cur); } }
        }
        return result;
    }

    // the arc of the SVG specification (F.6.5): from the end points to a centre, then points along it
    private static void Arc(List<(double X, double Y)> pts, double x1, double y1, double rx, double ry, double rotDeg, bool large, bool sweep, double x2, double y2)
    {
        if (rx == 0 || ry == 0 || (x1 == x2 && y1 == y2)) { pts.Add((x2, y2)); return; }
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        var phi = rotDeg * Math.PI / 180; double cosP = Math.Cos(phi), sinP = Math.Sin(phi);
        double dx = (x1 - x2) / 2, dy = (y1 - y2) / 2;
        double x1p = cosP * dx + sinP * dy, y1p = -sinP * dx + cosP * dy;
        var lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
        if (lambda > 1) { var k = Math.Sqrt(lambda); rx *= k; ry *= k; }
        var num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
        var den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        var coef = (large == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, num / den));
        double cxp = coef * rx * y1p / ry, cyp = -coef * ry * x1p / rx;
        double cx = cosP * cxp - sinP * cyp + (x1 + x2) / 2, cy = sinP * cxp + cosP * cyp + (y1 + y2) / 2;
        double Angle(double ux, double uy, double vx, double vy) => Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        var theta = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        var delta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && delta > 0) delta -= 2 * Math.PI; else if (sweep && delta < 0) delta += 2 * Math.PI;
        var steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 24)));
        for (var s = 1; s <= steps; s++)
        {
            var a = theta + delta * s / steps;
            pts.Add((cosP * rx * Math.Cos(a) - sinP * ry * Math.Sin(a) + cx, sinP * rx * Math.Cos(a) + cosP * ry * Math.Sin(a) + cy));
        }
    }

    // ---------- PNG ----------

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n => { var c = (uint)n; for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; return c; }).ToArray();

    private static byte[] Encode(int size, byte[] scanlines)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, size);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), size);
        header[8] = 8; header[9] = 6;                                                  // 8 bits, RGBA
        Chunk(ms, "IHDR", header);
        using (var z = new MemoryStream())
        {
            using (var zlib = new ZLibStream(z, CompressionLevel.SmallestSize, true)) zlib.Write(scanlines);
            Chunk(ms, "IDAT", z.ToArray());
        }
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(body);
        var crc = 0xFFFFFFFF;
        foreach (var b in body) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, crc ^ 0xFFFFFFFF);
        s.Write(len);
    }
}
