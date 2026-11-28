using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Base.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Base.Tests;

/// <summary>The installable frame (0.4): manifest, icons and service worker are made by the base from <see cref="BaseAppOptions"/>; SEC-08 holds for the worker.</summary>
public class PwaTests
{
    private static BaseAppOptions Options(Action<BaseAppOptions>? _ = null) => new("demo", "Demo App", typeof(Program).Assembly);

    [Fact]
    public async Task The_manifest_is_served_from_the_options_and_names_installable_icons()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var r = await c.GetAsync("/manifest.webmanifest");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/manifest+json", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-cache", r.Headers.CacheControl!.ToString());
        var m = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Start App", m.GetProperty("name").GetString());
        Assert.Equal("Start App", m.GetProperty("short_name").GetString());
        Assert.Equal("/", m.GetProperty("start_url").GetString());
        Assert.Equal("standalone", m.GetProperty("display").GetString());
        Assert.Matches("^#[0-9a-f]{6}$", m.GetProperty("theme_color").GetString()!);
        Assert.Matches("^#[0-9a-f]{6}$", m.GetProperty("background_color").GetString()!);
        var icons = m.GetProperty("icons").EnumerateArray().ToArray();
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "192x192" && i.GetProperty("type").GetString() == "image/png");
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "512x512" && i.GetProperty("purpose").GetString() == "any");
        Assert.Contains(icons, i => i.GetProperty("purpose").GetString() == "maskable");

        // every icon the manifest and the page name exists
        foreach (var src in icons.Select(i => i.GetProperty("src").GetString()!).Append("/apple-touch-icon.png"))
        {
            var icon = await c.GetAsync(src);
            Assert.Equal(HttpStatusCode.OK, icon.StatusCode);
            Assert.Equal(src.EndsWith(".svg") ? "image/svg+xml" : "image/png", icon.Content.Headers.ContentType!.MediaType);
        }

        // the browser asks again with the ETag and gets "not modified"
        var etag = r.Headers.ETag!.Tag;
        var again = new HttpRequestMessage(HttpMethod.Get, "/manifest.webmanifest");
        again.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await c.SendAsync(again)).StatusCode);
    }

    [Fact]
    public async Task A_file_of_the_same_name_in_the_apps_wwwroot_wins_over_the_made_one()
    {
        var root = Directory.CreateTempSubdirectory("base-webroot-");
        try
        {
            const string own = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><!-- the app's own artwork --></svg>";
            File.WriteAllText(Path.Combine(root.FullName, "icon.svg"), own);
            File.WriteAllText(Path.Combine(root.FullName, "sw.js"), "// the app's own worker");
            await using var db = await TestDb.CreateAsync();
            await using var app = new TestApp(db) { WebRoot = root.FullName };
            var c = app.Http();
            Assert.Equal(own, await c.GetStringAsync("/icon.svg"));
            Assert.Equal("// the app's own worker", await c.GetStringAsync("/sw.js"));
            // what the app does not bring is still made by the base
            Assert.DoesNotContain("own artwork", await c.GetStringAsync("/icon-maskable.svg"));
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/manifest.webmanifest")).StatusCode);
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public async Task The_png_icons_are_real_pictures_of_the_right_size_with_the_mark_drawn()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        foreach (var (path, size) in new[] { ("/icon-192.png", 192), ("/icon-512.png", 512), ("/icon-maskable-512.png", 512), ("/apple-touch-icon.png", 180) })
        {
            var png = await c.GetByteArrayAsync(path);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
            Assert.Equal(size, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
            Assert.Equal(size, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
            // inflate the one data chunk: every row has its filter byte and size x 4 bytes, and the picture has more than one colour (background and mark)
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));          // IHDR is 25 bytes after the signature; the next chunk is IDAT
            Assert.Equal("IDAT", System.Text.Encoding.ASCII.GetString(png, 37, 4));
            using var z = new ZLibStream(new MemoryStream(png, 41, length), CompressionMode.Decompress);
            using var raw = new MemoryStream();
            await z.CopyToAsync(raw);
            var data = raw.ToArray();
            Assert.Equal(size * (size * 4 + 1), data.Length);
            var colours = new HashSet<int>();
            for (var y = 0; y < size; y += 3) for (var x = 0; x < size; x += 3) colours.Add(BitConverter.ToInt32(data, y * (size * 4 + 1) + 1 + x * 4));
            Assert.True(colours.Count > 3, $"{path} shows no mark");
        }
        Assert.Contains("<svg", await c.GetStringAsync("/icon.svg"));
        Assert.Contains("<path", await c.GetStringAsync("/icon-maskable.svg"));        // the glyph of the app's icon ("bookmark") is in it
    }

    [Fact]
    public void The_colours_come_from_the_accent_and_the_text_on_it_is_black_or_white()
    {
        var violet = Pwa.Colours(Options() with { AccentHue = 299 });
        Assert.Equal((1.0, 1.0, 1.0), violet.On);                                         // a dark violet takes white
        var cyan = Pwa.Colours(Options() with { AccentColor = "#00e5ff" });
        Assert.Equal("#00e5ff", Pwa.Hex(cyan.Fill));
        Assert.Equal((0.0, 0.0, 0.0), cyan.On);                                           // a light colour takes black
        // the page colour is a near white of the same hue
        var (_, _, page) = cyan;
        Assert.True(page.R > 0.9 && page.G > 0.9 && page.B > 0.9);
        Assert.True(page.R < page.B);                                                    // tinted towards cyan, not red
        // the conversion round-trips a known colour: oklch(0.7 0.1 150) is a calm green
        var green = Pwa.FromOklch(0.7, 0.1, 150);
        Assert.True(green.G > green.R && green.G > green.B);
    }

    [Fact]
    public void A_short_name_fits_under_an_icon_and_the_options_are_checked_at_start()
    {
        Assert.Equal("Caddy", Pwa.ShortName("Caddy"));
        Assert.Equal("Proxy", Pwa.ShortName("Proxy Manager Deluxe"));
        Assert.Equal("Supercalifra", Pwa.ShortName("Supercalifragilistic"));
        Assert.Throws<InvalidOperationException>(() => Pwa.Check(Options() with { AccentColor = "cyan" }));
        Assert.Throws<InvalidOperationException>(() => Pwa.Check(Options() with { ShellPaths = ["/api/items"] }));
        Assert.Throws<InvalidOperationException>(() => Pwa.Check(Options() with { ShellPaths = ["/../etc"] }));
        Pwa.Check(Options() with { ShellPaths = ["/fonts/", "/logo.png"], AccentColor = "#00E5FF" });
    }

    [Fact]
    public void Glyph_paths_are_flattened_with_lines_curves_and_arcs()
    {
        var square = Raster.Flatten("M2 2h10v10H2z").Single();
        Assert.Equal([(2.0, 2.0), (12.0, 2.0), (12.0, 12.0), (2.0, 12.0), (2.0, 2.0)], square);
        var arc = Raster.Flatten("M0 12a12 12 0 0 1 24 0").Single();                    // half a circle: its middle point is at the top
        Assert.Equal(0, arc[0].X, 3);
        Assert.Equal(24, arc[^1].X, 3);
        Assert.Contains(arc, p => Math.Abs(p.X - 12) < 0.5 && Math.Abs(p.Y) < 0.5);
        Assert.NotEmpty(Raster.Flatten("M4 4c0 8 8 8 8 0s8-8 8 0"));
    }

    [Fact]
    public async Task The_service_worker_keeps_only_the_shell_and_is_named_after_the_versions()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var r = await c.GetAsync("/sw.js");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/javascript", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-cache", r.Headers.CacheControl!.ToString());
        var sw = await r.Content.ReadAsStringAsync();
        var info = app.Services.GetRequiredService<AppInfo>();
        // SEC-08: a short list of static files, everything else passes through untouched
        Assert.Contains("const SHELL = new Set(", sw);
        Assert.Contains("!isShell(url.pathname)", sw);
        Assert.DoesNotContain("'/api", sw);
        Assert.DoesNotContain("addEventListener('install'", sw);                          // a new worker waits until the person agrees
        Assert.Contains("e.data === 'skip-waiting'", sw);
        // the cache has the name of the app, its version and the version of the base; the others are deleted when the worker takes over
        Assert.Contains($"const CACHE = 'startapp-{info.Version}", sw);
        Assert.Contains($"-base{info.BaseVersion}'", sw);
        Assert.Contains("keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))", sw);
        // a new version of the app or of the base is a new cache
        var other = Pwa.ServiceWorker(info with { Version = "9.9.9" }, Options());
        Assert.NotEqual(Pwa.ServiceWorker(info with { Version = "1.0.0" }, Options()), other);
        // the app's own static files go onto the list through the options
        Assert.Contains("'/fonts/'", Pwa.ServiceWorker(info, Options() with { ShellPaths = ["/fonts/", "/logo.png"] }));
        Assert.Contains("'/logo.png'", Pwa.ServiceWorker(info, Options() with { ShellPaths = ["/fonts/", "/logo.png"] }));
    }
}
