using Base.Data;
using Base.Web;
using Base.Web.Settings;
using StartApp.Items;

// The health check of the container: dotnet StartApp.dll health
if (await BaseApp.RunCommandAsync(args) is { } exitCode) return exitCode;

var builder = WebApplication.CreateSlimBuilder(args);
builder.AddBase(new BaseAppOptions("startapp", "Start App", typeof(Program).Assembly)
{
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
