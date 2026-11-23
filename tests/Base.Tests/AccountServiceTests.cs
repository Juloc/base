using Base.Accounts;

namespace Base.Tests;

/// <summary>First setup, sign-in and the account of a person, against a real database.</summary>
public class AccountServiceTests
{
    [Fact]
    public async Task The_first_account_needs_the_setup_code_from_the_log()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        Assert.True(await a.Service.SetupNeededAsync());

        var wrong = await a.Service.BeginSetupAsync("WRONG-CODE-0000", "jul", TestAccounts.Password, TestAccounts.Ip);
        Assert.Equal("setup.code", wrong.Error!.Code);
        Assert.Equal(403, wrong.Error.Status);
        Assert.Equal(0L, await db.ScalarAsync<long>("select count(*) from users"));

        await a.SetUpAsync();
        Assert.False(await a.Service.SetupNeededAsync());
        var again = await a.Service.BeginSetupAsync(a.SetupCode.Value, "eve", TestAccounts.Password, TestAccounts.Ip);
        Assert.Equal("setup.done", again.Error!.Code);
        Assert.Equal(1L, await db.ScalarAsync<long>("select count(*) from users"));
    }

    [Fact]
    public async Task Setup_refuses_weak_input_and_guessing_the_code_locks()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        Assert.Equal("account.name", (await a.Service.BeginSetupAsync(a.SetupCode.Value, "no spaces", TestAccounts.Password, TestAccounts.Ip)).Error!.Code);
        var weak = await a.Service.BeginSetupAsync(a.SetupCode.Value, "jul", "short", TestAccounts.Ip);
        Assert.Equal("password.short", weak.Error!.Code);
        Assert.Equal("password.short", weak.Error.Fields!["password"]);

        for (var i = 0; i < 5; i++) await a.Service.BeginSetupAsync("nope", "jul", TestAccounts.Password, "192.0.2.99");
        var locked = await a.Service.BeginSetupAsync(a.SetupCode.Value, "jul", TestAccounts.Password, "192.0.2.99");
        Assert.Equal(429, locked.Error!.Status);
        Assert.True(locked.Error.RetryAfter > 0);
    }

    [Fact]
    public async Task Nothing_in_the_database_can_be_used_to_sign_in()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (secret, _, recovery) = await a.SetUpAsync();

        Assert.StartsWith("argon2id$", await db.ScalarAsync<string>("select password_hash from users"));
        var storedSecret = await db.ScalarAsync<string>("select totp_secret from users");
        Assert.NotEqual(secret, storedSecret);
        Assert.DoesNotContain(secret, storedSecret);                                      // encrypted, the key is not in the database
        Assert.Equal(0L, await db.ScalarAsync<long>("select count(*) from recovery_codes where code_hash = any($1)", (object)recovery));
        Assert.Equal((long)RecoveryCodes.Count, await db.ScalarAsync<long>("select count(*) from recovery_codes"));
    }

    [Fact]
    public async Task Sign_in_needs_password_and_second_factor_and_fails_the_same_way_for_every_reason()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (secret, _, _) = await a.SetUpAsync();
        a.NextStep();

        var wrongPassword = await a.Service.LoginAsync("jul", "wrong password 123", a.Code(secret), TestAccounts.Ip, null);
        var wrongCode = await a.Service.LoginAsync("jul", TestAccounts.Password, "000000", TestAccounts.Ip, null);
        var noCode = await a.Service.LoginAsync("jul", TestAccounts.Password, null, TestAccounts.Ip, null);
        var unknown = await a.Service.LoginAsync("nobody", TestAccounts.Password, a.Code(secret), TestAccounts.Ip, null);
        foreach (var r in new[] { wrongPassword, wrongCode, noCode, unknown })
        {
            Assert.Equal("auth.failed", r.Error!.Code);
            Assert.Equal(401, r.Error.Status);
            Assert.Equal(wrongPassword.Error!.Message, r.Error.Message);
        }

        var ok = await a.Service.LoginAsync(" JUL ", TestAccounts.Password, a.Code(secret), TestAccounts.Ip, "agent");
        Assert.True(ok.Ok, ok.Error?.Code);
        Assert.Equal("jul", ok.Value.User);
        Assert.Contains(Roles.Admin, ok.Value.Roles);
    }

    [Fact]
    public async Task A_one_time_code_works_once_also_for_two_requests_at_once()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (secret, _, _) = await a.SetUpAsync();

        // the code of the setup is spent
        Assert.Equal("auth.failed", (await a.Service.LoginAsync("jul", TestAccounts.Password, a.Code(secret), TestAccounts.Ip, null)).Error!.Code);

        a.NextStep();
        var code = a.Code(secret);
        var both = await Task.WhenAll(a.Service.LoginAsync("jul", TestAccounts.Password, code, "192.0.2.1", null), a.Service.LoginAsync("jul", TestAccounts.Password, code, "192.0.2.2", null));
        Assert.Single(both, r => r.Ok);
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (_, _, recovery) = await a.SetUpAsync();

        var first = await a.Service.LoginAsync("jul", TestAccounts.Password, recovery[0].ToLowerInvariant(), TestAccounts.Ip, null);
        Assert.True(first.Ok, first.Error?.Code);
        Assert.Equal(RecoveryCodes.Count - 1, first.Value.RecoveryLeft);
        var second = await a.Service.LoginAsync("jul", TestAccounts.Password, recovery[0], TestAccounts.Ip, null);
        Assert.Equal("auth.failed", second.Error!.Code);
    }

    [Fact]
    public async Task Wrong_attempts_lock_the_account_even_for_the_right_password()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (secret, _, _) = await a.SetUpAsync();
        a.NextStep();

        for (var i = 0; i < 5; i++)
            Assert.Equal(401, (await a.Service.LoginAsync("jul", "wrong password 123", "000000", "192.0.2." + (20 + i), null)).Error!.Status);     // from changing addresses
        var locked = await a.Service.LoginAsync("jul", TestAccounts.Password, a.Code(secret), "192.0.2.50", null);
        Assert.Equal(429, locked.Error!.Status);
        Assert.InRange(locked.Error.RetryAfter, 1, 30);

        a.Clock.Advance(TimeSpan.FromSeconds(31));
        var ok = await a.Service.LoginAsync("jul", TestAccounts.Password, a.Code(secret), "192.0.2.50", null);
        Assert.True(ok.Ok, ok.Error?.Code);
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_and_signs_out_the_other_devices()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (secret, token, _) = await a.SetUpAsync();
        a.NextStep();
        var other = await a.Service.LoginAsync("jul", TestAccounts.Password, a.Code(secret), TestAccounts.Ip, null);
        var me = (await a.Sessions.ResolveAsync(token, rotate: false))!;

        Assert.Equal("password.current", (await a.Service.ChangePasswordAsync(me, "not my password", "another good password", TestAccounts.Ip)).Error!.Code);
        Assert.Equal("password.short", (await a.Service.ChangePasswordAsync(me, TestAccounts.Password, "short", TestAccounts.Ip)).Error!.Code);
        Assert.Equal("password.same", (await a.Service.ChangePasswordAsync(me, TestAccounts.Password, TestAccounts.Password, TestAccounts.Ip)).Error!.Code);

        Assert.True((await a.Service.ChangePasswordAsync(me, TestAccounts.Password, "another good password", TestAccounts.Ip)).Ok);
        Assert.NotNull(await a.Sessions.ResolveAsync(token, rotate: false));                       // this device stays
        Assert.Null(await a.Sessions.ResolveAsync(other.Value.Token, rotate: false));              // the other one is out

        a.NextStep();
        Assert.Equal("auth.failed", (await a.Service.LoginAsync("jul", TestAccounts.Password, a.Code(secret), TestAccounts.Ip, null)).Error!.Code);
        a.NextStep();
        Assert.True((await a.Service.LoginAsync("jul", "another good password", a.Code(secret), TestAccounts.Ip, null)).Ok);
    }

    [Fact]
    public async Task A_new_authenticator_replaces_the_old_one_and_the_recovery_codes()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (oldSecret, token, oldRecovery) = await a.SetUpAsync();
        var me = (await a.Sessions.ResolveAsync(token, rotate: false))!;

        Assert.Equal("password.current", (await a.Service.BeginTotpChangeAsync(me, "not my password", TestAccounts.Ip)).Error!.Code);
        Assert.Equal("totp.expired", (await a.Service.FinishTotpChangeAsync(me, "123456", TestAccounts.Ip)).Error!.Code);

        var begin = await a.Service.BeginTotpChangeAsync(me, TestAccounts.Password, TestAccounts.Ip);
        Assert.True(begin.Ok);
        Assert.Contains("issuer=Test%20App", begin.Value.Uri);
        Assert.Equal("totp.wrong", (await a.Service.FinishTotpChangeAsync(me, "000000", TestAccounts.Ip)).Error!.Code);
        var finish = await a.Service.FinishTotpChangeAsync(me, a.Code(begin.Value.Secret), TestAccounts.Ip);
        Assert.True(finish.Ok, finish.Error?.Code);
        Assert.Equal(RecoveryCodes.Count, finish.Value.Length);

        a.NextStep();
        Assert.Equal("auth.failed", (await a.Service.LoginAsync("jul", TestAccounts.Password, a.Code(oldSecret), TestAccounts.Ip, null)).Error!.Code);
        Assert.Equal("auth.failed", (await a.Service.LoginAsync("jul", TestAccounts.Password, oldRecovery[0], TestAccounts.Ip, null)).Error!.Code);
        Assert.True((await a.Service.LoginAsync("jul", TestAccounts.Password, a.Code(begin.Value.Secret), TestAccounts.Ip, null)).Ok);

        // a requested key that nobody confirmed runs out
        var late = await a.Service.BeginTotpChangeAsync(me, TestAccounts.Password, TestAccounts.Ip);
        a.Clock.Advance(AccountService.TotpChangeLifetime + TimeSpan.FromSeconds(1));
        Assert.Equal("totp.expired", (await a.Service.FinishTotpChangeAsync(me, a.Code(late.Value.Secret), TestAccounts.Ip)).Error!.Code);
    }

    [Fact]
    public async Task What_happens_to_an_account_is_written_to_its_history_without_secrets()
    {
        await using var db = await TestDb.CreateAsync();
        var a = new TestAccounts(db);
        var (secret, token, _) = await a.SetUpAsync();
        var me = (await a.Sessions.ResolveAsync(token, rotate: false))!;
        await a.Service.LoginAsync("jul", "wrong password 123", "000000", TestAccounts.Ip, null);
        await a.Service.ChangePasswordAsync(me, TestAccounts.Password, "another good password", TestAccounts.Ip);
        await a.Service.LogoutAsync(me, TestAccounts.Ip);

        var events = (await a.Service.HistoryAsync(me)).Select(e => e.Event).ToArray();
        Assert.Equal(["logout", "password.changed", "login.failed", "setup.done"], events);
        var all = string.Join(' ', (await a.Audit.ListAsync(100)).Select(e => e.Event + e.Detail + e.Ip));
        Assert.DoesNotContain(secret, all);
        Assert.DoesNotContain("password 123", all);
        Assert.Null(await a.Sessions.ResolveAsync(token, rotate: false));
    }
}
