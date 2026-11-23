using Base.Core;
using Base.Data;
using Npgsql;

namespace StartApp.Items;

public sealed record Item(Guid Id, string Title, string Note, bool Done, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>The entries of a person. Every statement names the owner: nobody reads or changes an entry of somebody else.</summary>
public sealed class ItemStore(NpgsqlDataSource db)
{
    private const string Columns = "id, title, note, done, created_at, updated_at";

    private static Item Read(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.Time(4), r.Time(5));

    /// <summary>Newest first, page by page (LIST-01). <paramref name="search"/> looks in the title; <paramref name="openOnly"/> hides what is done.</summary>
    public async Task<Page<Item>> ListAsync(Guid userId, PageRequest page, string? search, bool openOnly, CancellationToken ct = default)
    {
        Guid? after = Cursor.TryDecode(page.Cursor, out var id) ? id : null;
        var rows = await db.QueryAsync(
            $"""
             select {Columns} from items
             where user_id = $1 and ($2::uuid is null or id < $2) and ($3 = '' or position(lower($3) in lower(title)) > 0) and (not $4 or not done)
             order by id desc limit $5
             """, Read, ct, userId, after, search ?? "", openOnly, page.Limit + 1);
        return Page<Item>.From(rows, page.Limit, i => Cursor.Encode(i.Id));
    }

    public Task<Item?> FindAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        db.FirstAsync($"select {Columns} from items where user_id = $1 and id = $2", Read, ct, userId, id);

    public async Task<Item> CreateAsync(Guid userId, string title, string note, CancellationToken ct = default) =>
        (await db.FirstAsync($"insert into items (id, user_id, title, note) values ($1, $2, $3, $4) returning {Columns}", Read, ct, Ids.New(), userId, title, note))!;

    public Task<Item?> UpdateAsync(Guid userId, Guid id, string title, string note, bool done, CancellationToken ct = default) =>
        db.FirstAsync($"update items set title = $3, note = $4, done = $5, updated_at = now() where user_id = $1 and id = $2 returning {Columns}", Read, ct, userId, id, title, note, done);

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        await db.ExecAsync("delete from items where user_id = $1 and id = $2", ct, userId, id) == 1;
}
