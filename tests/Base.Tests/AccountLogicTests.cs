using System.Net;
using System.Text;
using Base.Accounts;
using Base.Core;

namespace Base.Tests;

/// <summary>The parts of the sign-in that need no database: one-time codes, password hash and policy, lockout, encryption, addresses.</summary>
public class AccountLogicTests
{
    // RFC 6238, appendix B (SHA-1, the secret "12345678901234567890"); the six-digit code is the end of the eight-digit one given there
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Totp_matches_the_test_vectors_of_the_standard(long unixTime, string expected)
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal(expected, Totp.Hotp(secret, unixTime / 30));
    }

    [Fact]
    public void A_code_works_in_its_step_and_the_neighbours_and_only_once()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = now.ToUnixTimeSeconds() / 30;
        string At(long s) => Totp.Hotp(Base32.Decode(secret), s);

        Assert.Equal(step, Totp.Check(secret, At(step), 0, now));
        Assert.Equal(step - 1, Totp.Check(secret, At(step - 1), 0, now));
        Assert.Equal(step + 1, Totp.Check(secret, At(step + 1), 0, now));
        Assert.Null(Totp.Check(secret, At(step - 2), 0, now));
        Assert.Null(Totp.Check(secret, At(step), step, now));                  // spent
        Assert.Null(Totp.Check(secret, "12345", 0, now));
        Assert.Null(Totp.Check(secret, "abcdef", 0, now));
        Assert.Null(Totp.Check("", At(step), 0, now));                          // an unreadable secret never matches
    }

    [Fact]
    public void Base32_round_trips()
    {
        var data = new byte[] { 0, 1, 2, 250, 251, 252, 253, 254, 255, 17 };
        Assert.Equal(data, Base32.Decode(Base32.Encode(data)));
        Assert.Equal("MZXW6YTBOI", Base32.Encode(Encoding.ASCII.GetBytes("foobar")));        // RFC 4648
        Assert.Throws<FormatException>(() => Base32.Decode("abc!"));
    }

    [Fact]
    public async Task The_password_is_hashed_with_argon2id_and_a_salt()
    {
        var hasher = new PasswordHasher();
        var a = await hasher.HashAsync("correct horse battery");
        var b = await hasher.HashAsync("correct horse battery");
        Assert.StartsWith($"argon2id$m={PasswordHasher.MemoryKiB},t={PasswordHasher.Iterations},p={PasswordHasher.Parallelism}$", a);
        Assert.NotEqual(a, b);
        Assert.DoesNotContain("correct", a);
        Assert.Equal((true, false), await hasher.VerifyAsync("correct horse battery", a));
        Assert.Equal((false, false), await hasher.VerifyAsync("correct horse batterx", a));
    }

    [Fact]
    public async Task A_weaker_hash_is_accepted_and_marked_for_rehash_and_garbage_is_refused()
    {
        var hasher = new PasswordHasher();
        // a hash with smaller parameters, as an older version of the base could have written it
        using var argon = new Konscious.Security.Cryptography.Argon2id(Encoding.UTF8.GetBytes("old password 123")) { Salt = new byte[16], MemorySize = 1024, Iterations = 1, DegreeOfParallelism = 1 };
        var weak = $"argon2id$m=1024,t=1,p=1${Convert.ToBase64String(new byte[16])}${Convert.ToBase64String(argon.GetBytes(32))}";
        Assert.Equal((true, true), await hasher.VerifyAsync("old password 123", weak));
        Assert.Equal((false, false), await hasher.VerifyAsync("x", "plain text"));
        Assert.Equal((false, false), await hasher.VerifyAsync("x", "argon2id$m=99999999,t=3,p=1$AAAA$AAAA"));
        Assert.Equal((false, false), await hasher.VerifyAsync("x", "argon2id$m=65536,t=3,p=1$not base64$AAAA"));
    }

    [Fact]
    public void The_password_policy_asks_for_length_not_for_symbols()
    {
        Assert.Null(PasswordPolicy.Problem("correct horse battery", "jul"));
        Assert.Equal("password.short", PasswordPolicy.Problem("short", "jul"));
        Assert.Equal("password.short", PasswordPolicy.Problem(null, "jul"));
        Assert.Equal("password.long", PasswordPolicy.Problem(new string('a', 257) + "bcd", "jul"));
        Assert.Equal("password.contains_name", PasswordPolicy.Problem("my name is JUL forever", "jul"));
        Assert.Equal("password.simple", PasswordPolicy.Problem("aaaaaaaaaaaabb", "jul"));
        Assert.Equal("jul.1", PasswordPolicy.NormalizeUserName("  Jul.1 "));
        Assert.Null(PasswordPolicy.NormalizeUserName("jul name"));
        Assert.Null(PasswordPolicy.NormalizeUserName(""));
        Assert.Null(PasswordPolicy.NormalizeUserName(new string('a', 33)));
    }

    [Fact]
    public void Recovery_codes_are_random_and_stored_as_hashes()
    {
        var (plain, hashes) = RecoveryCodes.Generate();
        Assert.Equal(RecoveryCodes.Count, plain.Length);
        Assert.Equal(plain.Length, plain.Distinct().Count());
        Assert.All(plain, p => Assert.Matches("^[A-Z2-7]{4}(-[A-Z2-7]{4}){3}$", p));
        Assert.All(hashes, h => Assert.Matches("^[0-9a-f]{64}$", h));
        Assert.Equal(hashes[0], RecoveryCodes.Hash(plain[0].ToLowerInvariant().Replace("-", " ")));
        Assert.True(RecoveryCodes.LooksLikeRecoveryCode(plain[0]));
        Assert.False(RecoveryCodes.LooksLikeRecoveryCode("123456"));
    }

    [Fact]
    public void Failed_attempts_lock_with_a_growing_wait()
    {
        var clock = new ManualTime(DateTimeOffset.UtcNow);
        var limiter = new LoginLimiter(clock);
        for (var i = 0; i < 4; i++) limiter.Fail("user:jul");
        Assert.Equal(TimeSpan.Zero, limiter.Wait("user:jul"));
        limiter.Fail("user:jul");
        Assert.Equal(TimeSpan.FromSeconds(30), limiter.Wait("user:jul"));
        limiter.Fail("user:jul");
        Assert.Equal(TimeSpan.FromSeconds(60), limiter.Wait("user:jul"));
        for (var i = 0; i < 20; i++) limiter.Fail("user:jul");
        Assert.Equal(TimeSpan.FromMinutes(15), limiter.Wait("user:jul"));          // capped
        Assert.Equal(TimeSpan.Zero, limiter.Wait("user:other"));

        clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(TimeSpan.Zero, limiter.Wait("user:jul"));
        limiter.Succeed("user:jul");
        for (var i = 0; i < 4; i++) limiter.Fail("user:jul");
        Assert.Equal(TimeSpan.Zero, limiter.Wait("user:jul"));                      // the count started again
    }

    [Fact]
    public void An_address_gets_more_attempts_than_an_account_and_old_failures_are_forgotten()
    {
        var clock = new ManualTime(DateTimeOffset.UtcNow);
        var limiter = new LoginLimiter(clock);
        for (var i = 0; i < 9; i++) limiter.Fail("ip:192.0.2.1");
        Assert.Equal(0, limiter.WaitSeconds("ip:192.0.2.1"));
        limiter.Fail("ip:192.0.2.1");
        Assert.Equal(60, limiter.WaitSeconds("ip:192.0.2.1", "user:nobody"));

        for (var i = 0; i < 4; i++) limiter.Fail("user:a");
        clock.Advance(TimeSpan.FromHours(2));
        limiter.Fail("user:a");                                                     // the first after a quiet hour
        Assert.Equal(TimeSpan.Zero, limiter.Wait("user:a"));
    }

    [Fact]
    public void Only_so_many_checks_run_at_once_per_key()
    {
        var limiter = new LoginLimiter();
        Assert.True(limiter.TryBegin("k", 2));
        Assert.True(limiter.TryBegin("k", 2));
        Assert.False(limiter.TryBegin("k", 2));
        limiter.End("k");
        Assert.True(limiter.TryBegin("k", 2));
    }

    [Fact]
    public void A_secret_is_encrypted_bound_to_its_owner_and_never_twice_the_same()
    {
        var box = new SecretBox(TestApp.Key);
        var owner = Guid.NewGuid();
        var a = box.Encrypt("JBSWY3DPEHPK3PXP", owner);
        var b = box.Encrypt("JBSWY3DPEHPK3PXP", owner);
        Assert.NotEqual(a, b);
        Assert.Equal("JBSWY3DPEHPK3PXP", box.Decrypt(a, owner));
        Assert.Null(box.Decrypt(a, Guid.NewGuid()));                                // moved to another row
        a[^1] ^= 1;
        Assert.Null(box.Decrypt(a, owner));                                         // changed
        var other = new SecretBox(Convert.ToBase64String(new byte[32]));
        Assert.Null(other.Decrypt(b, owner));                                       // another key
        Assert.Throws<InvalidOperationException>(() => new SecretBox("too short"));
    }

    [Fact]
    public void The_key_file_is_made_once_and_reused()
    {
        var dir = Path.Combine(Path.GetTempPath(), "base-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = SecretBox.ResolveKey(null, dir);
            var second = SecretBox.ResolveKey("", dir);
            Assert.Equal(first, second);
            Assert.Equal(32, Convert.FromBase64String(first).Length);
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(dir, SecretBox.KeyFileName)));
            Assert.Equal("fixed", SecretBox.ResolveKey("fixed", dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_ipv6_client_is_its_whole_network()
    {
        Assert.Equal("192.0.2.7", ClientAddress.Key(IPAddress.Parse("192.0.2.7")));
        Assert.Equal("192.0.2.7", ClientAddress.Key(IPAddress.Parse("::ffff:192.0.2.7")));
        Assert.Equal("2001:db8:1:2::/64", ClientAddress.Key(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd")));
        Assert.Equal("?", ClientAddress.Key(null));
    }

    [Fact]
    public void The_setup_code_is_random_compared_loosely_and_dead_after_use()
    {
        var a = new SetupCode();
        var b = new SetupCode();
        Assert.NotEqual(a.Value, b.Value);
        Assert.Matches("^[A-Z2-7]{4}-[A-Z2-7]{4}-[A-Z2-7]{4}$", a.Value);
        Assert.True(a.Matches(a.Value.ToLowerInvariant().Replace("-", " ")));
        Assert.False(a.Matches(b.Value));
        Assert.False(a.Matches(null));
        a.MarkUsed();
        Assert.False(a.Matches(a.Value));
    }
}
