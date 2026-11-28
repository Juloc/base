using System.Net;
using System.Text.Json;
using Base.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Base.Tests;

/// <summary>The self-description of an app and its health (CONTRACT-01, CONTRACT-02): anonymous, fixed fields, nothing private.</summary>
public class AboutTests
{
    [Fact]
    public async Task About_describes_the_app_without_a_session_and_holds_nothing_private()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var r = await c.GetAsync("/api/about");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("public, max-age=300", r.Headers.CacheControl!.ToString());
        var text = await r.Content.ReadAsStringAsync();
        var about = JsonDocument.Parse(text).RootElement;

        // exactly these fields, nothing else: a new field is a decision (the contract says "fixed fields only")
        Assert.Equal(["accentHue", "base", "contract", "description", "embed", "icon", "id", "name", "roles", "start", "version"], about.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(1, about.GetProperty("contract").GetInt32());
        Assert.Equal("startapp", about.GetProperty("id").GetString());
        Assert.Equal("Start App", about.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(about.GetProperty("description").GetProperty("en").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(about.GetProperty("description").GetProperty("de").GetString()));
        Assert.Equal("bookmark", about.GetProperty("icon").GetString());
        Assert.Equal(299, about.GetProperty("accentHue").GetInt32());
        Assert.False(about.GetProperty("embed").GetProperty("hub").GetBoolean());   // the start app does not allow a hub window
        Assert.Equal("/", about.GetProperty("start").GetString());
        Assert.Equal(["admin", "member", "viewer"], about.GetProperty("roles").EnumerateArray().Select(x => x.GetProperty("key").GetString()).ToArray());
        Assert.Equal("Administrator", about.GetProperty("roles")[0].GetProperty("name").GetProperty("en").GetString());
        Assert.Matches(@"^\d+\.\d+\.\d+(-[a-z]+\.\d+)?$", about.GetProperty("version").GetString()!);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[a-z]+\.\d+)?$", about.GetProperty("base").GetString()!);
        // no address of any kind, no secret, no database, no person
        Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BASE_", text);
        Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
        // and it needs no setup: a fresh installation answers it too (checked above: no account exists)
    }

    [Fact]
    public async Task About_is_limited_per_address_and_still_answers_after_the_window()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        for (var i = 0; i < Describe_PerMinute(); i++) Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/about")).StatusCode);
        var refused = await c.GetAsync("/api/about");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter!.Delta!.Value.TotalSeconds is > 0 and <= 60);
        Assert.Equal("too_many", (await TestApp.Json(refused)).GetProperty("error").GetString());
        // health is not limited (a container asks it every few seconds)
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/health")).StatusCode);
    }

    private static int Describe_PerMinute() => 60;

    [Fact]
    public async Task Health_tells_ok_degraded_or_down_with_the_contract_version()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var ok = await TestApp.Json(await c.GetAsync("/api/health"));
        Assert.Equal("ok", ok.GetProperty("status").GetString());
        Assert.Equal(1, ok.GetProperty("contract").GetInt32());
        Assert.True(ok.GetProperty("ok").GetBoolean());            // the first fields of the base stay
        Assert.Equal("startapp", ok.GetProperty("app").GetString());
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", ok.GetProperty("time").GetString());   // UTC with Z, whole seconds (contract, section 1)
        Assert.True(DateTimeOffset.TryParse(ok.GetProperty("time").GetString(), out var time));
        Assert.InRange((DateTimeOffset.UtcNow - time).TotalMinutes, -1, 1);

        // "it works, but something is wrong": still 200, never a reason in the answer
        var state = app.Services.GetRequiredService<HealthState>();
        state.Set("mail", true);
        var r = await c.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var text = await r.Content.ReadAsStringAsync();
        Assert.Equal("degraded", JsonDocument.Parse(text).RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("mail", text);
        state.Set("mail", false);
        Assert.Equal("ok", (await TestApp.Json(await c.GetAsync("/api/health"))).GetProperty("status").GetString());
    }
}
