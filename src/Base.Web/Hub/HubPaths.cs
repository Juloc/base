namespace Base.Web;

/// <summary>The three public doors of the central sign-in (listed in <see cref="Auth.ApiGuard.BasePublicPaths"/>).</summary>
public static class HubPaths
{
    public const string Begin = "/api/auth/hub/begin";
    public const string Callback = "/api/auth/hub/callback";
    public const string BackChannelLogout = "/api/auth/hub/backchannel-logout";
}
