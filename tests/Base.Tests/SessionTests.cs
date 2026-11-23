using Base.Accounts;

namespace Base.Tests;

/// <summary>Sessions in PostgreSQL: AUTH-03 and AUTH-04.</summary>
public class SessionTests
{
    [Fact]
    public async Task Only_the_hash_of_the_token_is_stored()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (_, token, _) = await a.SetUpAsync();

        Assert.Equal(43, token.Length);                                               // 256 bit
        var stored = await db.ScalarAsync<string>("select hash from sessions");
        Assert.Equal(SessionTokens.Hash(token), stored);
        Assert.NotEqual(token, stored);
        Assert.Equal(0L, await db.ScalarAsync<long>("select count(*) from sessions where hash = $1 or prev_hash = $1", token));
        Assert.Null(await a.Sessions.ResolveAsync(stored, rotate: true));           // the stored value itself opens nothing
    }

    [Fact]
    public async Task A_session_resolves_to_its_person_and_their_roles()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (_, token, _) = await a.SetUpAsync();
        var who = await a.Sessions.ResolveAsync(token, rotate: true);
        Assert.NotNull(who);
        Assert.Equal("jul", who.UserName);
        Assert.True(who.Has(Roles.Admin));                                            // the first account administers the installation
        Assert.Null(who.NewToken);
        Assert.Null(await a.Sessions.ResolveAsync("unknown-token", rotate: true));
        Assert.Null(await a.Sessions.ResolveAsync(new string('x', 500), rotate: true));
    }

    [Fact]
    public async Task A_session_ends_after_30_idle_days_and_after_180_days_at_most()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (_, token, _) = await a.SetUpAsync();

        await db.ExecAsync("update sessions set last_seen = now() - interval '29 days'");
        Assert.NotNull(await a.Sessions.ResolveAsync(token, rotate: false));
        await db.ExecAsync("update sessions set last_seen = now() - interval '31 days'");
        Assert.Null(await a.Sessions.ResolveAsync(token, rotate: false));

        await db.ExecAsync("update sessions set last_seen = now(), created_at = now() - interval '179 days'");
        Assert.NotNull(await a.Sessions.ResolveAsync(token, rotate: false));
        await db.ExecAsync("update sessions set last_seen = now(), created_at = now() - interval '181 days'");
        Assert.Null(await a.Sessions.ResolveAsync(token, rotate: false));

        Assert.Equal(1, await a.Sessions.PurgeAsync());
        Assert.Equal(0L, await db.ScalarAsync<long>("select count(*) from sessions"));
    }

    [Fact]
    public async Task The_token_is_rotated_and_the_old_one_works_for_a_short_grace_only()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (_, token, _) = await a.SetUpAsync();
        var id = (await a.Sessions.ResolveAsync(token, rotate: true))!.Id;

        await db.ExecAsync("update sessions set rotated_at = now() - interval '13 hours'");
        Assert.Null((await a.Sessions.ResolveAsync(token, rotate: false))!.NewToken);                 // where no cookie can be sent back, nothing rotates
        var rotated = await a.Sessions.ResolveAsync(token, rotate: true);
        Assert.NotNull(rotated?.NewToken);
        Assert.NotEqual(token, rotated.NewToken);
        Assert.Equal(id, rotated.Id);                                                                 // the device stays the same

        var viaOld = await a.Sessions.ResolveAsync(token, rotate: true);                             // a request that was already on its way
        Assert.NotNull(viaOld);
        Assert.Null(viaOld.NewToken);
        Assert.Equal(id, (await a.Sessions.ResolveAsync(rotated.NewToken, rotate: true))!.Id);

        await db.ExecAsync("update sessions set prev_until = now() - interval '1 minute'");
        Assert.Null(await a.Sessions.ResolveAsync(token, rotate: true));                              // the grace is over
        Assert.NotNull(await a.Sessions.ResolveAsync(rotated.NewToken, rotate: true));
    }

    [Fact]
    public async Task Two_requests_at_the_same_moment_rotate_only_once()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (_, token, _) = await a.SetUpAsync();
        await db.ExecAsync("update sessions set rotated_at = now() - interval '13 hours'");
        var both = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => a.Sessions.ResolveAsync(token, rotate: true)));
        Assert.All(both, Assert.NotNull);
        Assert.Single(both, b => b!.NewToken is not null);
    }

    [Fact]
    public async Task Devices_are_listed_and_each_can_be_ended_by_its_person_only()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (secret, token, _) = await a.SetUpAsync();
        a.NextStep();
        var second = await a.Service.LoginAsync("jul", TestAccounts.Password, a.Code(secret), "192.0.2.8", "phone");
        Assert.True(second.Ok);

        var me = (await a.Sessions.ResolveAsync(token, rotate: false))!;
        var list = await a.Service.ListSessionsAsync(me);
        Assert.Equal(2, list.Count);
        Assert.Single(list, s => s.Current);
        var other = list.Single(s => !s.Current);
        Assert.Equal("phone", other.UserAgent);
        Assert.Equal("192.0.2.8", other.Ip);

        var stranger = me with { UserId = Guid.NewGuid() };
        Assert.False(await a.Service.EndSessionAsync(stranger, other.Id, TestAccounts.Ip));
        Assert.True(await a.Service.EndSessionAsync(me, other.Id, TestAccounts.Ip));
        Assert.Null(await a.Sessions.ResolveAsync(second.Value.Token, rotate: false));
        Assert.NotNull(await a.Sessions.ResolveAsync(token, rotate: false));
    }
}
