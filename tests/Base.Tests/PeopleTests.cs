using System.Net;
using Base.Accounts;
using Base.Core;
using Base.Web.Auth;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Base.Tests;

/// <summary>Roles and the management of people: invitation links, one role per person, the last admin, blocking, deleting (AUTH-11, AUTH-12, SEC-03).</summary>
public class PeopleTests
{
    private const string Pw = "purple orchid sunrise";

    private static async Task<(string Token, TestAccounts A, SessionIdentity Admin)> AdminAsync(TestDb db)
    {
        var a = new TestAccounts(db);
        var (_, token, _) = await a.SetUpAsync("jul");
        return (token, a, await a.WhoAsync(token));
    }

    /// <summary>Creates a person and walks through the invitation as that person; returns the session token.</summary>
    private static async Task<(PersonCreated Made, string Token)> JoinAsync(TestAccounts a, SessionIdentity admin, string name, string role)
    {
        var made = await a.People.CreateAsync(admin, name, role, TestAccounts.Ip);
        Assert.True(made.Ok, made.Error?.Code);
        var begun = await a.People.BeginInviteAsync(made.Value.Invite.Token, Pw, TestAccounts.Ip);
        Assert.True(begun.Ok, begun.Error?.Code);
        var done = await a.People.FinishInviteAsync(made.Value.Invite.Token, a.Code(begun.Value.Secret), TestAccounts.Ip, "agent");
        Assert.True(done.Ok, done.Error?.Code);
        a.NextStep();
        return (made.Value, done.Value.Token);
    }

    [Fact]
    public async Task The_first_person_of_the_setup_is_an_admin_and_roles_are_the_list_of_the_app()
    {
        await using var db = await TestDb.CreateAsync();
        var (_, a, admin) = await AdminAsync(db);
        Assert.Equal(["admin"], admin.Roles);
        Assert.Equal(["admin", "member", "viewer"], a.Roles.All.Select(r => r.Key));
        Assert.False((await a.People.CreateAsync(admin, "anna", "root", TestAccounts.Ip)).Ok);
        Assert.Throws<InvalidOperationException>(() => new AppRoles([new("member", new Localized("Member"))]));                       // no admin
        Assert.Throws<InvalidOperationException>(() => new AppRoles([new("admin", new Localized("A")), new("admin", new Localized("B"))]));   // twice
        Assert.Throws<InvalidOperationException>(() => new AppRoles([new("admin", new Localized("A")), new("Bad Key", new Localized("B"))]));
        Assert.Throws<InvalidOperationException>(() => new AppRoles([.. Enumerable.Range(0, 9).Select(i => new AppRole(i == 0 ? "admin" : "r" + i, new Localized("x")))]));
    }

    [Fact]
    public async Task An_invitation_link_works_once_and_makes_a_person_with_the_chosen_role()
    {
        await using var db = await TestDb.CreateAsync();
        var (_, a, admin) = await AdminAsync(db);
        var made = (await a.People.CreateAsync(admin, "Anna", "member", TestAccounts.Ip)).Value;
        Assert.Equal("anna", made.Person.Name);
        var token = made.Invite.Token;

        Assert.Equal("anna", (await a.People.CheckInviteAsync(token, TestAccounts.Ip)).Value.User);
        // before the setup is finished the person cannot sign in, whatever they know
        var begun = (await a.People.BeginInviteAsync(token, TestAccounts.Password, TestAccounts.Ip)).Value;
        Assert.False((await a.Service.LoginAsync("anna", TestAccounts.Password, a.Code(begun.Secret), TestAccounts.Ip, "agent")).Ok);
        a.NextStep();
        Assert.False((await a.People.FinishInviteAsync(token, "000000", TestAccounts.Ip, "agent")).Ok);                   // a wrong code finishes nothing
        var done = await a.People.FinishInviteAsync(token, a.Code(begun.Secret), TestAccounts.Ip, "agent");
        Assert.True(done.Ok, done.Error?.Code);
        Assert.Equal(["member"], done.Value.Roles);
        Assert.Equal(["member"], (await a.WhoAsync(done.Value.Token)).Roles);

        // used: the link is dead for every step
        Assert.Equal("invite.invalid", (await a.People.CheckInviteAsync(token, TestAccounts.Ip)).Error!.Code);
        Assert.False((await a.People.BeginInviteAsync(token, TestAccounts.Password, TestAccounts.Ip)).Ok);
        Assert.False((await a.People.FinishInviteAsync(token, a.Code(begun.Secret), TestAccounts.Ip, "agent")).Ok);
        // and the person now signs in with password and code like everybody
        a.NextStep();
        Assert.True((await a.Service.LoginAsync("anna", TestAccounts.Password, a.Code(begun.Secret), TestAccounts.Ip, "agent")).Ok);
    }

