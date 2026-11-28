using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Base.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace Base.Tests;

/// <summary>
/// "Sign in through the hub" against the small hub of the tests (<see cref="TestHub"/>): the whole flow, and every way it must fail (AUTH-13, AUTH-14).
/// What is not tested here: JulOS itself. These tests prove the app side against the standard; a run against the real hub is a manual check.
/// </summary>
public class HubSignInTests
{
    /// <summary>An app with an admin who has set up the hub (address, client id, secret, on), and the test hub.</summary>
    private sealed class World : IAsyncDisposable
    {
        public required TestDb Db;
        public required TestApp App;
        public required TestHub Hub;
        public required HttpClient Http;
        public required string Admin;

        public static async Task<World> CreateAsync(bool configure = true, bool enabled = true, bool embeddable = false)
        {
            var db = await TestDb.CreateAsync();
            var app = new TestApp(db) { Options = embeddable ? o => o with { Embeddable = true } : null };
            var hub = await TestHub.StartAsync();
            var http = app.Http();
            var (admin, _) = await app.SignedInAsync(http);
            var w = new World { Db = db, App = app, Hub = hub, Http = http, Admin = admin };
            if (configure) await w.ConfigureAsync(enabled);
            return w;
        }

        public async Task ConfigureAsync(bool enabled = true, string? secret = TestHub.Secret)
        {
            var r = await Http.SendAsync(App.Req(HttpMethod.Put, "/api/hub-settings", Admin, new { url = Hub.Url, clientId = TestHub.ClientId, secret, enabled }));
            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        }

        public async Task<JsonElement> GetAsync(string url, string? cookie = null) => await TestApp.Json(await Http.SendAsync(App.Req(HttpMethod.Get, url, cookie)));

        public async Task<long> CountAsync(string sql) => await Db.ScalarAsync<long>(sql);

        /// <summary>The browser's part: begin at the app, go to the hub, come back with the code. Returns where the app sends the browser and the new session cookie, if any.</summary>
        public async Task<(string Location, string? Session)> SignInAsync(string sub = "sub-anna", string? role = "member", string? name = "anna", string sid = "sid-1",
            Action<Dictionary<string, object>>? tamper = null, ECDsa? signWith = null, string? alg = null, Func<string, string>? changeCallback = null, bool sendFlowCookie = true)
        {
            var begin = await Http.SendAsync(App.Req(HttpMethod.Post, "/api/auth/hub/begin"));
            Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
            var flowCookie = begin.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("startapp_hubflow=", StringComparison.Ordinal));
            var callback = Hub.Authorize((await TestApp.Json(begin)).GetProperty("url").GetString()!, sub, role, name, sid, tamper, signWith, alg);
            if (changeCallback is not null) callback = changeCallback(callback);
            return await CallbackAsync(callback, sendFlowCookie ? flowCookie.Split(';')[0] : null);
        }

