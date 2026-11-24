using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection;

namespace Base.Tests;

/// <summary>
/// A small central sign-in for the tests: it speaks the standard (description, keys, token endpoint, logout tokens) and signs with a key made for the test,
/// so the whole flow runs without JulOS. It can also sign wrongly on purpose: another key, another issuer, another audience, an expired token.
/// </summary>
internal sealed class TestHub : IAsyncDisposable
{
    public const string ClientId = "startapp";
    public const string Secret = "test-hub-secret-0000";
    public const string Kid = "test-key-1";

    private sealed record Grant(string Challenge, string RedirectUri, Dictionary<string, object> Claims, ECDsa? SignWith, string? Alg);

    private readonly WebApplication _app;
    private readonly Dictionary<string, Grant> _grants = [];
    public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public ECDsa OtherKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public string Url { get; private set; } = "";
    public List<Dictionary<string, string>> TokenRequests { get; } = [];
    public string? AboutName { get; set; } = "Test Hub";
    /// <summary>What the description claims as issuer, when it is not the address it is served from.</summary>
    public string? ReportedIssuer { get; set; }

    private TestHub() => _app = WebApplication.CreateSlimBuilder().Build();

    public static async Task<TestHub> StartAsync()
    {
        var hub = new TestHub();
        await hub.RunAsync();
        return hub;
    }

    private static string B64(byte[] bytes) => Base64UrlEncoder.Encode(bytes);

    private async Task RunAsync()
    {
        // WebApplication cannot be changed after Build(); a fresh builder carries the routes
        await _app.DisposeAsync();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        Map(app);
        await app.StartAsync();
        Url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/');
        _running = app;
    }

    private WebApplication? _running;

    private void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = ReportedIssuer ?? Url, ["authorization_endpoint"] = Url + "/oidc/authorize", ["token_endpoint"] = Url + "/oidc/token", ["jwks_uri"] = Url + "/oidc/jwks",
            ["end_session_endpoint"] = Url + "/oidc/end-session", ["code_challenge_methods_supported"] = new[] { "S256" },
        }));
        app.MapGet("/oidc/jwks", () =>
        {
            var p = Key.ExportParameters(false);
            return Results.Json(new { keys = new[] { new { kty = "EC", crv = "P-256", x = B64(p.Q.X!), y = B64(p.Q.Y!), kid = Kid, use = "sig", alg = "ES256" } } });
        });
        app.MapGet("/api/about", () => AboutName is null ? Results.Unauthorized() : Results.Json(new { name = AboutName }));
        app.MapPost("/oidc/token", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var posted = form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
            TokenRequests.Add(posted);
            var basic = ctx.Request.Headers.Authorization.ToString();
            var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{Secret}"));
            if (basic != expected) return Results.Json(new { error = "invalid_client" }, statusCode: 401);
            if (!_grants.Remove(posted.GetValueOrDefault("code", ""), out var g)) return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            var challenge = B64(SHA256.HashData(Encoding.ASCII.GetBytes(posted.GetValueOrDefault("code_verifier", ""))));
            if (challenge != g.Challenge || posted.GetValueOrDefault("redirect_uri") != g.RedirectUri) return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            return Results.Json(new { id_token = Sign(g.Claims, g.SignWith ?? Key, g.Alg, "JWT"), access_token = "unused", token_type = "Bearer", expires_in = 300 });
        });
    }

    private string Sign(Dictionary<string, object> claims, ECDsa key, string? alg, string typ)
    {
        var payload = JsonSerializer.Serialize(claims);
        var handler = new JsonWebTokenHandler();
        if (alg == "none") return B64(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}")) + "." + B64(Encoding.UTF8.GetBytes(payload)) + ".";
        SigningCredentials credentials = alg == "HS256"
            ? new SigningCredentials(new SymmetricSecurityKey(SHA256.HashData(Encoding.UTF8.GetBytes(Url))) { KeyId = Kid }, SecurityAlgorithms.HmacSha256)
            : new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = Kid }, SecurityAlgorithms.EcdsaSha256);
        return handler.CreateToken(payload, credentials, new Dictionary<string, object> { ["typ"] = typ });
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>
    /// What the browser does at the hub: takes the address the app sent it to, signs the person in, and returns the address the hub sends the browser back to.
    /// <paramref name="tamper"/> changes the claims of the ID token before it is signed (a wrong nonce, issuer, audience, an old expiry ...).
    /// </summary>
    public string Authorize(string authorizeUrl, string sub, string? role, string? name = "anna", string sid = "sid-1", Action<Dictionary<string, object>>? tamper = null,
        ECDsa? signWith = null, string? alg = null)
    {
        var uri = new Uri(authorizeUrl);
        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal(Url + "/oidc/authorize", uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("code", q["response_type"]);
        Assert.Equal(ClientId, q["client_id"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("openid profile", q["scope"]);
        var claims = new Dictionary<string, object>
        {
            ["iss"] = Url, ["sub"] = sub, ["aud"] = ClientId, ["iat"] = Now(), ["exp"] = Now() + 300, ["auth_time"] = Now(),
            ["nonce"] = q["nonce"]!, ["sid"] = sid, ["amr"] = new[] { "pwd", "otp" },
        };
        if (name is not null) { claims["name"] = name; claims["preferred_username"] = name; }
        if (role is not null) claims["role"] = role;
        tamper?.Invoke(claims);
        var code = Rnd.Hex();
        _grants[code] = new Grant(q["code_challenge"]!, q["redirect_uri"]!, claims, signWith, alg);
        return q["redirect_uri"] + "?code=" + code + "&state=" + Uri.EscapeDataString(q["state"]!) + "&iss=" + Uri.EscapeDataString(Url);
    }

    /// <summary>A logout token as the hub sends it to the registered address.</summary>
    public string LogoutToken(string? sid, string? sub = null, string? jti = null, Action<Dictionary<string, object>>? tamper = null, ECDsa? signWith = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["iss"] = Url, ["aud"] = ClientId, ["iat"] = Now(), ["exp"] = Now() + 120, ["jti"] = jti ?? Rnd.Hex(),
            ["events"] = new Dictionary<string, object> { ["http://schemas.openid.net/event/backchannel-logout"] = new Dictionary<string, object>() },
        };
        if (sid is not null) claims["sid"] = sid;
        if (sub is not null) claims["sub"] = sub;
        tamper?.Invoke(claims);
        return Sign(claims, signWith ?? Key, null, "logout+jwt");
    }

    public async ValueTask DisposeAsync()
    {
        if (_running is not null) await _running.DisposeAsync();
        Key.Dispose(); OtherKey.Dispose();
    }

    /// <summary>The hub goes away (the port stays closed): for the test of an unreachable hub.</summary>
    public async Task StopAsync()
    {
        if (_running is not null) { await _running.StopAsync(); await _running.DisposeAsync(); _running = null; }
    }
}

internal static class Rnd
{
    public static string Hex() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