    [Fact]
    public async Task An_invitation_runs_out_after_24_hours_and_a_new_link_replaces_the_old_one()
    {
        await using var db = await TestDb.CreateAsync();
        var (_, a, admin) = await AdminAsync(db);
        var first = (await a.People.CreateAsync(admin, "anna", "member", TestAccounts.Ip)).Value;
        Assert.Equal(TimeSpan.FromHours(24), first.Invite.ExpiresAt - a.Clock.GetUtcNow());
        a.Clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59));
        Assert.True((await a.People.CheckInviteAsync(first.Invite.Token, TestAccounts.Ip)).Ok);
        a.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False((await a.People.CheckInviteAsync(first.Invite.Token, TestAccounts.Ip)).Ok);
        Assert.False((await a.People.BeginInviteAsync(first.Invite.Token, TestAccounts.Password, TestAccounts.Ip)).Ok);

        var second = (await a.People.ReinviteAsync(admin, first.Person.Id, TestAccounts.Ip)).Value;
        var third = (await a.People.ReinviteAsync(admin, first.Person.Id, TestAccounts.Ip)).Value;
        Assert.False((await a.People.CheckInviteAsync(second.Token, TestAccounts.Ip)).Ok);       // the older link stopped working
        Assert.True((await a.People.CheckInviteAsync(third.Token, TestAccounts.Ip)).Ok);
    }

    [Fact]
    public async Task The_link_is_stored_only_as_a_hash_and_guessing_links_locks()
    {
        await using var db = await TestDb.CreateAsync();
        var (_, a, admin) = await AdminAsync(db);
        var made = (await a.People.CreateAsync(admin, "anna", "member", TestAccounts.Ip)).Value;
        var stored = await db.ScalarAsync<string>("select token_hash from invites");
        Assert.NotEqual(made.Invite.Token, stored);
        Assert.Equal(SessionTokens.Hash(made.Invite.Token), stored);
        Assert.Equal(43, made.Invite.Token.Length);                       // 256 random bits

        Result<InviteInfo> last = default;
        for (var i = 0; i < 6; i++) last = await a.People.CheckInviteAsync("guess" + i, TestAccounts.Ip);
        Assert.Equal(ErrorKind.TooMany, last.Error!.Kind);
        // even the right link waits while the address is locked
        Assert.Equal(ErrorKind.TooMany, (await a.People.CheckInviteAsync(made.Invite.Token, TestAccounts.Ip)).Error!.Kind);
        Assert.True((await a.People.CheckInviteAsync(made.Invite.Token, "192.0.2.99")).Ok);
    }

    [Fact]
    public async Task The_last_admin_cannot_lose_the_role_be_blocked_or_deleted_and_nobody_acts_on_themselves()
    {
        await using var db = await TestDb.CreateAsync();
        var (_, a, jul) = await AdminAsync(db);
        Assert.Equal("people.last_admin", (await a.People.SetRoleAsync(jul, jul.UserId, "member", TestAccounts.Ip)).Error!.Code);
        Assert.Equal("people.self", (await a.People.SetBlockedAsync(jul, jul.UserId, true, TestAccounts.Ip)).Error!.Code);
        Assert.Equal("people.self", (await a.People.DeleteAsync(jul, jul.UserId, TestAccounts.Ip)).Error!.Code);

        var (made, token) = await JoinAsync(a, jul, "anna", "admin");
        var anna = await a.WhoAsync(token);
        Assert.True((await a.People.SetRoleAsync(anna, jul.UserId, "member", TestAccounts.Ip)).Ok);       // two admins: one may step down
        // anna is the last one now, whoever asks
        Assert.Equal("people.last_admin", (await a.People.SetRoleAsync(jul, made.Person.Id, "viewer", TestAccounts.Ip)).Error!.Code);
        Assert.Equal("people.last_admin", (await a.People.SetBlockedAsync(jul, made.Person.Id, true, TestAccounts.Ip)).Error!.Code);
        Assert.Equal("people.last_admin", (await a.People.DeleteAsync(jul, made.Person.Id, TestAccounts.Ip)).Error!.Code);
        Assert.Equal(1, await db.ScalarAsync<long>("select count(*) from users where 'admin' = any(roles)"));
    }

    [Fact]
    public async Task Two_admins_stepping_down_at_the_same_moment_leave_one()
    {
        await using var db = await TestDb.CreateAsync();
        var (_, a, jul) = await AdminAsync(db);
        var (made, token) = await JoinAsync(a, jul, "anna", "admin");
        var anna = await a.WhoAsync(token);
        var results = await Task.WhenAll(a.People.SetRoleAsync(jul, anna.UserId, "member", TestAccounts.Ip), a.People.SetRoleAsync(anna, jul.UserId, "member", TestAccounts.Ip));
        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.Equal(1, await db.ScalarAsync<long>("select count(*) from users where 'admin' = any(roles)"));
    }

    [Fact]
    public async Task Blocking_ends_every_session_and_stops_the_sign_in_until_the_person_is_unblocked()
    {
        await using var db = await TestDb.CreateAsync();
        var (_, a, jul) = await AdminAsync(db);
        var (made, token) = await JoinAsync(a, jul, "anna", "member");
        Assert.NotNull(await a.Sessions.ResolveAsync(token, false));
        Assert.True((await a.People.SetBlockedAsync(jul, made.Person.Id, true, TestAccounts.Ip)).Ok);
        Assert.Null(await a.Sessions.ResolveAsync(token, false));
        Assert.False((await a.Service.LoginAsync("anna", Pw, "123456", TestAccounts.Ip, "agent")).Ok);
        Assert.Equal("blocked", await db.ScalarAsync<string>("select state from users where name = 'anna'"));
        Assert.True((await a.People.SetBlockedAsync(jul, made.Person.Id, false, TestAccounts.Ip)).Ok);
        Assert.Null(await a.Sessions.ResolveAsync(token, false));         // the old session does not come back
    }

    [Fact]
    public async Task A_role_change_applies_to_open_sessions_at_once_and_deleting_removes_the_person()
    {
        await using var db = await TestDb.CreateAsync();
        var (_, a, jul) = await AdminAsync(db);
        var (made, token) = await JoinAsync(a, jul, "anna", "member");
        Assert.True((await a.People.SetRoleAsync(jul, made.Person.Id, "viewer", TestAccounts.Ip)).Ok);
        Assert.Equal(["viewer"], (await a.Sessions.ResolveAsync(token, false))!.Roles);
        Assert.Equal("people.role", (await a.People.SetRoleAsync(jul, made.Person.Id, "root", TestAccounts.Ip)).Error!.Field());
        Assert.True((await a.People.DeleteAsync(jul, made.Person.Id, TestAccounts.Ip)).Ok);
        Assert.Null(await a.Sessions.ResolveAsync(token, false));
        Assert.Equal(0, await db.ScalarAsync<long>("select count(*) from users where name = 'anna'"));
        Assert.Equal(0, await db.ScalarAsync<long>("select count(*) from sessions where user_id = $1", made.Person.Id));
    }

    [Fact]
    public async Task Changes_to_people_are_written_to_their_history_without_secrets()
    {
        await using var db = await TestDb.CreateAsync();
        var (_, a, jul) = await AdminAsync(db);
        var (made, _) = await JoinAsync(a, jul, "anna", "member");
        await a.People.SetRoleAsync(jul, made.Person.Id, "viewer", TestAccounts.Ip);
        await a.People.SetBlockedAsync(jul, made.Person.Id, true, TestAccounts.Ip);
        var events = (await a.People.HistoryAsync(made.Person.Id))!;
        Assert.Contains(events, e => e.Event == "person.created" && e.Detail!.Contains("by jul"));
        Assert.Contains(events, e => e.Event == "invite.used");
        Assert.Contains(events, e => e.Event == "person.role" && e.Detail!.Contains("member to viewer"));
        Assert.Contains(events, e => e.Event == "person.blocked");
        Assert.DoesNotContain(events, e => (e.Detail ?? "").Contains(made.Invite.Token) || (e.Detail ?? "").Contains(TestAccounts.Password));
    }

    // ---- over HTTP: the role is checked in one place ----

    [Fact]
    public async Task The_people_endpoints_need_the_role_admin_and_the_invitation_works_over_http()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        var c = app.Http();
        var (admin, _) = await app.SignedInAsync(c);

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(app.Req(HttpMethod.Get, "/api/people"))).StatusCode);
        var created = await c.SendAsync(app.Req(HttpMethod.Post, "/api/people", admin, new { name = "anna", role = "viewer" }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var token = (await TestApp.Json(created)).GetProperty("invite").GetProperty("token").GetString()!;
        var id = (await TestApp.Json(created)).GetProperty("person").GetProperty("id").GetString()!;

        // the public steps behind the link (no session)
        Assert.Equal("anna", (await TestApp.Json(await c.SendAsync(app.Req(HttpMethod.Post, "/api/invite/check", null, new { token })))).GetProperty("user").GetString());
        var begin = await c.SendAsync(app.Req(HttpMethod.Post, "/api/invite/begin", null, new { token, password = TestAccounts.Password }));
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        var secret = (await TestApp.Json(begin)).GetProperty("secret").GetString()!;
        var finish = await c.SendAsync(app.Req(HttpMethod.Post, "/api/invite/finish", null, new { token, totp = Totp.Hotp(Base32.Decode(secret), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30) }));
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        var viewer = TestApp.CookieOf(finish);
        var me = await TestApp.Json(await c.SendAsync(app.Req(HttpMethod.Get, "/api/auth/me", viewer)));
        Assert.Equal(["viewer"], me.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(["admin", "member", "viewer"], me.GetProperty("appRoles").EnumerateArray().Select(x => x.GetProperty("key").GetString()).ToArray());

        // a viewer reaches none of the admin endpoints; the guard answers before any handler runs
        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, "/api/people", null), (HttpMethod.Post, "/api/people", new { name = "x", role = "member" }), (HttpMethod.Post, $"/api/people/{id}/invite", null),
            (HttpMethod.Put, $"/api/people/{id}/role", new { role = "admin" }), (HttpMethod.Post, $"/api/people/{id}/block", null), (HttpMethod.Delete, $"/api/people/{id}", null),
            (HttpMethod.Get, "/api/instance-settings", null), (HttpMethod.Get, "/api/hub-settings", null), (HttpMethod.Put, "/api/hub-settings", new { url = "https://hub.example.test" }),
            (HttpMethod.Post, "/api/hub-settings/check", null),
        })
        {
            var r = await c.SendAsync(app.Req(method, url, viewer, body));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("role.missing", (await TestApp.Json(r)).GetProperty("error").GetString());
        }
        Assert.Equal("viewer", await db.ScalarAsync<string>("select roles[1] from users where name = 'anna'"));     // and nothing happened
    }

    [Fact]
    public async Task Every_endpoint_that_manages_people_or_the_hub_declares_the_role_it_needs()
    {
        await using var db = await TestDb.CreateAsync();
        await using var app = new TestApp(db);
        _ = app.Http();
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();
        string[] guarded = ["/api/people", "/api/hub-settings", "/api/instance-settings"];
        var admin = endpoints.Where(e => guarded.Any(p => e.RoutePattern.RawText!.StartsWith(p, StringComparison.Ordinal))).ToArray();
        Assert.True(admin.Length >= 15, "the endpoints of the admin were not found");
        Assert.All(admin, e => Assert.Equal("admin", e.Metadata.GetMetadata<RoleRequirement>()?.Role));
        // and the public doors are exactly the ones the guard lists: no other path under /api answers without a session
        var open = endpoints.Select(e => e.RoutePattern.RawText!).Where(p => p.StartsWith("/api/", StringComparison.Ordinal) && !p.StartsWith("/api/items", StringComparison.Ordinal))
            .Where(p => ApiGuard.BasePublicPaths.Contains(p)).Order().ToArray();
        Assert.Equal([.. ApiGuard.BasePublicPaths.Order()], open);
    }
}

internal static class ErrorExtensions
{
    /// <summary>The code of the first wrong field, or the code of the error.</summary>
    public static string Field(this Error e) => e.Fields?.Values.FirstOrDefault() ?? e.Code;
}