        public async Task<(string Location, string? Session)> CallbackAsync(string callbackUrl, string? flowCookie)
        {
            var uri = new Uri(callbackUrl);
            var req = new HttpRequestMessage(HttpMethod.Get, uri.PathAndQuery);
            if (flowCookie is not null) req.Headers.Add("Cookie", flowCookie);
            var r = await Http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            var session = r.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.FirstOrDefault(c => c.StartsWith("startapp_sid=", StringComparison.Ordinal) && !c.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)) : null;
            return (r.Headers.Location!.ToString(), session?.Split(';')[0]);
        }

        public async Task<HttpResponseMessage> BackChannelAsync(string? logoutToken)
        {
            // as the hub's server sends it: a form, no cookie, no header of the app's own pages
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/hub/backchannel-logout")
            { Content = new FormUrlEncodedContent(logoutToken is null ? [] : [new KeyValuePair<string, string>("logout_token", logoutToken)]) };
            return await Http.SendAsync(req);
        }

        public async Task<string?> UserOf(string cookie) => (await GetAsync("/api/auth/me", cookie)).TryGetProperty("user", out var u) ? u.GetString() : null;

        public async ValueTask DisposeAsync() { await Hub.DisposeAsync(); await App.DisposeAsync(); await Db.DisposeAsync(); }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ---- the flow ----

    [Fact]
    public async Task A_person_of_the_hub_signs_in_gets_their_own_session_and_the_role_of_the_hub()
    {
        await using var w = await World.CreateAsync();
        // the page offers the button, with the name the hub gives itself; it knows nothing of the address
        var offer = await w.GetAsync("/api/auth/me");
        Assert.Equal("Test Hub", offer.GetProperty("hub").GetProperty("name").GetString());
        Assert.DoesNotContain(w.Hub.Url, offer.ToString());

        var (location, session) = await w.SignInAsync("sub-anna", "member");
        Assert.Equal("/", location);
        Assert.NotNull(session);
        var me = await w.GetAsync("/api/auth/me", session);
        Assert.Equal("anna", me.GetProperty("user").GetString());
        Assert.Equal(["member"], me.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.True(me.GetProperty("viaHub").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await w.Http.SendAsync(w.App.Req(HttpMethod.Get, "/api/items", session))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Http.SendAsync(w.App.Req(HttpMethod.Get, "/api/people", session))).StatusCode);

        // what was kept: the own session with the sid of the hub, the sub on the person. No token of the hub, the flow is gone.
        Assert.Equal("sid-1", await w.Db.ScalarAsync<string>("select s.hub_sid from sessions s join users u on u.id = s.user_id where u.name = 'anna'"));
        Assert.Equal("sub-anna", await w.Db.ScalarAsync<string>("select hub_sub from users where name = 'anna'"));
        Assert.Equal(0, await w.CountAsync("select count(*) from hub_flows"));
        // of the hub's tokens only the ID token stays, on the session, as the hint for its sign-out; no access token, no secret of a person
        Assert.Equal(0, await w.CountAsync("select count(*) from information_schema.columns where table_schema = current_schema() and column_name ilike '%token%' and table_name not in ('invites','hub_logout_tokens')"));
        Assert.Equal(w.Hub.LastIdToken, await w.Db.ScalarAsync<string>("select hub_hint from sessions where hub_sid = 'sid-1'"));
        // the session cookie is the app's own: random, and only its hash is in the table
        Assert.DoesNotContain(session!.Split('=')[1], await w.Db.ScalarAsync<string>("select string_agg(hash, ',') from sessions") ?? "");
        // the hub saw what a strict client sends: the verifier of PKCE, the code once
        var posted = Assert.Single(w.Hub.TokenRequests);
        Assert.Equal("authorization_code", posted["grant_type"]);
        Assert.True(posted["code_verifier"].Length >= 43);
        Assert.Equal("http://localhost/api/auth/hub/callback", posted["redirect_uri"]);
        Assert.DoesNotContain("client_secret", posted.Keys);                    // the secret travels in the header, not in the body
    }

    [Fact]
    public async Task The_role_comes_from_the_hub_at_every_sign_in_and_cannot_be_set_here()
    {
        await using var w = await World.CreateAsync();
        var (_, first) = await w.SignInAsync("sub-anna", "viewer");
        Assert.Equal("viewer", (await w.GetAsync("/api/auth/me", first)).GetProperty("roles")[0].GetString());
        var (_, second) = await w.SignInAsync("sub-anna", "admin", sid: "sid-2");
        Assert.Equal("admin", (await w.GetAsync("/api/auth/me", second)).GetProperty("roles")[0].GetString());
        Assert.Equal(1, await w.CountAsync("select count(*) from users where hub_sub = 'sub-anna'"));         // the same person, not a second one
        // an admin of the hub administers this app too
        Assert.Equal(HttpStatusCode.OK, (await w.Http.SendAsync(w.App.Req(HttpMethod.Get, "/api/people", second))).StatusCode);
        // here nobody can overrule the hub: the role of such a person is not changed locally
        var id = await w.Db.ScalarAsync<Guid>("select id from users where hub_sub = 'sub-anna'");
        var refused = await w.Http.SendAsync(w.App.Req(HttpMethod.Put, $"/api/people/{id}/role", w.Admin, new { role = "viewer" }));
        Assert.Equal("people.managed_by_hub", (await TestApp.Json(refused)).GetProperty("error").GetString());
        // and the listing says where the person comes from
        var people = await w.GetAsync("/api/people", w.Admin);
        Assert.True(people.GetProperty("people").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "anna").GetProperty("viaHub").GetBoolean());
    }

    [Fact]
    public async Task A_role_the_app_does_not_know_means_no_access_and_creates_nobody()
    {
        await using var w = await World.CreateAsync();
        var before = await w.CountAsync("select count(*) from users");
        foreach (var role in new string?[] { "superuser", "Admin ", "", null })
        {
            var (location, session) = await w.SignInAsync("sub-x", role);
            Assert.Equal("/?hub_error=hub.no_access", location);
            Assert.Null(session);
        }
        Assert.Equal(before, await w.CountAsync("select count(*) from users"));
        Assert.Equal(0, await w.CountAsync("select count(*) from sessions where hub_sid is not null"));
    }

    [Fact]
    public async Task A_person_of_the_hub_is_never_joined_to_a_local_account_by_name()
    {
        await using var w = await World.CreateAsync();
        // the local admin is called "jul": the hub person with the same name becomes a second, separate person
        var (_, session) = await w.SignInAsync("sub-jul", "admin", name: "jul");
        var me = await w.GetAsync("/api/auth/me", session);
        Assert.NotEqual("jul", me.GetProperty("user").GetString());
        Assert.Equal(2, await w.CountAsync("select count(*) from users"));
        Assert.Equal(1, await w.CountAsync("select count(*) from users where hub_sub is null and name = 'jul'"));
        // and such a person has no password here: the form refuses any try with their name
        var name = me.GetProperty("user").GetString();
        var login = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/auth/login", null, new { user = name, password = TestAccounts.Password, code = "123456" }));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        // the local account did not become reachable through the hub
        Assert.Equal("jul", await w.UserOf(w.Admin));
    }

    [Fact]
    public async Task A_person_blocked_here_gets_no_session_from_the_hub()
    {
        await using var w = await World.CreateAsync();
        var (_, session) = await w.SignInAsync("sub-anna", "member");
        var id = await w.Db.ScalarAsync<Guid>("select id from users where hub_sub = 'sub-anna'");
        Assert.Equal(HttpStatusCode.NoContent, (await w.Http.SendAsync(w.App.Req(HttpMethod.Post, $"/api/people/{id}/block", w.Admin))).StatusCode);
        Assert.Null(await w.UserOf(session!));
        var (location, again) = await w.SignInAsync("sub-anna", "member", sid: "sid-9");
        Assert.Equal("/?hub_error=hub.no_access", location);
        Assert.Null(again);
    }

    // ---- what must fail ----

    [Fact]
    public async Task A_wrong_state_a_missing_cookie_and_a_repeated_callback_sign_nobody_in()
    {
        await using var w = await World.CreateAsync();
        // a state that was not issued here
        var (location, session) = await w.SignInAsync(changeCallback: u => System.Text.RegularExpressions.Regex.Replace(u, "state=[^&]+", "state=forged"));
        Assert.Equal("/?hub_error=hub.state", location);
        Assert.Null(session);
        // another browser: the flow cookie that bound the sign-in to the browser that began is missing (login CSRF: a code of somebody else)
        (location, session) = await w.SignInAsync(sendFlowCookie: false);
        Assert.Equal("/?hub_error=hub.state", location);
        Assert.Null(session);
        // a cookie of another flow does not do either
        var begin = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/auth/hub/begin"));
        var callback = w.Hub.Authorize((await TestApp.Json(begin)).GetProperty("url").GetString()!, "sub-anna", "member");
        (location, session) = await w.CallbackAsync(callback, "startapp_hubflow=" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
        Assert.Equal("/?hub_error=hub.state", location);
        Assert.Null(session);
        // the right browser, once: works; the same address again (a replay, a reload): refused
        var ok = await w.SignInAsync(sid: "sid-ok");
        Assert.NotNull(ok.Session);
        var begin2 = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/auth/hub/begin"));
        var cookie2 = begin2.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("startapp_hubflow=", StringComparison.Ordinal)).Split(';')[0];
        var url2 = w.Hub.Authorize((await TestApp.Json(begin2)).GetProperty("url").GetString()!, "sub-anna", "member", sid: "sid-2");
        Assert.NotNull((await w.CallbackAsync(url2, cookie2)).Session);
        var replay = await w.CallbackAsync(url2, cookie2);
        Assert.Equal("/?hub_error=hub.state", replay.Location);
        Assert.Null(replay.Session);
        Assert.Equal(1, await w.CountAsync("select count(*) from hub_flows"));       // only the one with the forged state was never taken; it runs out by itself
    }

    [Fact]
    public async Task A_flow_that_ran_out_is_refused()
    {
        await using var w = await World.CreateAsync();
        var begin = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/auth/hub/begin"));
        var cookie = begin.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("startapp_hubflow=", StringComparison.Ordinal)).Split(';')[0];
        var url = w.Hub.Authorize((await TestApp.Json(begin)).GetProperty("url").GetString()!, "sub-anna", "member");
        await w.Db.ExecAsync("update hub_flows set expires_at = now() - interval '1 minute'");
        var r = await w.CallbackAsync(url, cookie);
        Assert.Equal("/?hub_error=hub.state", r.Location);
        Assert.Null(r.Session);
    }

    [Fact]
    public async Task A_token_that_is_not_exactly_right_signs_nobody_in()
    {
        await using var w = await World.CreateAsync();
        var cases = new (string Why, Action<Dictionary<string, object>>? Tamper, ECDsa? Key, string? Alg)[]
        {
            ("another nonce", c => c["nonce"] = "not-the-nonce-of-this-flow", null, null),
            ("no nonce", c => c.Remove("nonce"), null, null),
            ("another issuer", c => c["iss"] = "http://127.0.0.1:1", null, null),
            ("another audience", c => c["aud"] = "another-app", null, null),
            ("no audience", c => c.Remove("aud"), null, null),
            ("expired", c => { c["iat"] = Now() - 900; c["exp"] = Now() - 600; }, null, null),
            ("no expiry", c => c.Remove("exp"), null, null),
            ("not yet valid", c => c["nbf"] = Now() + 3600, null, null),
            ("no subject", c => c.Remove("sub"), null, null),
            ("no sid", c => c.Remove("sid"), null, null),
            ("signed with another key", null, w.Hub.OtherKey, null),
            ("signed with a shared secret instead of the key", null, null, "HS256"),
            ("not signed at all", null, null, "none"),
        };
        foreach (var (why, tamper, key, alg) in cases)
        {
            var (location, session) = await w.SignInAsync("sub-anna", "member", sid: "sid-" + why.Length, tamper: tamper, signWith: key, alg: alg);
            Assert.True(location == "/?hub_error=hub.failed" && session is null, $"{why}: {location}");
        }
        Assert.Equal(0, await w.CountAsync("select count(*) from users where hub_sub is not null"));
        Assert.Equal(cases.Length, w.Hub.TokenRequests.Count);                  // each one really got as far as the token check
    }

    [Fact]
    public async Task A_callback_naming_another_issuer_or_an_error_signs_nobody_in()
    {
        await using var w = await World.CreateAsync();
        var (location, session) = await w.SignInAsync(changeCallback: u => System.Text.RegularExpressions.Regex.Replace(u, "iss=[^&]+", "iss=http%3A%2F%2F127.0.0.1%3A1"));
        Assert.Equal("/?hub_error=hub.failed", location);
        Assert.Null(session);
        // the hub says no (access_denied): shown as such, nothing else is tried
        (location, session) = await w.SignInAsync(changeCallback: u => System.Text.RegularExpressions.Regex.Replace(u, "code=[^&]+", "error=access_denied"));
        Assert.Equal("/?hub_error=hub.denied", location);
        Assert.Null(session);
        Assert.Empty(w.Hub.TokenRequests);
    }

    // ---- the centre is away ----

    [Fact]
    public async Task When_the_hub_is_away_the_own_form_still_works_and_health_says_degraded()
    {
        await using var w = await World.CreateAsync();
        var begin = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/auth/hub/begin"));
        var cookie = begin.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("startapp_hubflow=", StringComparison.Ordinal)).Split(';')[0];
        var callback = w.Hub.Authorize((await TestApp.Json(begin)).GetProperty("url").GetString()!, "sub-anna", "member");
        await w.Hub.StopAsync();

        // the button asks the hub itself, so a hub that went away is said in words and the person is not sent to a dead address
        var again = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/auth/hub/begin"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var reason = await TestApp.Json(again);
        Assert.Equal("hub.unreachable", reason.GetProperty("error").GetString());
        Assert.DoesNotContain("Exception", reason.GetProperty("message").GetString());

        var (location, session) = await w.CallbackAsync(callback, cookie);
        Assert.Equal("/?hub_error=hub.unreachable", location);
        Assert.Null(session);
        Assert.Equal("degraded", (await w.GetAsync("/api/health")).GetProperty("status").GetString());
        // the check button says it in words
        var check = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/hub-settings/check", w.Admin));
        Assert.Equal("hub.unreachable", (await TestApp.Json(check)).GetProperty("error").GetString());
        // and the own form, and every session that exists, go on as if nothing happened
        Assert.Equal("jul", await w.UserOf(w.Admin));
        var secret = (await w.App.Services.GetRequiredService<UserStore>().FindByNameAsync("jul"))!.TotpSecret;
        var totp = Totp.Hotp(Base32.Decode(secret), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + 1);       // the next step: the setup used this one
        var login = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/auth/login", null, new { user = "jul", password = TestAccounts.Password, code = totp }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    // ---- the settings ----

    [Fact]
    public async Task The_secret_is_stored_encrypted_and_never_comes_back()
    {
        await using var w = await World.CreateAsync();
        var settings = await w.GetAsync("/api/hub-settings", w.Admin);
        Assert.True(settings.GetProperty("secretSet").GetBoolean());
        Assert.Equal(w.Hub.Url, settings.GetProperty("url").GetString());
        Assert.DoesNotContain(TestHub.Secret, settings.ToString());
        Assert.False(settings.TryGetProperty("secret", out _));
        // not in the database in the clear, not in the generic settings, and the generic API cannot write it
        Assert.DoesNotContain(TestHub.Secret, await w.Db.ScalarAsync<string>("select string_agg(value::text, ' ') from instance_settings"));
        Assert.NotEqual(0, await w.CountAsync("select count(*) from instance_settings where key = 'hub.secret'"));
        var generic = await w.GetAsync("/api/instance-settings", w.Admin);
        Assert.DoesNotContain("hub.", generic.ToString());
        var put = await w.Http.SendAsync(w.App.Req(HttpMethod.Put, "/api/instance-settings/hub.secret", w.Admin, new { value = "x" }));
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        // saving again without a secret keeps the stored one; a new one replaces it
        var keep = await w.Http.SendAsync(w.App.Req(HttpMethod.Put, "/api/hub-settings", w.Admin, new { url = w.Hub.Url, clientId = TestHub.ClientId, enabled = true }));
        Assert.Equal(HttpStatusCode.NoContent, keep.StatusCode);
        Assert.Equal("Test Hub", (await w.GetAsync("/api/auth/me")).GetProperty("hub").GetProperty("name").GetString());
        Assert.NotNull((await w.SignInAsync()).Session);                 // still works: the secret was kept
    }

    [Fact]
    public async Task The_hub_settings_refuse_unsafe_addresses_and_an_incomplete_switch_on()
    {
        await using var w = await World.CreateAsync(configure: false);
        async Task<(HttpStatusCode, string?)> Put(object body)
        {
            var r = await w.Http.SendAsync(w.App.Req(HttpMethod.Put, "/api/hub-settings", w.Admin, body));
            return (r.StatusCode, r.StatusCode == HttpStatusCode.BadRequest ? (await TestApp.Json(r)).GetProperty("error").GetString() : null);
        }
        // the secret travels to this address, so: https, or this machine; no path, no password in the address
        foreach (var bad in new[] { "http://hub.example.test", "ftp://hub.example.test", "https://hub.example.test/path", "https://hub.example.test@attacker.example.test", "https://hub.example.test/?x=1", "javascript:alert(1)", "hub.example.test" })
            Assert.Equal((HttpStatusCode.BadRequest, "hub.url"), await Put(new { url = bad, clientId = "app", secret = "long-enough-secret", enabled = false }));
        Assert.Equal((HttpStatusCode.BadRequest, "hub.client_id"), await Put(new { url = "https://hub.example.test", clientId = "bad id!", secret = "long-enough-secret" }));
        Assert.Equal((HttpStatusCode.BadRequest, "hub.secret"), await Put(new { url = "https://hub.example.test", clientId = "app", secret = "short" }));
        // on without everything: refused
        Assert.Equal((HttpStatusCode.BadRequest, "hub.incomplete"), await Put(new { url = "https://hub.example.test", clientId = "app", enabled = true }));
        Assert.Equal((HttpStatusCode.NoContent, null), await Put(new { url = "https://hub.example.test", clientId = "app", secret = "long-enough-secret", enabled = true }));
        Assert.Equal((HttpStatusCode.NoContent, null), await Put(new { url = w.Hub.Url, clientId = "app", enabled = false }));         // this machine over http is fine
        // off: no button, and no way to begin
        Assert.False((await w.GetAsync("/api/auth/me")).TryGetProperty("hub", out _));
        var begin = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/auth/hub/begin"));
        Assert.Equal("hub.off", (await TestApp.Json(begin)).GetProperty("error").GetString());
        var (location, session) = await w.CallbackAsync("http://localhost/api/auth/hub/callback?code=a&state=b", null);
        Assert.Equal("/?hub_error=hub.off", location);
        Assert.Null(session);
    }

    [Fact]
    public async Task Check_reads_the_description_of_the_hub_and_the_button_carries_its_name()
    {
        await using var w = await World.CreateAsync(enabled: false);
        var r = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/hub-settings/check", w.Admin));
        var body = await TestApp.Json(r);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Test Hub", body.GetProperty("name").GetString());
        Assert.Equal(w.Hub.Url, body.GetProperty("issuer").GetString());
        Assert.True(body.GetProperty("endSession").GetBoolean());
        await w.ConfigureAsync(enabled: true);
        Assert.Equal("Test Hub", (await w.GetAsync("/api/auth/me")).GetProperty("hub").GetProperty("name").GetString());
        // a hub that gives no name: the host stands on the button
        var w2 = await World.CreateAsync();
        await using (w2)
        {
            w2.Hub.AboutName = null;
            await w2.Http.SendAsync(w2.App.Req(HttpMethod.Post, "/api/hub-settings/check", w2.Admin));
            Assert.Equal("127.0.0.1", (await w2.GetAsync("/api/auth/me")).GetProperty("hub").GetProperty("name").GetString());
        }
        // a hub whose description names another issuer is not accepted
        w.Hub.ReportedIssuer = "http://127.0.0.1:1";
        var wrong = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/hub-settings/check", w.Admin));
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        Assert.Equal("hub.metadata", (await TestApp.Json(wrong)).GetProperty("error").GetString());
        // and nobody can begin a sign-in with it either
        var begin = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/auth/hub/begin"));
        Assert.Equal(HttpStatusCode.Conflict, begin.StatusCode);
    }

    // ---- sign out ----

    [Fact]
    public async Task Signing_out_in_the_app_goes_on_to_the_sign_out_of_the_hub_for_its_people_only()
    {
        await using var w = await World.CreateAsync();
        var (_, session) = await w.SignInAsync();
        var r = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/auth/logout", session));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        // with the ID token as the hint and the app's own address to come back to: the hub then signs out without asking and returns the person here
        var url = new Uri((await TestApp.Json(r)).GetProperty("hubLogoutUrl").GetString()!);
        var q = System.Web.HttpUtility.ParseQueryString(url.Query);
        Assert.Equal(w.Hub.Url + "/oidc/end-session", url.GetLeftPart(UriPartial.Path));
        Assert.Equal(TestHub.ClientId, q["client_id"]);
        Assert.Equal(w.Hub.LastIdToken, q["id_token_hint"]);
        Assert.Equal(w.Http.BaseAddress!.GetLeftPart(UriPartial.Authority) + "/", q["post_logout_redirect_uri"]);
        Assert.Null(await w.UserOf(session!));
        // a person of the local form signs out as before
        var local = await w.Http.SendAsync(w.App.Req(HttpMethod.Post, "/api/auth/logout", w.Admin));
        Assert.Equal(HttpStatusCode.NoContent, local.StatusCode);
    }

    [Fact]
    public async Task The_logout_of_the_hub_ends_the_matching_sessions_and_a_token_works_once()
    {
        await using var w = await World.CreateAsync();
        var (_, a1) = await w.SignInAsync("sub-anna", "member", sid: "sid-A");
        var (_, a2) = await w.SignInAsync("sub-anna", "member", sid: "sid-B");
        var (_, b1) = await w.SignInAsync("sub-ben", "member", name: "ben", sid: "sid-A2");

        // no cookie, no header of the app's own pages: the signed token is the proof
        var token = w.Hub.LogoutToken("sid-A", "sub-anna", jti: "jti-1");
        Assert.Equal(HttpStatusCode.OK, (await w.BackChannelAsync(token)).StatusCode);
        Assert.Null(await w.UserOf(a1!));                       // that central session is over here
        Assert.Equal("anna", await w.UserOf(a2!));              // another session of the same person goes on
        Assert.Equal("ben", await w.UserOf(b1!));

        // the same token again: refused, and it ends nothing more (here: a session that later gets the same sid)
        var (_, a3) = await w.SignInAsync("sub-anna", "member", sid: "sid-A");
        var replay = await w.BackChannelAsync(token);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("hub.token", (await TestApp.Json(replay)).GetProperty("error").GetString());
        Assert.Equal("anna", await w.UserOf(a3!));

        // by person only (no sid): every session of that person, not of others
        Assert.Equal(HttpStatusCode.OK, (await w.BackChannelAsync(w.Hub.LogoutToken(null, "sub-anna"))).StatusCode);
        Assert.Null(await w.UserOf(a2!)); Assert.Null(await w.UserOf(a3!));
        Assert.Equal("ben", await w.UserOf(b1!));
        // the person can sign in again afterwards: a logout is not a block
        Assert.NotNull((await w.SignInAsync("sub-anna", "member", sid: "sid-C")).Session);
        // a sid that belongs to another person ends nothing
        Assert.Equal(HttpStatusCode.OK, (await w.BackChannelAsync(w.Hub.LogoutToken("sid-A2", "sub-anna"))).StatusCode);
        Assert.Equal("ben", await w.UserOf(b1!));
        // local sessions are never touched by the hub
        Assert.Equal("jul", await w.UserOf(w.Admin));
    }

    [Fact]
    public async Task A_logout_token_that_is_not_exactly_right_ends_nothing()
    {
        await using var w = await World.CreateAsync();
        var (_, session) = await w.SignInAsync("sub-anna", "member", sid: "sid-A");
        var cases = new (string Why, string? Token)[]
        {
            ("no token", null),
            ("not a token", "garbage"),
            ("another key", w.Hub.LogoutToken("sid-A", "sub-anna", signWith: w.Hub.OtherKey)),
            ("another issuer", w.Hub.LogoutToken("sid-A", "sub-anna", tamper: c => c["iss"] = "http://127.0.0.1:1")),
            ("another audience", w.Hub.LogoutToken("sid-A", "sub-anna", tamper: c => c["aud"] = "another-app")),
            ("expired", w.Hub.LogoutToken("sid-A", "sub-anna", tamper: c => { c["iat"] = Now() - 900; c["exp"] = Now() - 600; })),
            ("issued long ago", w.Hub.LogoutToken("sid-A", "sub-anna", tamper: c => { c["iat"] = Now() - 3600; c["exp"] = Now() + 60; })),
            ("no event", w.Hub.LogoutToken("sid-A", "sub-anna", tamper: c => c.Remove("events"))),
            ("another event", w.Hub.LogoutToken("sid-A", "sub-anna", tamper: c => c["events"] = new Dictionary<string, object> { ["http://schemas.openid.net/event/other"] = new Dictionary<string, object>() })),
            ("with a nonce (an ID token is no logout token)", w.Hub.LogoutToken("sid-A", "sub-anna", tamper: c => c["nonce"] = "n")),
            ("no jti", w.Hub.LogoutToken("sid-A", "sub-anna", tamper: c => c.Remove("jti"))),
            ("neither sid nor sub", w.Hub.LogoutToken(null, null)),
        };
        foreach (var (why, token) in cases)
        {
            var r = await w.BackChannelAsync(token);
            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{why}: {r.StatusCode}");
            Assert.Equal("anna", await w.UserOf(session!));
        }
        Assert.Equal(0, await w.CountAsync("select count(*) from hub_logout_tokens where jti_hash is null"));
        // a GET or a body without the form type is not accepted either
        Assert.NotEqual(HttpStatusCode.OK, (await w.Http.GetAsync("/api/auth/hub/backchannel-logout")).StatusCode);
        var json = await w.Http.PostAsync("/api/auth/hub/backchannel-logout", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, json.StatusCode);
        Assert.Equal("anna", await w.UserOf(session!));
        // and the ordinary rule for requests that change something stays in force everywhere else (SEC-04)
        var plain = await w.Http.PostAsync("/api/auth/logout", new StringContent(""));
        Assert.Equal(HttpStatusCode.Forbidden, plain.StatusCode);
    }

    [Fact]
    public async Task An_embeddable_app_lets_only_the_connected_hub_frame_it()
    {
        // SEC-01, SEC-02, contract about.embed: the hub's origin is the one frame ancestor while the app allows it and the hub is on; otherwise nobody
        await using var w = await World.CreateAsync(embeddable: true);
        var page = await w.Http.GetAsync("/");
        Assert.Contains("frame-ancestors " + w.Hub.Url + ";", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(page.Headers.Contains("X-Frame-Options"));
        Assert.True((await w.GetAsync("/api/about")).GetProperty("embed").GetProperty("hub").GetBoolean());

        await w.ConfigureAsync(enabled: false);                                 // switched off: back to no framing at all
        page = await w.Http.GetAsync("/");
        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("DENY", page.Headers.GetValues("X-Frame-Options").Single());
        Assert.False((await w.GetAsync("/api/about")).GetProperty("embed").GetProperty("hub").GetBoolean());
    }

    [Fact]
    public async Task An_app_that_does_not_allow_it_is_never_framed_even_with_a_hub()
    {
        await using var w = await World.CreateAsync();
        var page = await w.Http.GetAsync("/");
        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("DENY", page.Headers.GetValues("X-Frame-Options").Single());
        Assert.False((await w.GetAsync("/api/about")).GetProperty("embed").GetProperty("hub").GetBoolean());
    }
}
