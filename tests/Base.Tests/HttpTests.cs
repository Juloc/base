using System.Net;
using System.Text.Json;
using Base.Accounts;
using Base.Web;

namespace Base.Tests;

/// <summary>The start app over HTTP: headers, the guard of the API, the cookie, health, settings, and that a restart signs nobody out.</summary>
public class HttpTests
{
    [Fact]
    public async Task Every_answer_carries_the_security_headers()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        foreach (var url in new[] { "/", "/api/health", "/api/items", "/base/base.css", "/no/such/page" })
        {
            var r = await c.GetAsync(url);
            Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
            Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
            Assert.Equal(SecurityHeaders.Csp, r.Headers.GetValues("Content-Security-Policy").Single());
            Assert.Equal("same-origin", r.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
            Assert.False(r.Headers.Contains("Server"));
            if (url.StartsWith("/api", StringComparison.Ordinal)) Assert.Equal("no-store", r.Headers.CacheControl!.ToString());
        }
    }

    [Fact]
    public void The_content_policy_allows_only_the_app_itself()
    {
        var csp = SecurityHeaders.Csp;
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.DoesNotContain("http:", csp);
        Assert.DoesNotContain("https:", csp);
        Assert.DoesNotContain("*", csp);
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("style-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'none'", csp);
    }

