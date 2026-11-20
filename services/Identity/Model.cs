using Microsoft.EntityFrameworkCore;

namespace MetroRail.Identity;

public sealed class User
{
    public string Id { get; set; } = "";            // USR-xxxx
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Roles { get; set; } = "commuter";  // comma-separated
    public string ConsentsJson { get; set; } = "{}";
    public string Plan { get; set; } = "free";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OtpChallenge
{
    public string Id { get; set; } = "";
    public string Target { get; set; } = "";  // normalised phone or email
    public string Channel { get; set; } = ""; // sms | email
    public string CodeHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public bool Used { get; set; }
}

public sealed class RefreshToken
{
    public string TokenHash { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Revoked { get; set; }
}

public sealed class IdentityDb(DbContextOptions<IdentityDb> o) : DbContext(o)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OtpChallenge> Challenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasKey(x => x.Id);
        b.Entity<User>().HasIndex(x => x.Phone).IsUnique();
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
        b.Entity<OtpChallenge>().HasKey(x => x.Id);
        b.Entity<RefreshToken>().HasKey(x => x.TokenHash);
        b.Entity<RefreshToken>().HasIndex(x => x.UserId);
    }
}

public sealed record StartIn(string? Phone, string? Email);
public sealed record StartOut(string ChallengeId, string Channel, string SentTo, int ExpiresInS, string? DevCode);
public sealed record VerifyIn(string ChallengeId, string Code);
public sealed record TokensOut(string AccessToken, string RefreshToken, string UserId, bool IsNewUser, string TokenType = "Bearer", int ExpiresInS = 900);
public sealed record RefreshIn(string RefreshToken);
public sealed record Consents(bool TripAlerts = true, bool NearestStation = false, bool UsageCounts = true);
public sealed record MeOut(string Id, string? PhoneMasked, string? Email, string Plan, Consents Consents, string MemberSince);
