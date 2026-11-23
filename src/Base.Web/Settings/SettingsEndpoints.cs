using System.Text.Json;
using Base.Accounts;
using Base.Core;
using Base.Web.Auth;

namespace Base.Web.Settings;

public sealed record SettingsResponse(Dictionary<string, JsonElement> Settings);
public sealed record SettingRequest(JsonElement? Value);

/// <summary>
/// The settings page of every app talks to these: a person's own settings, and (for the role admin) those of the installation.
/// A missing value means "the default"; the page knows the defaults.
/// </summary>
internal static class SettingsEndpoints
{
    private static readonly Error Unknown = Error.NotFound("setting.unknown", "Unknown setting.");
    private static readonly Error Bad = Error.Invalid("setting.invalid", "This value is not allowed.");

    private static Dictionary<string, JsonElement> Known(IReadOnlyDictionary<string, string> stored, SettingRules rules, SettingScope scope)
    {
        var result = new Dictionary<string, JsonElement>();
        foreach (var (key, json) in stored)
            if (rules.Find(scope, key) is not null) result[key] = JsonDocument.Parse(json).RootElement.Clone();
        return result;
    }

    public static void MapBaseSettings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings", async (HttpContext ctx, SettingsStore store, SettingRules rules) =>
            new SettingsResponse(Known(await store.GetAllAsync(ctx.Person().UserId, ctx.RequestAborted), rules, SettingScope.Person)));

        app.MapPut("/api/settings/{key}", async (string key, SettingRequest body, HttpContext ctx, SettingsStore store, SettingRules rules) =>
        {
            if (rules.Find(SettingScope.Person, key) is not { } rule) return Unknown.ToHttp();
            if (body.Value is not { } value || rule.Normalize(value) is not { } json) return Bad.ToHttp();
            await store.SetAsync(ctx.Person().UserId, key, json, ctx.RequestAborted);
            return Results.NoContent();
        });

        // reset: the server forgets the value, the page falls back to its default
        app.MapDelete("/api/settings/{key}", async (string key, HttpContext ctx, SettingsStore store, SettingRules rules) =>
        {
            if (rules.Find(SettingScope.Person, key) is null) return Unknown.ToHttp();
            await store.DeleteAsync(ctx.Person().UserId, key, ctx.RequestAborted);
            return Results.NoContent();
        });

        app.MapGet("/api/instance-settings", async (HttpContext ctx, SettingsStore store, SettingRules rules) =>
            ctx.Require(Roles.Admin) ?? Results.Ok(new SettingsResponse(Known(await store.GetInstanceAsync(ctx.RequestAborted), rules, SettingScope.Instance))));

        app.MapPut("/api/instance-settings/{key}", async (string key, SettingRequest body, HttpContext ctx, SettingsStore store, SettingRules rules, ProxyTrust proxies) =>
        {
            if (ctx.Require(Roles.Admin) is { } refused) return refused;
            if (rules.Find(SettingScope.Instance, key) is not { } rule) return Unknown.ToHttp();
            if (body.Value is not { } value || rule.Normalize(value) is not { } json) return Bad.ToHttp();
            await store.SetInstanceAsync(key, json, ctx.RequestAborted);
            if (key == ProxyTrust.SettingKey) proxies.Apply(json);
            return Results.NoContent();
        });

        app.MapDelete("/api/instance-settings/{key}", async (string key, HttpContext ctx, SettingsStore store, SettingRules rules, ProxyTrust proxies) =>
        {
            if (ctx.Require(Roles.Admin) is { } refused) return refused;
            if (rules.Find(SettingScope.Instance, key) is null) return Unknown.ToHttp();
            await store.DeleteInstanceAsync(key, ctx.RequestAborted);
            if (key == ProxyTrust.SettingKey) proxies.Apply(null);
            return Results.NoContent();
        });
    }
}
