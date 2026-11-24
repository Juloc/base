using System.Reflection;
using Base.Accounts;
using Base.Core;
using Base.Data;
using Base.Web.Settings;

namespace Base.Web;

/// <summary>What an app tells the base about itself.</summary>
/// <param name="Id">Short technical name (lower case, digits, dash): names the session cookie, so two apps on one host do not collide.</param>
/// <param name="Name">The name people see; also shown by authenticator apps.</param>
/// <param name="Assembly">The app's own library: its version is the version of the app.</param>
public sealed record BaseAppOptions(string Id, string Name, Assembly Assembly)
{
    /// <summary>The app's own numbered SQL scripts; they run after those of the base.</summary>
    public IReadOnlyList<MigrationSet> Migrations { get; init; } = [];

    /// <summary>The app's own settings (the base brings language, light or dark and the trusted proxies).</summary>
    public IReadOnlyList<SettingRule> Settings { get; init; } = [];

    /// <summary>Paths under /api that answer without a session. Keep this list short; every entry is a door (SEC-03).</summary>
    public IReadOnlyList<string> PublicPaths { get; init; } = [];

    /// <summary>What the app is, in a sentence people read (English required, German expected). The app's name when not given.</summary>
    public Localized? Description { get; init; }

    /// <summary>The icon of the app: a name from the icon file of the base (<c>ui/icons.js</c>).</summary>
    public string Icon { get; init; } = "grid";

    /// <summary>The colour of the app as an angle on the colour wheel (0 to 359): the same number as <c>--accent-hue</c> in its stylesheet (COLOR-01).</summary>
    public int AccentHue { get; init; } = 250;

    /// <summary>Where the app starts, a path on its own address.</summary>
    public string Start { get; init; } = "/";

    /// <summary>The roles of the app: a small fixed list, one per person, always with "admin" (SEC-03). Default: admin and member.</summary>
    public IReadOnlyList<AppRole> Roles { get; init; } = AppRoles.Default;

    /// <summary>Paths under /api that accept large bodies (uploads); each of them checks its own limit.</summary>
    public IReadOnlyList<string> LargeBodyPaths { get; init; } = [];
}

/// <summary>Name and versions of the running app: its own version, the commit it was built from, and the version of the base it was made with.</summary>
public sealed record AppInfo(string Id, string Name, string Version, string? Commit, string BaseVersion)
{
    /// <summary>The version of the contract between an app and the hub that this base speaks (docs/CONTRACT.md). A change that breaks a reader raises it.</summary>
    public const int ContractVersion = 1;

    public static AppInfo From(BaseAppOptions options)
    {
        var (version, commit) = Parse(options.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        return new AppInfo(options.Id, options.Name, version, commit, ReadBaseVersion());
    }

    /// <summary>Splits "1.2.3+commit" (the build appends the commit) into version and short commit.</summary>
    public static (string Version, string? Commit) Parse(string? informationalVersion)
    {
        var text = (informationalVersion ?? "").Trim();
        var plus = text.IndexOf('+');
        var version = plus < 0 ? text : text[..plus];
        var commit = plus < 0 ? "" : text[(plus + 1)..];
        return (version.Length == 0 ? "0.0.0" : version, commit.Length == 0 ? null : commit[..Math.Min(7, commit.Length)]);
    }

    private static string ReadBaseVersion()
    {
        using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream("base/VERSION");
        if (stream is null) return "0.0.0";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
