using Base.Core;
using Base.Data;
using Npgsql;

namespace Base.Accounts;

public sealed record UserRow(Guid Id, string Name, string PasswordHash, string TotpSecret, bool TotpConfirmed, long TotpLastStep, string[] Roles);

/// <summary>Names of the roles the base itself knows. A role is a plain word in a person's list; an app may add its own (AUTH-11).</summary>
public static class Roles
{
    public const string Admin = "admin";
}

/// <summary>
/// The people of an app. The authenticator secret is stored encrypted (<see cref="SecretBox"/>, Base64 of AES-256-GCM with the id of the person
/// bound in), so a dump of the database shows only Argon2 hashes and encrypted values.
/// </summary>
public sealed class UserStore(NpgsqlDataSource db, SecretBox box)
{
    private const string Columns = "id, name, password_hash, totp_secret, totp_confirmed, totp_last_step, roles";

    private string Seal(Guid id, string secret) => Convert.ToBase64String(box.Encrypt(secret, id));

    /// <summary>The secret in plain text. A value that cannot be read (another key) gives "": the sign-in fails instead of the server.</summary>
    private string Open(Guid id, string stored)
    {
        try { return box.Decrypt(Convert.FromBase64String(stored), id) ?? ""; }
        catch (FormatException) { return ""; }
    }

    private UserRow Read(NpgsqlDataReader r) =>
        new(r.GetGuid(0), r.GetString(1), r.GetString(2), Open(r.GetGuid(0), r.GetString(3)), r.GetBoolean(4), r.GetInt64(5), r.GetFieldValue<string[]>(6));

    public async Task<bool> AnyConfirmedAsync(CancellationToken ct = default) =>
        await db.ScalarAsync<bool>("select exists (select 1 from users where totp_confirmed)", ct);

    public Task<UserRow?> FindByNameAsync(string name, CancellationToken ct = default) =>
        db.FirstAsync($"select {Columns} from users where lower(name) = lower($1)", Read, ct, name);

    public Task<UserRow?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.FirstAsync($"select {Columns} from users where id = $1", Read, ct, id);

    /// <summary>Creates a person with recovery codes, in one transaction. At the first setup, unconfirmed leftovers of earlier tries are removed first.</summary>
    public async Task<UserRow> CreateAsync(string name, string passwordHash, string totpSecret, bool confirmed, IEnumerable<string> recoveryHashes, string[] roles, bool replacePending, CancellationToken ct = default)
    {
        var id = Ids.New();                                    // the id is part of the encrypted value, it must exist before the insert
        await db.InTransactionAsync(async (conn, tx) =>
        {
            if (replacePending) await conn.ExecAsync(tx, "delete from users where not totp_confirmed", ct);
            await conn.ExecAsync(tx, "insert into users (id, name, password_hash, totp_secret, totp_confirmed, roles) values ($1, $2, $3, $4, $5, $6)", ct,
                id, name, passwordHash, Seal(id, totpSecret), confirmed, roles);
            await InsertRecoveryAsync(conn, tx, id, recoveryHashes, ct);
        }, ct);
        return new UserRow(id, name, passwordHash, totpSecret, confirmed, 0, roles);
    }

    private static async Task InsertRecoveryAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, IEnumerable<string> hashes, CancellationToken ct)
    {
        foreach (var h in hashes) await conn.ExecAsync(tx, "insert into recovery_codes (user_id, code_hash) values ($1, $2)", ct, id, h);
    }

    /// <summary>Spends a time step. Works only when it is newer than the last one used: the same code never works twice, also not in two requests at once.</summary>
    public async Task<bool> TryAdvanceTotpStepAsync(Guid id, long step, CancellationToken ct = default) =>
        await db.ExecAsync("update users set totp_last_step = $2 where id = $1 and totp_last_step < $2", ct, id, step) == 1;

    /// <summary>Spends a recovery code: the row is deleted, so it works once.</summary>
    public async Task<bool> ConsumeRecoveryCodeAsync(Guid id, string hash, CancellationToken ct = default) =>
        await db.ExecAsync("delete from recovery_codes where user_id = $1 and code_hash = $2", ct, id, hash) == 1;

    public async Task<int> RecoveryLeftAsync(Guid id, CancellationToken ct = default) =>
        (int)await db.ScalarAsync<long>("select count(*) from recovery_codes where user_id = $1", ct, id);

    public Task ConfirmAsync(Guid id, CancellationToken ct = default) => db.ExecAsync("update users set totp_confirmed = true where id = $1", ct, id);

    public Task SetRolesAsync(Guid id, string[] roles, CancellationToken ct = default) => db.ExecAsync("update users set roles = $2 where id = $1", ct, id, roles);

    /// <param name="keepSession">With <paramref name="endSessions"/>: this session stays, for example the one the password is changed in.</param>
    public Task SetPasswordAsync(Guid id, string passwordHash, bool endSessions, CancellationToken ct = default, Guid? keepSession = null) =>
        db.InTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecAsync(tx, "update users set password_hash = $2 where id = $1", ct, id, passwordHash);
            if (endSessions) await conn.ExecAsync(tx, "delete from sessions where user_id = $1 and ($2::uuid is null or id <> $2)", ct, id, keepSession);
        }, ct);

    /// <summary>A new authenticator secret and new recovery codes; ends every session except <paramref name="keepSession"/>.</summary>
    /// <param name="lastStep">The time step the new secret was confirmed with: the same code does not work again afterwards.</param>
    public Task ReplaceTotpAsync(Guid id, string secret, IEnumerable<string> recoveryHashes, CancellationToken ct = default, Guid? keepSession = null, long lastStep = 0) =>
        db.InTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecAsync(tx, "update users set totp_secret = $2, totp_last_step = $3, totp_confirmed = true where id = $1", ct, id, Seal(id, secret), lastStep);
            await conn.ExecAsync(tx, "delete from recovery_codes where user_id = $1", ct, id);
            await InsertRecoveryAsync(conn, tx, id, recoveryHashes, ct);
            await conn.ExecAsync(tx, "delete from sessions where user_id = $1 and ($2::uuid is null or id <> $2)", ct, id, keepSession);
        }, ct);
}