    [Fact]
    public async Task Https_adds_strict_transport_and_the_host_cookie()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db, https: true);
        var c = app.Http();
        Assert.Equal("max-age=31536000", (await c.GetAsync("/api/health")).Headers.GetValues("Strict-Transport-Security").Single());

        var begin = await c.SendAsync(app.Req(HttpMethod.Post, "/api/setup/begin", null, new { code = TestApp.SetupCode, user = "jul", password = TestAccounts.Password }));
        var secret = (await TestApp.Json(begin)).GetProperty("secret").GetString()!;
        var finish = await c.SendAsync(app.Req(HttpMethod.Post, "/api/setup/finish", null, new { code = TestApp.SetupCode, user = "jul", totp = Totp.Hotp(Base32.Decode(secret), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30) }));
        var cookie = finish.Headers.GetValues("Set-Cookie").Single();
        Assert.StartsWith("__Host-startapp_sid=", cookie);
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Over_plain_http_the_cookie_is_still_http_only_and_same_site()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var begin = await c.SendAsync(app.Req(HttpMethod.Post, "/api/setup/begin", null, new { code = TestApp.SetupCode, user = "jul", password = TestAccounts.Password }));
        var secret = (await TestApp.Json(begin)).GetProperty("secret").GetString()!;
        var finish = await c.SendAsync(app.Req(HttpMethod.Post, "/api/setup/finish", null, new { code = TestApp.SetupCode, user = "jul", totp = Totp.Hotp(Base32.Decode(secret), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30) }));
        var cookie = finish.Headers.GetValues("Set-Cookie").Single();
        Assert.StartsWith("startapp_sid=", cookie);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        // the answer names the person, never the token or the authenticator secret
        var body = await finish.Content.ReadAsStringAsync();
        Assert.DoesNotContain(cookie.Split(';')[0].Split('=')[1], body);
        Assert.DoesNotContain(secret, body);
    }

    [Fact]
    public async Task The_api_needs_a_session_except_for_the_few_public_paths()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        foreach (var url in new[] { "/api/items", "/api/settings", "/api/instance-settings", "/api/auth/sessions", "/api/auth/history", "/api/no-such-endpoint" })
        {
            var r = await c.GetAsync(url);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal("auth.none", (await TestApp.Json(r)).GetProperty("error").GetString());
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(app.Req(HttpMethod.Post, "/api/items", null, new { title = "x" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(app.Req(HttpMethod.Post, "/api/auth/logout"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(app.Req(HttpMethod.Get, "/api/items", "startapp_sid=made-up-token"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/setup/status")).StatusCode);
        var me = await TestApp.Json(await c.GetAsync("/api/auth/me"));
        Assert.True(me.GetProperty("setupNeeded").GetBoolean());
        Assert.False(me.TryGetProperty("user", out _));
    }

    [Fact]
    public async Task A_changing_request_needs_the_own_header_and_the_own_origin()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var (cookie, _) = await app.SignedInAsync(c);

        HttpRequestMessage Post(Action<HttpRequestMessage> change)
        {
            var req = app.Req(HttpMethod.Post, "/api/items", cookie, new { title = "milk" });
            change(req);
            return req;
        }

        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Post(_ => { }))).StatusCode);
        var noHeader = await c.SendAsync(Post(r => r.Headers.Remove("X-Base")));
        Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);
        Assert.Equal("request.refused", (await TestApp.Json(noHeader)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Post(r => r.Headers.Remove("Origin")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Post(r => { r.Headers.Remove("Origin"); r.Headers.Add("Origin", "http://evil.example"); }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Post(r => { r.Headers.Remove("Origin"); r.Headers.Add("Origin", "http://localhost.evil.example"); }))).StatusCode);
        // also the public paths that change something
        var login = app.Req(HttpMethod.Post, "/api/auth/login", null, new { user = "jul", password = "x", code = "1" });
        login.Headers.Remove("X-Base");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(login)).StatusCode);

        Assert.Equal(1L, await db.ScalarAsync<long>("select count(*) from items"));
    }

    [Fact]
    public async Task Bodies_are_small()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var (cookie, _) = await app.SignedInAsync(c);
        var big = await c.SendAsync(app.Req(HttpMethod.Post, "/api/items", cookie, new { title = "x", note = new string('a', 70_000) }));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        var bigLogin = await c.SendAsync(app.Req(HttpMethod.Post, "/api/auth/login", null, new { user = "jul", password = new string('a', 5000), code = "1" }));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, bigLogin.StatusCode);
    }

    [Fact]
    public async Task Health_tells_the_versions_and_whether_the_database_answers()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var health = await TestApp.Json(await app.Http().GetAsync("/api/health"));
        Assert.True(health.GetProperty("ok").GetBoolean());
        Assert.True(health.GetProperty("db").GetBoolean());
        Assert.Equal("startapp", health.GetProperty("app").GetString());
        Assert.Matches(@"^\d+\.\d+\.\d+", health.GetProperty("version").GetString()!);
        Assert.Equal(Repo.Read("VERSION").Trim(), health.GetProperty("base").GetString());
    }

    [Fact]
    public async Task A_restart_of_the_server_signs_nobody_out()
    {
        await using var db = await TestDb.CreateAsync();
        string cookie;
        await using (var first = new TestApp(db))
        {
            (cookie, _) = await first.SignedInAsync(first.Http());
            Assert.Equal("jul", (await TestApp.Json(await first.Http().SendAsync(first.Req(HttpMethod.Get, "/api/auth/me", cookie)))).GetProperty("user").GetString());
        }
        // a new process: nothing of the session lives in memory or hangs on a key made at start
        await using var second = new TestApp(db);
        var me = await TestApp.Json(await second.Http().SendAsync(second.Req(HttpMethod.Get, "/api/auth/me", cookie)));
        Assert.Equal("jul", me.GetProperty("user").GetString());
        Assert.Equal(HttpStatusCode.OK, (await second.Http().SendAsync(second.Req(HttpMethod.Get, "/api/items", cookie))).StatusCode);
    }

    [Fact]
    public async Task Sign_out_ends_the_session_on_the_server()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var (cookie, _) = await app.SignedInAsync(c);
        var r = await c.SendAsync(app.Req(HttpMethod.Post, "/api/auth/logout", cookie));
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Contains("max-age=0", r.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(app.Req(HttpMethod.Get, "/api/items", cookie))).StatusCode);       // the old cookie is worth nothing
        Assert.Equal(0L, await db.ScalarAsync<long>("select count(*) from sessions"));
    }

    [Fact]
    public async Task A_rotated_session_sends_the_new_cookie_with_the_answer()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var (cookie, _) = await app.SignedInAsync(c);
        Assert.False((await c.SendAsync(app.Req(HttpMethod.Get, "/api/items", cookie))).Headers.Contains("Set-Cookie"));
        await db.ExecAsync("update sessions set rotated_at = now() - interval '13 hours'");
        var r = await c.SendAsync(app.Req(HttpMethod.Get, "/api/items", cookie));
        var fresh = TestApp.CookieOf(r);
        Assert.NotEqual(cookie, fresh);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(app.Req(HttpMethod.Get, "/api/items", fresh))).StatusCode);
    }

    [Fact]
    public async Task Settings_accept_only_known_keys_and_checked_values()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var (cookie, _) = await app.SignedInAsync(c);
        Task<HttpResponseMessage> Put(string key, object? value) => c.SendAsync(app.Req(HttpMethod.Put, "/api/settings/" + key, cookie, new { value }));

        Assert.Equal(HttpStatusCode.NoContent, (await Put("lang", "en")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Put("scheme", "dark")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Put("items.hideDone", true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put("lang", "klingon")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put("scheme", 5)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put("items.hideDone", "yes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put("anything.else", "x")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put("proxy.trusted", new[] { "192.0.2.1" })).StatusCode);       // an instance setting is not a personal one

        var all = (await TestApp.Json(await c.SendAsync(app.Req(HttpMethod.Get, "/api/settings", cookie)))).GetProperty("settings");
        Assert.Equal("en", all.GetProperty("lang").GetString());
        Assert.Equal("dark", all.GetProperty("scheme").GetString());
        Assert.True(all.GetProperty("items.hideDone").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await c.SendAsync(app.Req(HttpMethod.Delete, "/api/settings/lang", cookie))).StatusCode);
        all = (await TestApp.Json(await c.SendAsync(app.Req(HttpMethod.Get, "/api/settings", cookie)))).GetProperty("settings");
        Assert.False(all.TryGetProperty("lang", out _));
    }

    [Fact]
    public async Task Instance_settings_need_the_role_admin()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var (cookie, _) = await app.SignedInAsync(c);
        Task<HttpResponseMessage> Put(object? value) => c.SendAsync(app.Req(HttpMethod.Put, "/api/instance-settings/proxy.trusted", cookie, new { value }));

        Assert.Equal(HttpStatusCode.NoContent, (await Put(new[] { "192.0.2.10", "198.51.100.0/24" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(new[] { "not an address" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put("192.0.2.10")).StatusCode);
        var stored = (await TestApp.Json(await c.SendAsync(app.Req(HttpMethod.Get, "/api/instance-settings", cookie)))).GetProperty("settings").GetProperty("proxy.trusted");
        Assert.Equal(2, stored.GetArrayLength());

        await db.ExecAsync("update users set roles = '{}'");
        var refused = await Put(new[] { "192.0.2.11" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("role.missing", (await TestApp.Json(refused)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(app.Req(HttpMethod.Get, "/api/instance-settings", cookie))).StatusCode);
    }

    [Fact]
    public void Only_a_trusted_proxy_is_believed()
    {
        var trust = new ProxyTrust();
        Assert.True(trust.Unset);
        Assert.True(trust.IsTrusted(IPAddress.Parse("10.0.0.1")));               // not set: every private network
        Assert.True(trust.IsTrusted(IPAddress.Loopback));
        Assert.False(trust.IsTrusted(IPAddress.Parse("203.0.113.9")));              // never the open internet
        Assert.False(trust.IsTrusted(null));

        trust.Apply("""["192.0.2.10", "2001:db8::/32"]""");
        Assert.False(trust.Unset);
        Assert.True(trust.IsTrusted(IPAddress.Parse("192.0.2.10")));
        Assert.True(trust.IsTrusted(IPAddress.Parse("::ffff:192.0.2.10")));
        Assert.True(trust.IsTrusted(IPAddress.Parse("2001:db8::1")));
        Assert.False(trust.IsTrusted(IPAddress.Parse("10.0.0.1")));
        Assert.False(trust.IsTrusted(IPAddress.Parse("192.0.2.11")));

        trust.Apply("[]");
        Assert.True(trust.Unset);
    }

    [Fact]
    public async Task Errors_have_one_shape_with_a_code_and_the_wrong_fields()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var (cookie, _) = await app.SignedInAsync(c);
        var r = await c.SendAsync(app.Req(HttpMethod.Post, "/api/items", cookie, new { title = "  ", note = "fine" }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await TestApp.Json(r);
        Assert.Equal("invalid", body.GetProperty("error").GetString());
        Assert.Equal("required", body.GetProperty("fields").GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("message").ValueKind);

        var missing = await c.SendAsync(app.Req(HttpMethod.Get, "/api/items/" + Guid.NewGuid(), cookie));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("not_found", (await TestApp.Json(missing)).GetProperty("error").GetString());

        var broken = app.Req(HttpMethod.Post, "/api/items", cookie);
        broken.Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(broken)).StatusCode);
    }

    [Fact]
    public async Task A_person_sees_and_changes_only_their_own_rows()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var (cookie, _) = await app.SignedInAsync(c);
        var created = await TestApp.Json(await c.SendAsync(app.Req(HttpMethod.Post, "/api/items", cookie, new { title = "mine" })));
        var id = created.GetProperty("id").GetString()!;

        // a second person, made directly in the database (the base has no user management page yet)
        var users = new UserStore(db.Data, new SecretBox(TestApp.Key));
        var other = await users.CreateAsync("eve", "argon2id$x", Totp.NewSecret(), confirmed: true, [], [], replacePending: false);
        var token = await new SessionStore(db.Data).CreateAsync(other.Id, null, null);
        var eve = "startapp_sid=" + token;

        Assert.Equal(0, (await TestApp.Json(await c.SendAsync(app.Req(HttpMethod.Get, "/api/items", eve)))).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(app.Req(HttpMethod.Get, "/api/items/" + id, eve))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(app.Req(HttpMethod.Put, "/api/items/" + id, eve, new { title = "taken" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(app.Req(HttpMethod.Delete, "/api/items/" + id, eve))).StatusCode);
        Assert.Equal("mine", (await TestApp.Json(await c.SendAsync(app.Req(HttpMethod.Get, "/api/items/" + id, cookie)))).GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_list_comes_in_pages_newest_first()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var (cookie, _) = await app.SignedInAsync(c);
        for (var i = 1; i <= 5; i++)
        {
            await c.SendAsync(app.Req(HttpMethod.Post, "/api/items", cookie, new { title = "item " + i }));
            await Task.Delay(2);
        }
        var first = await TestApp.Json(await c.SendAsync(app.Req(HttpMethod.Get, "/api/items?limit=2", cookie)));
        Assert.Equal(["item 5", "item 4"], first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()));
        var next = first.GetProperty("next").GetString();
        var second = await TestApp.Json(await c.SendAsync(app.Req(HttpMethod.Get, "/api/items?limit=2&cursor=" + next, cookie)));
        Assert.Equal(["item 3", "item 2"], second.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()));
        var last = await TestApp.Json(await c.SendAsync(app.Req(HttpMethod.Get, "/api/items?limit=2&cursor=" + second.GetProperty("next").GetString(), cookie)));
        Assert.Single(last.GetProperty("items").EnumerateArray());
        Assert.False(last.TryGetProperty("next", out _));
        var found = await TestApp.Json(await c.SendAsync(app.Req(HttpMethod.Get, "/api/items?q=TEM%203", cookie)));
        Assert.Equal("item 3", found.GetProperty("items").EnumerateArray().Single().GetProperty("title").GetString());
    }
}
