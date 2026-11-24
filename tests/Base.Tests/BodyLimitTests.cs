using System.Net;
using System.Net.Http.Json;
using Base.Accounts;
using Base.Web;
using Base.Web.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Base.Tests;

/// <summary>SEC-04: an endpoint declares the body it takes (<c>WithMaxBody</c>), the guard applies it behind a session only.</summary>
public class BodyLimitTests
{
    private const string Origin = "http://localhost";
    private const int Limit = 1000;

    private static async Task<IResult> ReadBody(HttpContext ctx) => Results.Json(new { length = (await new StreamReader(ctx.Request.Body).ReadToEndAsync()).Length });

    private static async Task<WebApplication> StartAsync(TestDb db)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BASE_DB"] = db.ConnectionString, ["BASE_SETUP_CODE"] = TestApp.SetupCode, ["BASE_SECRET_KEY"] = TestApp.Key,
        });
        builder.AddBase(new BaseAppOptions("limittest", "Limit Test", typeof(BodyLimitTests).Assembly));
        var app = builder.Build();
        await app.StartBaseAsync();
        app.UseBase();
        app.MapBase();
        app.MapPut("/api/sites/{id}/files/{**path}", (Delegate)ReadBody).WithMaxBody(Limit);
        app.MapPost("/api/note", (Delegate)ReadBody);                                  // the neighbour: the default
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app)
    {
        var c = app.GetTestClient();
        c.BaseAddress = new Uri(Origin);
        return c;
    }

    private static HttpRequestMessage Put(string url, HttpContent content, string? cookie)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        req.Headers.Add("X-Base", "1");
        req.Headers.Add("Origin", Origin);
        if (cookie is not null) req.Headers.Add("Cookie", cookie);
        return req;
    }

    private static async Task<string> SignInAsync(HttpClient c)
    {
        HttpRequestMessage Post(string url, object body)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            r.Headers.Add("X-Base", "1"); r.Headers.Add("Origin", Origin);
            return r;
        }
        var begin = await c.SendAsync(Post("/api/setup/begin", new { code = TestApp.SetupCode, user = "jul", password = TestAccounts.Password }));
        var secret = (await TestApp.Json(begin)).GetProperty("secret").GetString()!;
        var totp = Totp.Hotp(Base32.Decode(secret), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        return TestApp.CookieOf(await c.SendAsync(Post("/api/setup/finish", new { code = TestApp.SetupCode, user = "jul", totp })));
    }

    private sealed class Unsized(string text) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text)).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    [Fact]
    public async Task An_endpoint_takes_the_body_it_declares_declared_and_chunked_and_not_more()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = await StartAsync(db);
        var c = Client(app);
        var cookie = await SignInAsync(c);
        const string url = "/api/sites/abc/files/css/site/main.css";

        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Put(url, new StringContent(new string('x', Limit)), cookie))).StatusCode);          // just under (exactly)
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Put(url, new Unsized(new string('x', Limit)), cookie))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.SendAsync(Put(url, new StringContent(new string('x', Limit + 1)), cookie))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.SendAsync(Put(url, new Unsized(new string('x', Limit + 1)), cookie))).StatusCode);
        // far above the default of 64 KiB is refused too, since the declared limit replaces the default in both directions
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.SendAsync(Put(url, new StringContent(new string('x', 70_000)), cookie))).StatusCode);
    }

    [Fact]
    public async Task A_larger_limit_than_the_default_is_possible_and_a_neighbour_keeps_the_default()
    {
        await using var db = await TestDb.CreateAsync();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["BASE_DB"] = db.ConnectionString, ["BASE_SETUP_CODE"] = TestApp.SetupCode, ["BASE_SECRET_KEY"] = TestApp.Key });
        builder.AddBase(new BaseAppOptions("limittest", "Limit Test", typeof(BodyLimitTests).Assembly));
        await using var app = builder.Build();
        await app.StartBaseAsync();
        app.UseBase();
        app.MapBase();
        app.MapPut("/api/big/{id}", (Delegate)ReadBody).WithMaxBody(5 << 20);
        app.MapPost("/api/note", (Delegate)ReadBody);
        await app.StartAsync();
        var c = Client(app);
        var cookie = await SignInAsync(c);

        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Put("/api/big/1", new StringContent(new string('x', 1 << 20)), cookie))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Put("/api/big/1", new Unsized(new string('x', 1 << 20)), cookie))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.SendAsync(Put("/api/big/1", new StringContent(new string('x', 6 << 20)), cookie))).StatusCode);

        // the neighbour without the declaration and a path that matches nothing keep the default
        var neighbour = new HttpRequestMessage(HttpMethod.Post, "/api/note") { Content = new StringContent(new string('x', 70_000)) };
        neighbour.Headers.Add("X-Base", "1"); neighbour.Headers.Add("Origin", Origin); neighbour.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.SendAsync(neighbour)).StatusCode);
        var nowhere = Put("/api/no/such/path", new StringContent(new string('x', 70_000)), cookie);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.SendAsync(nowhere)).StatusCode);
        var nowhereSmall = Put("/api/no/such/path", new StringContent("x"), cookie);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(nowhereSmall)).StatusCode);
    }

    [Fact]
    public async Task The_declared_limit_needs_a_session_and_the_usual_header()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = await StartAsync(db);
        var c = Client(app);
        const string url = "/api/sites/abc/files/a.css";
        var cookie = await SignInAsync(c);

        // without a session the answer is 401 whatever the size; the limit changes nothing about who may call
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Put(url, new StringContent("x"), null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Put(url, new StringContent(new string('x', 500)), "x_sid=nonsense"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Put(url, new StringContent(new string('x', 200_000)), null))).StatusCode);
        // the header and the origin are still asked
        var bare = new HttpRequestMessage(HttpMethod.Put, url) { Content = new StringContent("x") };
        bare.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(bare)).StatusCode);
    }

    [Fact]
    public void A_bad_limit_is_refused_when_the_endpoint_is_declared()
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        foreach (var bad in new long[] { 0, -1, long.MinValue, (1L << 30) + 1, long.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => app.MapPut("/api/x/{id}", () => "").WithMaxBody(bad));
        app.MapPut("/api/y/{id}", () => "").WithMaxBody(1L << 30);      // the ceiling itself is fine
        app.MapPut("/api/z/{id}", () => "").WithMaxBody(1);
    }
}
