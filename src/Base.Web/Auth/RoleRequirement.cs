namespace Base.Web.Auth;

/// <summary>The role an endpoint needs. Declared with <see cref="Endpoints.RequireRole{T}"/>, checked in one place: <see cref="ApiGuard"/> (SEC-03).</summary>
public sealed record RoleRequirement(string Role);

public static class Endpoints
{
    /// <summary>
    /// Says that only people with this role may call the endpoint (or every endpoint of a group). The guard answers 403 before the handler runs,
    /// so a handler cannot forget the check, and a test can list which endpoints demand which role.
    /// </summary>
    public static T RequireRole<T>(this T builder, string role) where T : IEndpointConventionBuilder => builder.WithMetadata(new RoleRequirement(role));
}
