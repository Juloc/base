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

    [Fact]
    public async Task The_table_of_the_migrator_has_a_default_name_and_can_be_set()
    {
        await using var db = await TestDb.CreateAsync(migrate: false);
        var set = Set("demo", ("001_first", "create table t (a int)"));
        await new Migrator(db.Data, [set]).RunAsync();
        Assert.Equal(1L, await db.ScalarAsync<long>("select count(*) from schema_migrations"));
        Assert.Equal("schema_migrations", Migrator.DefaultTable);
    }

    [Fact]
    public async Task A_custom_table_works_beside_a_foreign_schema_migrations_that_stays_untouched()
    {
        await using var db = await TestDb.CreateAsync(migrate: false);
        await db.ExecAsync("create table schema_migrations (version bigint primary key, name text)");
        await db.ExecAsync("insert into schema_migrations values (7, 'foreign')");

        var set = Set("demo", ("001_first", "create table t (a int)"), ("002_more", "alter table t add column b int"));
        Assert.Equal(["demo/001_first", "demo/002_more"], await new Migrator(db.Data, [set], "base_migrations").RunAsync());
        Assert.Empty(await new Migrator(db.Data, [set], "base_migrations").RunAsync());
        Assert.Equal(2L, await db.ScalarAsync<long>("select count(*) from base_migrations"));
        // a changed script is still caught in the custom table
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Migrator(db.Data, [Set("demo", ("001_first", "create table t (a bigint)"))], "base_migrations").RunAsync());

        Assert.Equal(1L, await db.ScalarAsync<long>("select count(*) from schema_migrations"));
        Assert.Equal("foreign", await db.ScalarAsync<string>("select name from schema_migrations where version = 7"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a b")]
    [InlineData("a;drop table t")]
    [InlineData("a-b")]
    [InlineData("a.b")]
    [InlineData("\"a\"")]
    [InlineData("1abc")]
    [InlineData("ä")]
    [InlineData("a\n")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void A_table_name_that_is_not_a_plain_identifier_is_refused(string name)
    {
        Assert.Throws<ArgumentException>(() => new Migrator(null!, [], name));
    }

    [Fact]
    public void A_plain_table_name_is_accepted()
    {
        _ = new Migrator(null!, [], "My_table_2");
    }
}
