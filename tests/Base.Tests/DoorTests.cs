using System.Net;
using System.Net.Http.Headers;
using Base.Accounts;
using Base.Web;
using Base.Web.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Base.Tests;

/// <summary>SEC-07: an own door under /api that the app protects itself (a bearer key, no cookie, no Origin).</summary>
public class DoorTests
{
    private const string Key = "Bearer test-key-0001";
    private const string Origin = "http://localhost";

    private static ValueTask<bool> KeyOk(HttpContext ctx) => ValueTask.FromResult(ctx.Request.Headers.Authorization == Key);

    private static BaseAppOptions Options(params ApiDoor[] doors) => new("doortest", "Door Test", typeof(DoorTests).Assembly) { Doors = doors };

    /// <summary>A small app with the base and a few routes: two under the door, two outside it.</summary>
    private static async Task<WebApplication> StartAsync(TestDb db, BaseAppOptions options)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BASE_DB"] = db.ConnectionString, ["BASE_SETUP_CODE"] = TestApp.SetupCode, ["BASE_SECRET_KEY"] = TestApp.Key,
        });
        builder.AddBase(options);
        var app = builder.Build();
        await app.StartBaseAsync();
        app.UseBase();
        app.MapBase();
        app.MapMethods("/api/v1/assets/{id}", ["GET", "POST", "PUT", "PATCH", "DELETE"], async (string id, HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            return Results.Json(new { id, identity = ctx.Items.ContainsKey(ApiGuard.IdentityKey), length = body.Length });
        });
        app.MapGet("/api/v10/x", () => "outside");
        app.MapGet("/api/mine", (HttpContext ctx) => ctx.Person().UserId.ToString());
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app)
    {
        var c = app.GetTestClient();
        c.BaseAddress = new Uri(Origin);
        return c;
    }

    private static async Task<string> SignInAsync(HttpClient c)
    {
        var begin = await c.SendAsync(Req(HttpMethod.Post, "/api/setup/begin", null, new { code = TestApp.SetupCode, user = "jul", password = TestAccounts.Password }));
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        var secret = (await TestApp.Json(begin)).GetProperty("secret").GetString()!;
        var totp = Totp.Hotp(Base32.Decode(secret), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var finish = await c.SendAsync(Req(HttpMethod.Post, "/api/setup/finish", null, new { code = TestApp.SetupCode, user = "jul", totp }));
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        return TestApp.CookieOf(finish);
    }

    private static HttpRequestMessage Req(HttpMethod method, string url, string? cookie, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (method != HttpMethod.Get) { req.Headers.Add("X-Base", "1"); req.Headers.Add("Origin", Origin); }
        if (cookie is not null) req.Headers.Add("Cookie", cookie);
        return req;
    }

    private static HttpRequestMessage Door(HttpMethod method, string url = "/api/v1/assets/7", string? key = Key, string? cookie = null, HttpContent? content = null)
    {
        var req = new HttpRequestMessage(method, url) { Content = content };
        if (key is not null) req.Headers.TryAddWithoutValidation("Authorization", key);
        if (cookie is not null) req.Headers.Add("Cookie", cookie);
        return req;                                   // no X-Base, no Origin
    }

    private static readonly HttpMethod[] Methods = [HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete];

    [Fact]
    public async Task A_door_answers_401_without_a_passing_check_for_every_method()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = await StartAsync(db, Options(new ApiDoor("/api/v1", KeyOk, 1024)));
        var c = Client(app);
        foreach (var m in Methods)
        {
            foreach (var key in new string?[] { null, "Bearer wrong", "Bearer" })
            {
                var r = await c.SendAsync(Door(m, key: key));
                Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
                Assert.Equal("auth.none", (await TestApp.Json(r)).GetProperty("error").GetString());   // the base's error shape
            }
        }
    }

    [Fact]
    public async Task A_passing_check_needs_neither_the_header_nor_the_origin_and_sets_no_identity()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = await StartAsync(db, Options(new ApiDoor("/api/v1", KeyOk, 1024)));
        var c = Client(app);
        foreach (var m in Methods)
        {
            var r = await c.SendAsync(Door(m, content: m == HttpMethod.Get ? null : new StringContent("abc")));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var json = await TestApp.Json(r);
            Assert.Equal("7", json.GetProperty("id").GetString());
            Assert.False(json.GetProperty("identity").GetBoolean());
            Assert.Equal("no-store", r.Headers.CacheControl!.ToString());                      // the rest of UseBase still applies
            Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        }
    }

    [Fact]
    public async Task A_valid_session_cookie_alone_never_opens_a_door_and_is_not_read_inside_one()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = await StartAsync(db, Options(new ApiDoor("/api/v1", KeyOk, 1024)));
        var c = Client(app);
        var cookie = await SignInAsync(c);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Req(HttpMethod.Get, "/api/mine", cookie))).StatusCode);   // the cookie is good

        foreach (var m in Methods)
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Door(m, key: null, cookie: cookie))).StatusCode);

        // with the key and the cookie: the key opens it, the cookie is ignored (no identity, no rotated cookie sent back)
        var both = await c.SendAsync(Door(HttpMethod.Get, cookie: cookie));
        Assert.Equal(HttpStatusCode.OK, both.StatusCode);
        Assert.False((await TestApp.Json(both)).GetProperty("identity").GetBoolean());
        Assert.False(both.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Another_spelling_of_the_path_is_still_the_door()
    {
        // Routing ignores case, so /api/V1/... reaches the same endpoint. The door must cover it too: otherwise a session cookie would open it.
        await using var db = await TestDb.CreateAsync();
        await using var app = await StartAsync(db, Options(new ApiDoor("/api/v1", KeyOk, 1024)));
        var c = Client(app);
        var cookie = await SignInAsync(c);

        foreach (var url in new[] { "/api/V1/assets/7", "/API/v1/assets/7", "/Api/V1/Assets/7" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Door(HttpMethod.Get, url, key: null, cookie: cookie))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req(HttpMethod.Post, url, cookie))).StatusCode);   // with the header and the Origin of a browser
            var withKey = await c.SendAsync(Door(HttpMethod.Get, url, cookie: cookie));
            Assert.Equal(HttpStatusCode.OK, withKey.StatusCode);
            Assert.False((await TestApp.Json(withKey)).GetProperty("identity").GetBoolean());
        }
    }

    [Fact]
    public async Task A_passed_check_reaches_nothing_outside_the_door()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = await StartAsync(db, Options(new ApiDoor("/api/v1", KeyOk, 1024)));
        var c = Client(app);
        // whole segments: /api/v10 is not under /api/v1, and the base's own paths stay behind the session
        foreach (var url in new[] { "/api/v10/x", "/api/mine", "/api/people", "/api/instance-settings", "/api/auth/sessions", "/api/settings" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Door(HttpMethod.Get, url))).StatusCode);
        // case-sensitive like the rest: another spelling is not the door (and so needs a session)
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Door(HttpMethod.Get, "/api/V1/assets/7", key: null))).StatusCode);
        // a changing request outside the door still needs the header and the origin, with or without the key
        var post = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        post.Headers.TryAddWithoutValidation("Authorization", Key);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(post)).StatusCode);
    }

    [Fact]
    public async Task A_body_over_the_limit_of_the_door_is_refused_declared_and_while_reading()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = await StartAsync(db, Options(new ApiDoor("/api/v1", KeyOk, 10)));
        var c = Client(app);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Door(HttpMethod.Post, content: new StringContent("0123456789")))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.SendAsync(Door(HttpMethod.Post, content: new StringContent("0123456789X")))).StatusCode);
        // no declared length: cut off while the body is read
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.SendAsync(Door(HttpMethod.Post, content: new Unsized(new string('x', 500))))).StatusCode);
        // the limit is the door's own, not the 64 KiB of the rest
        await using var wide = await StartAsync(db, Options(new ApiDoor("/api/v1", KeyOk, 1 << 20)));
        Assert.Equal(HttpStatusCode.OK, (await Client(wide).SendAsync(Door(HttpMethod.Post, content: new StringContent(new string('x', 200_000))))).StatusCode);
    }

    [Fact]
    public async Task A_check_that_fails_lets_nobody_in()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = await StartAsync(db, Options(new ApiDoor("/api/v1", _ => throw new InvalidOperationException("boom"), 1024)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(app).SendAsync(Door(HttpMethod.Get))).StatusCode);
    }

    [Fact]
    public async Task An_app_without_doors_behaves_as_before()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = await StartAsync(db, Options());
        var c = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Door(HttpMethod.Get))).StatusCode);                                  // a key means nothing: needs a session
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Door(HttpMethod.Post, content: new StringContent("a")))).StatusCode);   // and the header
        var cookie = await SignInAsync(c);
        var ok = await c.SendAsync(Req(HttpMethod.Get, "/api/v1/assets/7", cookie));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True((await TestApp.Json(ok)).GetProperty("identity").GetBoolean());
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/")]
    [InlineData("/v1")]
    [InlineData("/api/v1/")]
    [InlineData("/api//v1")]
    [InlineData("/api/v 1")]
    [InlineData("/api/v1/../auth")]
    [InlineData("/api/{id}")]
    [InlineData("/API/v1")]
    [InlineData("/api/auth")]
    [InlineData("/api/auth/hub")]
    [InlineData("/api/setup")]
    [InlineData("/api/invite")]
    [InlineData("/api/account")]
    [InlineData("/api/account/x")]
    [InlineData("/api/health")]
    [InlineData("/api/about")]
    [InlineData("/api/people")]
    [InlineData("/api/people/x")]
    [InlineData("/api/settings")]
    [InlineData("/api/instance-settings")]
    [InlineData("/api/hub-settings")]
    public void A_door_cannot_sit_on_the_base_or_be_malformed(string prefix)
    {
        Assert.Throws<InvalidOperationException>(() => ApiDoor.Validate([new ApiDoor(prefix, KeyOk, 10)], ApiGuard.BasePublicPaths));
    }

    [Fact]
    public void A_door_needs_a_check_and_may_not_cover_declared_public_or_large_paths_or_another_door()
    {
        Assert.Throws<InvalidOperationException>(() => ApiDoor.Validate([new ApiDoor("/api/v1", null!, 10)], []));
        Assert.Throws<InvalidOperationException>(() => ApiDoor.Validate([new ApiDoor("/api/v1", KeyOk, 10)], ["/api/v1/open"]));
        Assert.Throws<InvalidOperationException>(() => ApiDoor.Validate([new ApiDoor("/api/v1", KeyOk, 10), new ApiDoor("/api/v1/x", KeyOk, 10)], []));
        ApiDoor.Validate([new ApiDoor("/api/v1", KeyOk, 10), new ApiDoor("/api/v10", KeyOk, 10), new ApiDoor("/api/devices/v2", KeyOk, 10)], ApiGuard.BasePublicPaths);   // fine
    }

    [Fact]
    public async Task The_start_refuses_a_wrong_door()
    {
        await using var db = await TestDb.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(db, Options(new ApiDoor("/api/auth", KeyOk, 10))));
    }

    private sealed class Unsized(string text) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text)).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
