using System.Text.RegularExpressions;

namespace Base.Tests;

/// <summary>
/// Rules that the files of the repository can show by themselves (docs/RULES.md names each test at its rule). They read the sources;
/// nothing is started. scripts/check-app.sh runs the same kind of checks on an app made from the base.
/// </summary>
public partial class RuleFileTests
{
    internal static IEnumerable<string> Files(string[] folders, string pattern) =>
        folders.Select(f => Repo.Path_(f)).Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, pattern, SearchOption.AllDirectories))
            .Where(f => !Regex.IsMatch(f.Replace('\\', '/'), "/(bin|obj|data|node_modules)/"));

    private static string Rel(string file) => Path.GetRelativePath(Repo.Root, file).Replace('\\', '/');

    private static IEnumerable<string> Scripts() => Files(["ui", "samples"], "*.js");
    private static IEnumerable<string> Pages() => Files(["ui", "samples"], "*.html");
    private static IEnumerable<string> Code() => Files(["src", "samples"], "*.cs");

    private static void None(IEnumerable<string> files, string pattern, string what, Func<string, bool>? except = null)
    {
        var re = new Regex(pattern);
        var hits = files.Where(f => except is null || !except(Rel(f)))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i)).Where(x => re.IsMatch(x.line)).Select(x => $"{Rel(x.f)}:{x.i + 1}: {x.line.Trim()}"))
            .ToArray();
        Assert.True(hits.Length == 0, what + ":\n" + string.Join("\n", hits.Take(20)));
    }

    // ---- CODE ----

    [Fact]
    public void Only_the_allowed_packages_are_referenced()
    {
        // CODE-02: what an app may reference; the base itself uses the first three (DuckDB is for apps that keep logs and statistics)
        string[] allowed = ["Npgsql", "Konscious.Security.Cryptography.Argon2", "Microsoft.IdentityModel.JsonWebTokens", "DuckDB.NET.Data", "DuckDB.NET.Data.Full"];
        string[] baseUses = ["Npgsql", "Konscious.Security.Cryptography.Argon2", "Microsoft.IdentityModel.JsonWebTokens"];
        var used = Files(["src", "samples"], "*.csproj").SelectMany(f => Regex.Matches(File.ReadAllText(f), "<PackageReference Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value)).Distinct().ToArray();
        Assert.Empty(used.Except(allowed));
        Assert.Equal(baseUses.Order(), used.Order());
    }

    [Fact]
    public void No_code_asks_the_system_clock_directly() =>
        None(Code(), @"\bDateTime(Offset)?\.(Now|UtcNow|Today)\b", "the time comes from a TimeProvider (CODE-05)");

    [Fact]
    public void The_browser_side_has_no_build_step_and_no_foreign_origin()
    {
        var all = Files(["ui", "samples"], "*").ToArray();
        Assert.DoesNotContain(all, f => Path.GetFileName(f) is "package.json" or "package-lock.json" or "tsconfig.json" or "vite.config.js" or "webpack.config.js");
        Assert.DoesNotContain(all, f => Path.GetExtension(f) is ".ts" or ".tsx" or ".jsx" or ".scss" or ".less");
        // the only address a page may name is the XML namespace of SVG, which is a name, not a request
        var web = all.Where(f => Path.GetExtension(f) is ".js" or ".css" or ".html" or ".webmanifest" or ".svg");
        var foreign = web.SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(https?:)?//[A-Za-z0-9.-]+\.[A-Za-z]{2,}[^\s'""`)]*").Select(m => (File: Rel(f), Url: m.Value)))
            .Where(x => !x.Url.StartsWith("http://www.w3.org/", StringComparison.Ordinal)).Select(x => $"{x.File}: {x.Url}").ToArray();
        Assert.True(foreign.Length == 0, "addresses of other servers in the browser side:\n" + string.Join("\n", foreign));
    }

    // ---- SEC ----

    [Fact]
    public void Pages_have_no_inline_script_style_or_handlers()
    {
        Assert.NotEmpty(Pages());
        None(Pages(), @"<[^>]+\sstyle\s*=", "inline style attribute (SEC-02)");
        None(Pages(), @"<[^>]+\son[a-z]+\s*=", "inline event handler (SEC-02)");
        None(Pages(), @"<style[\s>]", "style element in a page (SEC-02)");
        None(Pages(), @"<script(?![^>]*\ssrc=)[^>]*>", "script without src (SEC-02)");
        None(Pages(), @"javascript:", "javascript: address (SEC-02)");
        // and scripts do not write style or handler attributes either (setting el.style.x from script is fine, the policy allows it)
        None(Scripts(), @"setAttribute\(\s*['""](style|on[a-z]+)['""]", "style or handler attribute set from script (SEC-02)");
        None(Scripts(), @"[{,]\s*style:\s*['""`]", "style given as an attribute to h() (SEC-02)");
    }

    [Fact]
    public void Markup_is_parsed_in_one_place_only()
    {
        None(Scripts(), @"insertAdjacentHTML|document\.write|\beval\(|new Function\(|\.outerHTML\s*=", "markup or code made from text (SEC-05)");
        var writes = Scripts().SelectMany(f => File.ReadLines(f).Where(l => Regex.IsMatch(l, @"\.innerHTML\s*=")).Select(_ => Rel(f))).ToArray();
        Assert.Equal(["ui/base.js"], writes);        // the one function svg(), which only ever gets markup this code made itself
    }

    [Fact]
    public void Nothing_private_is_in_the_repository()
    {
        var files = Directory.EnumerateFiles(Repo.Root, "*", SearchOption.AllDirectories)
            .Where(f => !Regex.IsMatch(Rel(f), @"(^|/)(\.git|\.claude|bin|obj|data|node_modules|TestResults|shots)/") && new FileInfo(f).Length < 2_000_000).ToArray();
        Assert.DoesNotContain(files, f => Path.GetFileName(f) is ".env" or "secret.key" || Path.GetExtension(f) is ".pem" or ".pfx" or ".key");
        var findings = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var rel = Rel(file);
            // addresses: only loopback, "any", the documentation ranges (RFC 5737) and the private networks as such
            foreach (Match m in Regex.Matches(text, @"(?<![\d.])(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?![\d.])"))
            {
                var ip = m.Value;
                var ok = ip.StartsWith("127.", StringComparison.Ordinal) || ip.StartsWith("192.0.2.", StringComparison.Ordinal) || ip.StartsWith("198.51.100.", StringComparison.Ordinal)
                      || ip.StartsWith("203.0.113.", StringComparison.Ordinal) || ip is "0.0.0.0" or "10.0.0.0" or "10.0.0.1" or "172.16.0.0" or "192.168.0.0";
                if (!ok) findings.Add($"{rel}: address {ip}");
            }
            if (rel != "tests/Base.Tests/RuleFileTests.cs")
            {
                if (Regex.IsMatch(text, @"(?<![\w/.-])/home/[a-z][a-z0-9_-]+|(?<![\w/.-])/Users/[A-Za-z]|[A-Z]:\\Users\\")) findings.Add($"{rel}: a personal path");
                if (Regex.IsMatch(text, @"PRIVATE KEY-----|ghp_[A-Za-z0-9]{20}|github_pat_[A-Za-z0-9_]{20}|AKIA[0-9A-Z]{16}|xox[bp]-[0-9A-Za-z-]{10}")) findings.Add($"{rel}: looks like a key or token");
            }
            foreach (Match m in Regex.Matches(text, @"[A-Za-z0-9._+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+"))
                if (!Regex.IsMatch(m.Value, @"@(example\.(com|org|net)|[a-z0-9.-]+\.example|[a-z0-9.-]+\.test)$") && m.Value != "noreply@anthropic.com" && !Regex.IsMatch(m.Value, @"@[0-9a-f]{40}$"))
                    findings.Add($"{rel}: mail address {m.Value}");
        }
        // in code (not in the prose of docs/) a named server is only this machine, an example name, or the namespace of SVG
        foreach (var file in Files(["src", "ui", "samples", "scripts", "templates", "tests", ".github"], "*"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"https?://([A-Za-z0-9.-]+)"))
            {
                var host = m.Groups[1].Value;
                if (host is "localhost" or "127.0.0.1" or "www.w3.org" or "aka.ms" or "goo.gl" or "schemas.openid.net" || host.EndsWith(".example", StringComparison.Ordinal) || host.EndsWith(".test", StringComparison.Ordinal) || host == "example.com") continue;
                findings.Add($"{Rel(file)}: server name {host}");
            }
        Assert.True(findings.Count == 0, "possibly private (SEC-06):\n" + string.Join("\n", findings.Distinct().Take(30)));
    }

    // ---- OPS ----

    [Fact]
    public void The_compose_file_has_one_app_one_database_one_volume_and_no_variables()
    {
        // OPS-01, OPS-02 (minimal since 0.4.3): two services, two volumes, only the plain keys; the rest is in the image
        var compose = Repo.Read("samples", "StartApp", "docker-compose.yml");
        var code = string.Join('\n', compose.Split('\n').Select(l => Regex.Replace(l, @"\s+#.*$", "")).Where(l => !l.TrimStart().StartsWith('#')));
        Assert.DoesNotMatch(@"\$\{|\$[A-Za-z_(]", code);                                  // no variables, no interpolation
        Assert.DoesNotContain("env_file", code);
        Assert.DoesNotMatch(@"(?m)^\s+(security_opt|cap_drop|cap_add|read_only|tmpfs|healthcheck|user|entrypoint|command):", code);   // what the container needs is in the image

        var services = Regex.Matches(Regex.Match(code, @"(?s)^services:\n(.*?)^volumes:", RegexOptions.Multiline).Groups[1].Value, @"^  ([a-z0-9_-]+):\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(["app", "db"], services);                                             // one app, one PostgreSQL, no init container
        var allowed = new[] { "build", "image", "restart", "ports", "environment", "volumes", "networks", "depends_on" };
        foreach (Match key in Regex.Matches(code, @"(?m)^    ([a-z_]+):"))
            Assert.Contains(key.Groups[1].Value, allowed);
        var volumes = Regex.Matches(Regex.Match(code, @"(?s)^volumes:\n(.*)$", RegexOptions.Multiline).Groups[1].Value, @"^  ([a-z0-9_-]+):", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(["data", "db"], volumes);
        Assert.DoesNotMatch(@"(?m)^\s+- (\.|/|~)[^:\n]*:/", code);                         // no folder of the host is mounted

        var app = Regex.Match(code, @"(?s)^  app:\n(.*?)^  db:", RegexOptions.Multiline).Groups[1].Value;
        var db = Regex.Match(code, @"(?s)^  db:\n(.*?)^volumes:", RegexOptions.Multiline).Groups[1].Value;
        Assert.Matches(@"ports:\s*\n\s*- ""127\.0\.0\.1:\d+:8080""", app);                 // only this machine reaches the port
        Assert.DoesNotMatch(@"(?m)^\s+ports:", db);                                       // the database is not published: that is why its password may stand here
        Assert.Matches(@"POSTGRES_PASSWORD: \S+", db);

        var dockerfile = Repo.Read("samples", "StartApp", "Dockerfile");
        Assert.Contains("USER $APP_UID", dockerfile);                                      // not root
        Assert.Matches(@"(?m)^HEALTHCHECK .*""health""", dockerfile);
        Assert.Single(Regex.Matches(dockerfile, @"(?m)^ENTRYPOINT"));
    }

    [Fact]
    public void Only_the_documented_environment_settings_exist()
    {
        string[] allowed = ["BASE_DB", "BASE_DB_PASSWORD_FILE", "BASE_DATA", "BASE_SECRET_KEY", "BASE_SETUP_CODE", "BASE_UI_DIR", "ASPNETCORE_HTTP_PORTS"];
        var used = Code().SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?:cfg|Configuration|configuration)\[""([^""]+)""\]|GetEnvironmentVariable\(""([^""]+)""\)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)).Distinct().ToArray();
        Assert.Empty(used.Except(allowed));
        Assert.Contains("BASE_DB", used);
        // and each of them is named in the rule, so nobody has to read the code to find them
        var rule = Repo.Read("docs", "RULES.md");
        Assert.All(allowed.Where(a => a.StartsWith("BASE_", StringComparison.Ordinal)), a => Assert.Contains($"`{a}`", rule));
    }

    [Fact]
    public void The_version_is_semver_and_has_a_changelog_entry()
    {
        var version = Repo.Read("VERSION").Trim();
        Assert.Matches(@"^\d+\.\d+\.\d+(-[a-z]+\.\d+)?$", version);
        Assert.Matches($@"(?m)^## {Regex.Escape(version)}\b", Repo.Read("CHANGELOG.md"));
        Assert.DoesNotContain("<Version>", Regex.Replace(Repo.Read("Directory.Build.props"), @"<Version>\$\(\[System\.IO\.File\][^<]+</Version>", ""));     // the number stands in VERSION only
    }

    [Fact]
    public void Actions_are_pinned_by_commit()
    {
        var workflows = Files([".github", "templates"], "*.yml").ToArray();
        Assert.NotEmpty(workflows);
        foreach (var file in workflows)
        {
            var text = File.ReadAllText(file);
            var uses = Regex.Matches(text, @"uses:\s*(\S+)").Select(m => m.Groups[1].Value).ToArray();
            Assert.NotEmpty(uses);
            Assert.All(uses, u => Assert.Matches(@"^[\w.-]+/[\w./-]+@[0-9a-f]{40}$", u));
            Assert.Matches(@"(?m)^permissions:\s*\n\s+contents: read\s*$", text);
        }
    }

    // ---- DIALOG, COLOR, A11Y ----

    [Fact]
    public void No_blocking_dialogs() =>
        None(Scripts(), @"(?<![\w.])(alert|confirm|prompt)\(|window\.(alert|confirm|prompt)\(|\.showModal\(", "a dialog that blocks the page (DIALOG-01)");

    [Fact]
    public void Dialogs_are_solid_and_close_when_the_page_changes()
    {
        var styles = Repo.Read("ui", "base.css");
        var script = Repo.Read("ui", "base.js");
        Assert.Matches(@"\.dialog\s*\{[^}]*background:\s*var\(--surface\)", styles);           // solid, never see-through
        Assert.DoesNotMatch(@"\.dialog\s*\{[^}]*(backdrop-filter|--glass)", styles);
        Assert.Matches(@"addEventListener\('hashchange',\s*\(\)\s*=>\s*\{\s*closeDialogs\(\)", script);   // the router closes dialogs when the page changes
        Assert.Contains("side === 'start'", script);                                           // delete and deactivate bottom left
        Assert.Matches(@"\.dialog-foot\s*\{", styles);                                          // the buttons stand outside the scrolling body
    }

    [Fact]
    public void Selects_are_our_own_dropdown()
    {
        None(Scripts(), @"h\('select'", "a native select (use dropdown(), KIT-02)");
        var script = Repo.Read("ui", "base.js");
        foreach (var word in new[] { "role: 'combobox'", "role: 'listbox'", "role: 'option'", "aria-activedescendant", "'Escape'", "'Home'", "'ArrowDown'" })
            Assert.Contains(word, script);
    }

    [Fact]
    public void The_phone_menu_is_a_pill_of_all_places()
    {
        var styles = Repo.Read("ui", "base.css");
        var script = Repo.Read("ui", "base.js");
        Assert.Matches(@"\.shell-tabs\s*\{[^}]*background:\s*var\(--surface\)", styles);   // solid white pill, not glass
        Assert.Matches(@"\.tab\s*\{[^}]*flex:\s*1 0 auto;\s*min-width:\s*68px", styles);   // as wide as its name, at least 68 px
        Assert.DoesNotContain("nav.slice(0, 5)", script);                                      // every place, not the first five
        Assert.Contains("export function navBadge(", script);
    }

    [Fact]
    public void An_app_sets_its_colour_with_one_variable()
    {
        const string literal = @"#[0-9a-fA-F]{3,8}\b|\b(rgb|rgba|hsl|hsla|oklch|oklab|lab|lch|hwb)\(";
        foreach (var css in Files(["samples"], "*.css"))
        {
            var text = Regex.Replace(File.ReadAllText(css), @"/\*.*?\*/", "", RegexOptions.Singleline);
            // exactly one of the two: a colour (any CSS colour, the one place a colour value may stand) or a hue
            var colour = Regex.Matches(text, @"--accent-color:\s*[^;{}]+;");
            var hue = Regex.Matches(text, @"--accent-hue:\s*\d{1,3};");
            Assert.Equal(1, colour.Count + hue.Count);
            Assert.DoesNotMatch(literal, colour.Count == 1 ? text.Replace(colour[0].Value, "") : text);
            Assert.DoesNotMatch(@"--(?!accent-hue|accent-color|danger-hue)[a-z0-9-]+\s*:", text);        // an app does not redefine tokens (the red of faults may turn by its hue)
        }
        // in the base itself, colour values live in tokens.css; the styles only use the tokens (the white ground of the QR code is the one exception: a scanner needs it)
        var styles = Regex.Replace(Repo.Read("ui", "base.css"), @"/\*.*?\*/|url\(""data:[^""]*""\)", "", RegexOptions.Singleline);
        Assert.Equal(["#fff"], Regex.Matches(styles, literal).Select(m => m.Value));
        Assert.Matches(@"\.qr\s*\{[^}]*#fff", styles);
        // the colour of the app reaches the tokens through one place
        Assert.Matches(@"--accent-base:\s*var\(--accent-color,\s*var\(--accent-from-hue\)\)", Repo.Read("ui", "tokens.css"));
    }

    [Fact]
    public void Buttons_keep_their_shape_and_text_in_the_chrome_cannot_be_selected()
    {
        var styles = Repo.Read("ui", "base.css");
        Assert.DoesNotMatch(@"\.(btn|tile)[^{]*:active\s*\{[^}]*border-radius", styles);      // no change of shape when pressed
        Assert.DoesNotMatch(@"\.btn\s*\{[^}]*transition:[^;}]*border-radius", styles);
        Assert.Matches(@"\.btn\s*\{[^}]*font:\s*700 ", styles);                             // bold
        Assert.Matches(@"\.btn\.mini\s*\{[^}]*min-height:\s*34px", styles);                   // the row button, with the 44 px target around it
        Assert.Matches(@"\.btn\.mini::after\s*\{[^}]*inset:\s*-5px", styles);
        Assert.Matches(@"\.nav,[^{]*\.tab,[^{]*\.btn,[^{]*\.chip,[^{]*\.table th[^{]*\{[^}]*user-select:\s*none", styles);
        Assert.Contains("--hair", Repo.Read("ui", "tokens.css"));
        Assert.Matches(@"\.table td\s*\{[^}]*var\(--hair\)", styles);
    }

    [Fact]
    public void Focus_is_visible_and_motion_can_be_switched_off()
    {
        var styles = Repo.Read("ui", "base.css");
        Assert.Matches(@":focus-visible\s*\{\s*outline:\s*2px solid var\(--accent-ink\)", styles);
        Assert.Matches(@"@media \(prefers-reduced-motion: reduce\)\s*\{[^}]*animation-duration:[^}]*transition-duration:", styles);
        // where the outline is taken away, something else shows the focus on the same element
        foreach (Match m in Regex.Matches(styles, @"([^{}]+)\{([^{}]*outline:\s*none[^{}]*)\}"))
            Assert.True(m.Groups[2].Value.Contains("box-shadow") || m.Groups[2].Value.Contains("background") || m.Groups[1].Value.Trim() == ".shell-main",
                "outline: none without another sign of focus: " + m.Groups[1].Value.Trim());
    }

    // ---- I18N ----

    private static Dictionary<string, string> Dictionary(string file)
    {
        var entries = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(file).Select(l => l.Trim()))
        {
            if (line.Length == 0 || line.StartsWith("/*", StringComparison.Ordinal) || line.StartsWith('*') || line is "export default {" or "};") continue;
            var m = Regex.Match(line, @"^'([A-Za-z0-9_.-]+)': '((?:[^'\\]|\\.)*)',$");
            Assert.True(m.Success, $"{Rel(file)}: not of the form 'key': 'text', -> {line}");
            Assert.True(entries.TryAdd(m.Groups[1].Value, m.Groups[2].Value), $"{Rel(file)}: key twice: {m.Groups[1].Value}");
        }
        return entries;
    }

    private static IEnumerable<string> GermanFiles() => Files(["ui", "samples"], "de.js").Where(f => Rel(f).Contains("/i18n/"));

    [Fact]
    public void German_and_English_have_the_same_keys()
    {
        Assert.Equal(2, GermanFiles().Count());
        foreach (var de in GermanFiles())
        {
            var german = Dictionary(de);
            var english = Dictionary(Path.Combine(Path.GetDirectoryName(de)!, "en.js"));
            Assert.NotEmpty(german);
            Assert.Empty(german.Keys.Except(english.Keys));
            Assert.Empty(english.Keys.Except(german.Keys));
            Assert.All(german, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
            Assert.All(english, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
            // the same placeholders in both languages
            foreach (var (key, text) in german)
                Assert.Equal(Regex.Matches(text, @"\{\w+\}").Select(m => m.Value).Order(), Regex.Matches(english[key], @"\{\w+\}").Select(m => m.Value).Order());
        }
    }

    private static HashSet<string> AllKeys() => [.. GermanFiles().SelectMany(f => Dictionary(f).Keys)];

    [Fact]
    public void Every_key_used_in_the_scripts_exists()
    {
        var keys = AllKeys();
        var missing = new List<string>();
        var used = 0;
        foreach (var file in Scripts().Where(f => Path.GetFileName(f) is not ("catalog.js" or "i18n.js" or "de.js" or "en.js")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"(?<![\w.])t\('([A-Za-z0-9_.-]+)'[,)]")) { used++; if (!keys.Contains(m.Groups[1].Value)) missing.Add($"{Rel(file)}: {m.Groups[1].Value}"); }
            foreach (Match m in Regex.Matches(text, @"(?<![\w.])tp\('([A-Za-z0-9_.-]+)'"))
                foreach (var form in new[] { ".one", ".other" }) { used++; if (!keys.Contains(m.Groups[1].Value + form)) missing.Add($"{Rel(file)}: {m.Groups[1].Value}{form}"); }
        }
        Assert.True(used > 100, "the calls of t() were not found");
        Assert.True(missing.Count == 0, "texts that are missing (I18N-01):\n" + string.Join("\n", missing.Distinct()));
        // the events of the account history are shown by their code
        var events = Code().SelectMany(f => Regex.Matches(File.ReadAllText(f), @"AuditAsync\([^,]+, ""([a-z_.]+)""").Select(m => m.Groups[1].Value)).Distinct().ToArray();
        Assert.True(events.Length >= 6);
        Assert.All(events, e => Assert.Contains("history." + e, keys));
        // a page shows no text of its own: what stands in an HTML page of an app is the title and the noscript line only
        foreach (var page in Pages().Where(p => Path.GetFileName(p) != "catalog.html"))
            Assert.DoesNotMatch(@"<(p|h[1-6]|button|label|a|span|li)\b[^>]*>\s*[A-Za-zÄÖÜ]", File.ReadAllText(page));
    }

    [Fact]
    public void Every_error_code_of_the_server_has_a_text()
    {
        var keys = AllKeys();
        var codes = new HashSet<string>();
        foreach (var file in Code())
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in new[]
            {
                @"Error\.(?:Invalid|Conflict|Forbidden|NotFound|Unauthorized)\(""([a-z_.]+)""",      // Error.Invalid("code", ...)
                @"new(?: Error)?\(\s*ErrorKind\.\w+,\s*""([a-z_.]+)""",                                // new Error(ErrorKind.X, "code", ...)
                @"\?\s*""([a-z_.]+)""\s*:\s*""([a-z_.]+)""\s*,",                                        // a choice between two codes
                @"string code = ""([a-z_.]+)""",                                                       // the defaults of Error.NotFound() and friends
                @"Field\(""\w+"",\s*""([a-z_.]+)""",                                                   // Field("user", "code", ...)
                @"return ""(password\.[a-z_]+)""",                                                     // the password policy
            })
                foreach (Match m in Regex.Matches(text, pattern))
                    foreach (var g in m.Groups.Cast<Group>().Skip(1).Where(g => g.Success)) codes.Add(g.Value);
        }
        Assert.Superset(new HashSet<string> { "auth.failed", "auth.none", "too_many", "too_large", "input", "invalid", "not_found", "forbidden", "setup.code", "password.short", "totp.wrong", "request.refused", "role.missing", "setting.invalid" }, codes);
        var missing = codes.Where(c => !keys.Contains("err." + c)).Order().ToArray();
        Assert.True(missing.Length == 0, "error codes without a text err.<code> (I18N-02):\n" + string.Join("\n", missing));
        // the codes of single fields
        var fieldCodes = Regex.Matches(Repo.Read("src", "Base.Core", "Validator.cs"), @"(\w+) = ""([a-z_]+)""").Select(m => m.Groups[2].Value).ToArray();
        Assert.Equal(5, fieldCodes.Length);
        Assert.All(fieldCodes, c => Assert.Contains("field." + c, keys));
    }

    // ---- KIT, MOBILE ----

    [Fact]
    public void Every_part_of_the_look_is_in_the_catalogue()
    {
        var catalogue = Repo.Read("ui", "catalog.html") + Repo.Read("ui", "catalog.js");
        string[] classes = ["btn", "field", "input", "textarea", "select", "switch", "check", "seg", "card", "list", "row", "table", "badge", "notice", "empty", "spinner", "skeleton", "capsule", "chip", "tile", "tiles", "search", "panel", "tabbar"];
        Assert.All(classes, c => Assert.Matches($@"class=""[^""]*\b{c}\b", catalogue));
        string[] functions = ["toast(", "openDialog(", "confirmDialog(", "openMenu(", "field(", "passwordField(", "submit(", "segmented(", "shell(", "router(", "floatBar(", "capsule(", "dropdown(", "choiceTiles(", "actionBar(", "selectionBar(", "navBadge(", "closeDialogs(", "table(", "tabs(", "chips(", "numberBox(", "chart(", "liveFeed(", "timeline(", "ring(", "foldCard(", "flash(", "authCard(", "stateCard(", "mountAuth(", "pwa("];
        Assert.All(functions, f => Assert.Contains(f, catalogue));
        Assert.Contains("Object.keys(GLYPHS)", catalogue);                                 // every icon, by itself
        // every colour token is shown
        var tokens = Regex.Matches(Repo.Read("ui", "tokens.css"), @"^\s*--([a-z0-9-]+):\s*light-dark\(", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
        Assert.All(tokens, t => Assert.Contains($"'{t}'", catalogue));
    }

    [Fact]
    public void An_app_has_no_worker_list_of_its_own_and_the_page_names_the_manifest_and_the_icon()
    {
        // the worker (SEC-08) is made by the base (PwaTests); the start app keeps no copy of it, of the manifest or of the icons
        var wwwroot = Path.Combine(Repo.Root, "samples", "StartApp", "wwwroot");
        foreach (var name in new[] { "sw.js", "manifest.webmanifest", "icon.svg", "icon-maskable.svg" }) Assert.False(File.Exists(Path.Combine(wwwroot, name)), name);
        var page = Repo.Read("samples", "StartApp", "wwwroot", "index.html");
        Assert.Contains("rel=\"manifest\" href=\"/manifest.webmanifest\"", page);
        Assert.Contains("rel=\"apple-touch-icon\" href=\"/apple-touch-icon.png\"", page);
        Assert.Contains("pwa();", Repo.Read("samples", "StartApp", "wwwroot", "app.js"));
    }
}

