using Base.Data;

namespace Base.Tests;

public class MigratorTests
{
    private static MigrationSet Set(string name, params (string File, string Sql)[] scripts) =>
        MigrationSet.From(name, [.. scripts.Select(s => new Migration($"{name}/{s.File}", s.Sql))]);

    [Fact]
    public async Task Scripts_run_in_the_order_of_their_numbers_and_only_once()
    {
        await using var db = await TestDb.CreateAsync(migrate: false);
        var set = Set("demo", ("002_more", "alter table t add column b int"), ("001_first", "create table t (a int)"));

        var first = await new Migrator(db.Data, [set]).RunAsync();
        Assert.Equal(["demo/001_first", "demo/002_more"], first);

        var second = await new Migrator(db.Data, [set]).RunAsync();
        Assert.Empty(second);
        Assert.Equal(2L, await db.ScalarAsync<long>("select count(*) from schema_migrations"));
    }

    [Fact]
    public async Task A_new_script_is_added_later_and_sets_run_in_the_order_given()
    {
        await using var db = await TestDb.CreateAsync(migrate: false);
        await new Migrator(db.Data, [Set("one", ("001_a", "create table a (x int)"))]).RunAsync();
        var applied = await new Migrator(db.Data, [Set("one", ("001_a", "create table a (x int)"), ("002_b", "create table b (x int)")), Set("two", ("001_c", "create table c (y int)"))]).RunAsync();
        Assert.Equal(["one/002_b", "two/001_c"], applied);
    }

    [Fact]
    public async Task A_changed_applied_script_stops_the_start()
    {
        await using var db = await TestDb.CreateAsync(migrate: false);
        await new Migrator(db.Data, [Set("demo", ("001_first", "create table t (a int)"))]).RunAsync();
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => new Migrator(db.Data, [Set("demo", ("001_first", "create table t (a bigint)"))]).RunAsync());
        Assert.Contains("demo/001_first", e.Message);
        // line endings of the checkout do not count as a change
        Assert.Equal(new Migration("x/001_a", "a\r\nb").Checksum, new Migration("x/001_a", "a\nb").Checksum);
    }

    [Fact]
    public async Task A_failing_script_leaves_nothing_behind()
    {
        await using var db = await TestDb.CreateAsync(migrate: false);
        var set = Set("demo", ("001_ok", "create table ok (a int)"), ("002_broken", "create table half (a int); select no_such_function()"));
        await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => new Migrator(db.Data, [set]).RunAsync());
        Assert.Equal(1L, await db.ScalarAsync<long>("select count(*) from schema_migrations"));
        Assert.False(await db.ScalarAsync<bool>("select exists (select 1 from information_schema.tables where table_schema = current_schema() and table_name = 'half')"));
    }

    [Fact]
    public void A_number_used_twice_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Set("demo", ("001_a", "select 1"), ("001_b", "select 1")));
    }

    [Fact]
    public async Task Two_servers_starting_together_apply_each_script_once()
    {
        await using var db = await TestDb.CreateAsync(migrate: false);
        var set = Set("demo", ("001_first", "create table t (a int)"), ("002_second", "insert into t values (1)"));
        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => new Migrator(db.Data, [set]).RunAsync()));
        Assert.Equal(2, runs.Sum(r => r.Count));
        Assert.Equal(1L, await db.ScalarAsync<long>("select count(*) from t"));
    }

    [Fact]
    public void The_scripts_of_the_base_and_the_start_app_are_named_and_numbered_properly()
    {
        var sets = TestDb.Sets;
        Assert.Equal(["accounts", "web", "app"], sets.Select(s => s.Name));
        Assert.All(sets, s => Assert.NotEmpty(s.Scripts));
        Assert.All(sets.SelectMany(s => s.Scripts), m => Assert.Matches("^[a-z]+/[0-9]{3}_[a-z0-9_]+$", m.Id));
    }
}
