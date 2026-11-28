namespace Base.Web;

/// <summary>
/// The security headers of every answer (SEC-01). The content policy allows only what the app itself serves: no inline script, no inline style,
/// no foreign origin, no framing except by the connected hub when the app allows it (SEC-02). That is why the look has no build step and no CDN: everything comes from this server.
/// </summary>
public static class SecurityHeaders
{
    public const string Csp = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; "
                            + "manifest-src 'self'; worker-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";

    /// <summary>The policy when the app may be shown in a window of the connected hub: only that origin may frame it (an origin from <see cref="HubUrl.Normalize"/>, so it holds no space or semicolon).</summary>
    public static string CspFramedBy(string origin) => Csp.Replace("frame-ancestors 'none'", "frame-ancestors " + origin, StringComparison.Ordinal);

    public const string Permissions = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    /// <param name="frameAncestor">The origin of the connected hub when the app may be shown in its window (<see cref="BaseAppOptions.Embeddable"/>), else null: no framing at all.</param>
    public static void Apply(HttpContext ctx, string? frameAncestor = null)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        if (frameAncestor is null) h["X-Frame-Options"] = "DENY";       // with an allowed ancestor only frame-ancestors speaks (X-Frame-Options cannot name one)
        h["Content-Security-Policy"] = frameAncestor is null ? Csp : CspFramedBy(frameAncestor);
        h["Permissions-Policy"] = Permissions;
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Cross-Origin-Resource-Policy"] = "same-origin";
        if (ctx.Request.IsHttps) h["Strict-Transport-Security"] = "max-age=31536000";
        // answers of the API are never kept by a browser or a proxy
        if (ctx.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
    }
}
