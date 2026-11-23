using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace Base.Data;

/// <summary>One numbered SQL script. <see cref="Id"/> is "set/NNN_name" and never changes once the script is released.</summary>
public sealed record Migration(string Id, string Sql)
{
    /// <summary>SHA-256 of the script, independent of the line endings of the checkout.</summary>
    public string Checksum => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Sql.ReplaceLineEndings("\n"))));
}

/// <summary>
/// The scripts of one owner (the accounts of the base, the settings of the base, the app itself), in the order of their numbers.
/// A set is read from the resources of a library: files named <c>NNN_name.sql</c> embedded as <c>migrations/&lt;set&gt;/NNN_name.sql</c>.
/// </summary>
public sealed partial record MigrationSet(string Name, IReadOnlyList<Migration> Scripts)
{
    [GeneratedRegex("^[0-9]{3}_[a-z0-9_]+$")]
    private static partial Regex FileName();

    public static MigrationSet FromAssembly(Assembly assembly, string name)
    {
        var prefix = $"migrations/{name}/";
        var scripts = new List<Migration>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var file = resource.Replace('\\', '/')[prefix.Length..];
            if (!file.EndsWith(".sql", StringComparison.Ordinal) || !FileName().IsMatch(file[..^4]))
                throw new InvalidOperationException($"Migration '{resource}' is not named NNN_name.sql (lower case, digits, underscore).");
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            scripts.Add(new Migration($"{name}/{file[..^4]}", reader.ReadToEnd()));
        }
        return From(name, scripts);
    }

    /// <summary>Checks the numbers: each one once per set. Two scripts with the same number would run in an order nobody chose.</summary>
    public static MigrationSet From(string name, IReadOnlyList<Migration> scripts)
    {
        var twice = scripts.GroupBy(s => s.Id[(name.Length + 1)..][..3]).FirstOrDefault(g => g.Count() > 1);
        if (twice is not null) throw new InvalidOperationException($"Migration number {twice.Key} exists twice in the set '{name}'.");
        return new MigrationSet(name, [.. scripts.OrderBy(s => s.Id, StringComparer.Ordinal)]);
    }
}

/// <summary>
/// Applies the numbered SQL scripts at start, set after set in the order given, each script in its own transaction and exactly once (DATA-02).
/// A script that was applied is never edited: its checksum is stored, and a different one stops the start. Changes come as a new script.
/// An advisory lock keeps two instances from migrating at the same time.
/// </summary>
public sealed class Migrator(NpgsqlDataSource db, IEnumerable<MigrationSet> sets)
{
    private const long LockKey = 0x42415345_4D494752; // "BASEMIGR"

    public async Task<IReadOnlyList<string>> RunAsync(CancellationToken ct = default)
    {
        var applied = new List<string>();
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecAsync(null, "select pg_advisory_lock($1)", ct, LockKey);
        try
        {
            await conn.ExecAsync(null, "create table if not exists schema_migrations (id text primary key, checksum text not null, applied_at timestamptz not null default now())", ct);
            var known = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var cmd = conn.Cmd(null, "select id, checksum from schema_migrations"))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct)) known[r.GetString(0)] = r.GetString(1);

            foreach (var script in sets.SelectMany(s => s.Scripts))
            {
                if (known.TryGetValue(script.Id, out var checksum))
                {
                    if (checksum != script.Checksum)
                        throw new InvalidOperationException($"Migration '{script.Id}' was changed after it was applied. Applied scripts are never edited; add a new numbered script instead.");
                    continue;
                }
                await using var tx = await conn.BeginTransactionAsync(ct);
                await using (var run = new NpgsqlCommand(script.Sql, conn, tx)) await run.ExecuteNonQueryAsync(ct);
                await conn.ExecAsync(tx, "insert into schema_migrations (id, checksum) values ($1, $2)", ct, script.Id, script.Checksum);
                await tx.CommitAsync(ct);
                applied.Add(script.Id);
            }
        }
        finally
        {
            await conn.ExecAsync(null, "select pg_advisory_unlock($1)", CancellationToken.None, LockKey);
        }
        return applied;
    }
}
