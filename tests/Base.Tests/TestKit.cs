using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Base.Accounts;
using Base.Core;
using Base.Data;
using Base.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Base.Tests;

/// <summary>
/// An own schema per test in the test database (BASE_TEST_DB; scripts/test.sh starts a throwaway PostgreSQL and sets it).
/// Without it the database tests are skipped.
/// </summary>
internal sealed class TestDb : IAsyncDisposable
{
    public static string? Admin => Environment.GetEnvironmentVariable("BASE_TEST_DB");

    private readonly string _schema = "t_" + Guid.NewGuid().ToString("N");
    public NpgsqlDataSource Data { get; private set; } = null!;
    public string ConnectionString { get; private set; } = "";

    public static IReadOnlyList<MigrationSet> Sets => [AccountsModule.Migrations, BaseApp.Migrations, MigrationSet.FromAssembly(typeof(Program).Assembly, "app")];

    /// <param name="migrate">false: an empty schema (for the tests of the migrator itself)</param>
    public static async Task<TestDb> CreateAsync(bool migrate = true)
    {
        Assert.SkipWhen(Admin is null, "BASE_TEST_DB is not set (use scripts/test.sh)");
        var t = new TestDb();
        await using (var admin = NpgsqlDataSource.Create(Admin!))
            await admin.ExecAsync($"create schema {t._schema}", TestContext.Current.CancellationToken);
        // small, short-lived pools: many tests side by side must not use up the connections of the test database
        t.ConnectionString = new NpgsqlConnectionStringBuilder(Admin!) { SearchPath = t._schema, MaxPoolSize = 20, ConnectionIdleLifetime = 15 }.ConnectionString;
        t.Data = NpgsqlDataSource.Create(t.ConnectionString);
        if (migrate) await new Migrator(t.Data, Sets).RunAsync(TestContext.Current.CancellationToken);
        return t;
    }

    public Task<int> ExecAsync(string sql, params object?[] args) => Data.ExecAsync(sql, TestContext.Current.CancellationToken, args);

    public async Task<T> ScalarAsync<T>(string sql, params object?[] args) => (await Data.ScalarAsync<T>(sql, TestContext.Current.CancellationToken, args))!;

    public async ValueTask DisposeAsync()
    {
        await Data.DisposeAsync();
        await using var admin = NpgsqlDataSource.Create(Admin!);
        await admin.ExecAsync($"drop schema {_schema} cascade", CancellationToken.None);
    }
}

/// <summary>The account service on a test database, with a clock the test moves.</summary>
internal sealed class TestAccounts
{
    public const string Password = "correct horse battery";
    public const string Ip = "192.0.2.7";

    public ManualTime Clock { get; } = new(DateTimeOffset.UtcNow);
    public UserStore Users { get; }
    public SessionStore Sessions { get; }
    public AuditStore Audit { get; }
    public LoginLimiter Limiter { get; }
    public SetupCode SetupCode { get; } = new("TEST-CODE-0000");
    public AccountService Service { get; }
    public InviteStore Invites { get; }
    public PeopleService People { get; }
    public HubAccounts Hub { get; }
    public AppRoles Roles { get; } = new([new(Base.Accounts.Roles.Admin, new Localized("Administrator")), new("member", new Localized("Member")), new("viewer", new Localized("Viewer"))]);

    // one hasher for all tests: its warm-up (the dummy hash) is paid once
    private static readonly PasswordHasher Hasher = new();

    public TestAccounts(TestDb db)
    {
        Users = new UserStore(db.Data, new SecretBox(TestApp.Key));
        Sessions = new SessionStore(db.Data);
        Audit = new AuditStore(db.Data, Clock);
        Limiter = new LoginLimiter(Clock);
        Service = new AccountService(Users, Sessions, Hasher, Limiter, SetupCode, Audit, new AccountOptions("Test App"), Clock, NullLogger<AccountService>.Instance);
        Invites = new InviteStore(db.Data, Clock);
        People = new PeopleService(Users, Sessions, Invites, Hasher, Limiter, Audit, Roles, new AccountOptions("Test App"), Clock, NullLogger<PeopleService>.Instance);
        Hub = new HubAccounts(Users, Sessions, Audit, Roles, NullLogger<HubAccounts>.Instance);
    }

    /// <summary>The person behind a session token, as the guard sees them.</summary>
    public async Task<SessionIdentity> WhoAsync(string token) => (await Sessions.ResolveAsync(token, rotate: false))!;

