using Base.Accounts;
using Base.Core;
using Base.Data;
using Base.Web;
using Base.Web.Settings;
using StartApp.Items;

// The health check of the container: dotnet StartApp.dll health
if (await BaseApp.RunCommandAsync(args) is { } exitCode) return exitCode;

var builder = WebApplication.CreateSlimBuilder(args);
// The one place where the app says what it is (read by /api/about, the sign-in pages and the people management).
builder.AddBase(new BaseAppOptions("startapp", "Start App", typeof(Program).Assembly)
{
    Description = new Localized("A small list of things to remember: the pattern of every app of the family.", "Eine kleine Merkliste: das Muster aller Apps der Familie."),
    Icon = "bookmark",
    AccentHue = 265,                                  // the same number as --accent-hue in wwwroot/app.css
    Start = "/",
    Roles =
    [
        new AppRole("admin", new Localized("Administrator", "Administration")),
        new AppRole("member", new Localized("Member", "Mitglied")),
        new AppRole("viewer", new Localized("Viewer", "Leser")),
    ],
    Migrations = [MigrationSet.FromAssembly(typeof(Program).Assembly, "app")],
    Settings = [SettingRule.Bool("items.hideDone", SettingScope.Person)],
});
builder.Services.AddSingleton<ItemStore>();

var app = builder.Build();
await app.StartBaseAsync();
app.UseBase();
app.MapBase();
app.MapItems();
app.Run();
return 0;

public partial class Program;