    public string Code(string secret, int stepOffset = 0) => Totp.Hotp(Base32.Decode(secret), Clock.GetUtcNow().ToUnixTimeSeconds() / Totp.StepSeconds + stepOffset);

    /// <summary>Runs the first setup and returns the authenticator secret and the session token.</summary>
    public async Task<(string Secret, string Token, string[] Recovery)> SetUpAsync(string user = "jul")
    {
        var begin = await Service.BeginSetupAsync(SetupCode.Value, user, Password, Ip);
        Assert.True(begin.Ok, begin.Error?.Code);
        var finish = await Service.FinishSetupAsync(SetupCode.Value, user, Code(begin.Value.Secret), Ip, "test agent");
        Assert.True(finish.Ok, finish.Error?.Code);
        return (begin.Value.Secret, finish.Value.Token, begin.Value.Recovery);
    }

    /// <summary>Moves the clock to a fresh time step, so the next code is not the one already used.</summary>
    public void NextStep() => Clock.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));
}

/// <summary>The start app as a real server in memory (real middleware, real database).</summary>
internal sealed class TestApp(TestDb db, bool https = false) : WebApplicationFactory<Program>
{
    public const string SetupCode = "TEST-CODE-0000";
    public static readonly string Key = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());

    public string Origin => https ? "https://localhost" : "http://localhost";

    /// <summary>Changes the app's <see cref="Base.Web.BaseAppOptions"/> for one test (for example <c>Embeddable</c>).</summary>
    public Func<Base.Web.BaseAppOptions, Base.Web.BaseAppOptions>? Options { get; init; }

    /// <summary>Another folder as the app's <c>wwwroot</c> for one test (for example an app with its own <c>icon.svg</c>).</summary>
    public string? WebRoot { get; init; }

    public HttpClient Http() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false, BaseAddress = new Uri(Origin) });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("BASE_DB", db.ConnectionString);
        builder.UseSetting("BASE_SETUP_CODE", SetupCode);
        builder.UseSetting("BASE_SECRET_KEY", Key);
        builder.ConfigureServices(s => s.AddTransient<IStartupFilter, LoopbackFilter>());
        if (WebRoot is { } root) builder.UseWebRoot(root);
        if (Options is { } change)
            builder.ConfigureTestServices(s =>
            {
                var given = s.Single(d => d.ServiceType == typeof(Base.Web.BaseAppOptions));
                s.Remove(given);
                s.AddSingleton(change((Base.Web.BaseAppOptions)given.ImplementationInstance!));
            });
    }

    private sealed class LoopbackFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, n) => { ctx.Connection.RemoteIpAddress ??= IPAddress.Loopback; return n(ctx); });
            next(app);
        };
    }

    /// <summary>A request as the app's own page sends it: with the custom header and the matching origin on everything that changes something.</summary>
    public HttpRequestMessage Req(HttpMethod method, string url, string? cookie = null, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (method != HttpMethod.Get) { req.Headers.Add("X-Base", "1"); req.Headers.Add("Origin", Origin); }
        if (cookie is not null) req.Headers.Add("Cookie", cookie);
        return req;
    }

    public static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    public static string CookieOf(HttpResponseMessage r) => r.Headers.GetValues("Set-Cookie").First().Split(';')[0];

    /// <summary>Runs the setup assistant over HTTP and returns the session cookie ("name=value") and the authenticator secret.</summary>
    public async Task<(string Cookie, string Secret)> SignedInAsync(HttpClient c, string user = "jul")
    {
        var begin = await c.SendAsync(Req(HttpMethod.Post, "/api/setup/begin", null, new { code = SetupCode, user, password = TestAccounts.Password }));
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        var secret = (await Json(begin)).GetProperty("secret").GetString()!;
        var totp = Totp.Hotp(Base32.Decode(secret), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var finish = await c.SendAsync(Req(HttpMethod.Post, "/api/setup/finish", null, new { code = SetupCode, user, totp }));
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        return (CookieOf(finish), secret);
    }
}

/// <summary>Finds the files of the repository from the folder the tests run in.</summary>
internal static class Repo
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "base.slnx"))) return dir.FullName;
        throw new InvalidOperationException("base.slnx not found above " + AppContext.BaseDirectory);
    }

    public static string Path_(params string[] parts) => Path.Combine([Root, .. parts]);
    public static string Read(params string[] parts) => File.ReadAllText(Path_(parts));
}
