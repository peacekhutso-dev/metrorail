using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MetroRail.Common;
using Microsoft.EntityFrameworkCore;

namespace MetroRail.Identity;

public static partial class Endpoints
{
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(5);
    private const int MaxAttempts = 3;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRx();

    public static string NormalisePhone(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.StartsWith('0') && digits.Length == 10) digits = "27" + digits[1..];
        if (!(digits.StartsWith("27") && digits.Length == 11))
            throw new AppException("Enter a South African mobile number", "invalid_phone");
        return "+" + digits;
    }

    private static string? Mask(string? phone) => phone is null ? null : $"{phone[..3]} {phone[3..5]} *** {phone[^4..]}";

    private static MeOut MeOut(User u) => new(u.Id, Mask(u.Phone), u.Email, u.Plan,
        Json.Deserialize<Consents>(u.ConsentsJson), Clock.ToSast(u.CreatedAt).ToString("MMM yyyy"));

    private static async Task<User> Me(HttpContext ctx, IdentityDb db)
    {
        var p = await ctx.RequireUserAsync();
        return await db.Users.FindAsync(p.UserId) ?? throw new NotFoundException("Account not found", "user_not_found");
    }

    public static void MapIdentity(this WebApplication app)
    {
        var g = app.MapGroup("").WithTags("identity");

        g.MapPost("/auth/start", async (StartIn body, IdentityDb db, ICodeSender sender, ServiceSettings settings) =>
        {
            Check.That(string.IsNullOrEmpty(body.Phone) != string.IsNullOrEmpty(body.Email), "phone", "Send either phone or email");
            var channel = body.Phone is { Length: > 0 } ? "sms" : "email";
            string target;
            if (channel == "sms") target = NormalisePhone(body.Phone!);
            else
            {
                Check.That(EmailRx().IsMatch(body.Email!), "email", "Enter a valid email");
                target = body.Email!.ToLowerInvariant();
            }
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");
            var ch = new OtpChallenge
            {
                Id = Guid.NewGuid().ToString("N"), Target = target, Channel = channel, CodeHash = Signer.Hash(code),
                ExpiresAt = Clock.RealUtcNow + CodeTtl,
            };
            db.Challenges.Add(ch);
            await db.SaveChangesAsync();
            await sender.SendAsync(channel, target, code);
            return new StartOut(ch.Id, channel, channel == "sms" ? Mask(target)! : target, (int)CodeTtl.TotalSeconds,
                settings.IsProduction ? null : code);
        });

        g.MapPost("/auth/verify", async (VerifyIn body, IdentityDb db, Signer signer, IEventBus bus) =>
        {
            Check.Matches(body.Code, @"^\d{6}$", "code", "Enter the 6-digit code");
            var ch = await db.Challenges.FindAsync(body.ChallengeId);
            if (ch is null || ch.Used || ch.ExpiresAt < Clock.RealUtcNow)
                throw new UnauthorizedException("This code has expired. Ask for a new one.", "code_expired");
            if (ch.Attempts >= MaxAttempts)
                throw new UnauthorizedException("Too many tries. Ask for a new code.", "too_many_attempts");
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(ch.CodeHash), Convert.FromHexString(Signer.Hash(body.Code))))
            {
                ch.Attempts++;
                await db.SaveChangesAsync();
                throw new UnauthorizedException($"That code didn't match. {MaxAttempts - ch.Attempts} tries left.", "code_mismatch");
            }
            ch.Used = true;
            var user = ch.Channel == "sms"
                ? await db.Users.FirstOrDefaultAsync(u => u.Phone == ch.Target)
                : await db.Users.FirstOrDefaultAsync(u => u.Email == ch.Target);
            var isNew = user is null;
            if (user is null)
            {
                user = new User
                {
                    Id = "USR-" + Guid.NewGuid().ToString("N")[..12], Phone = ch.Channel == "sms" ? ch.Target : null,
                    Email = ch.Channel == "email" ? ch.Target : null, ConsentsJson = Json.Serialize(new Consents()),
                    CreatedAt = Clock.RealUtcNow,
                };
                db.Users.Add(user);
            }
            var (refresh, hash) = Signer.NewRefreshToken();
            db.RefreshTokens.Add(new RefreshToken { TokenHash = hash, UserId = user.Id, ExpiresAt = Clock.RealUtcNow + Signer.RefreshTtl });
            await db.SaveChangesAsync();
            if (isNew) await bus.PublishAsync("identity.user.created", new { user_id = user.Id });
            return new TokensOut(signer.AccessToken(user.Id, user.Roles.Split(',')), refresh, user.Id, isNew);
        });

        g.MapPost("/auth/refresh", async (RefreshIn body, IdentityDb db, Signer signer) =>
        {
            var rt = await db.RefreshTokens.FindAsync(Signer.Hash(body.RefreshToken));
            var user = rt is null ? null : await db.Users.FindAsync(rt.UserId);
            if (rt is null || rt.Revoked || rt.ExpiresAt < Clock.RealUtcNow || user is null)
                throw new UnauthorizedException("Please log in again", "refresh_invalid");
            rt.Revoked = true; // rotate
            var (next, hash) = Signer.NewRefreshToken();
            db.RefreshTokens.Add(new RefreshToken { TokenHash = hash, UserId = user.Id, ExpiresAt = Clock.RealUtcNow + Signer.RefreshTtl });
            await db.SaveChangesAsync();
            return new TokensOut(signer.AccessToken(user.Id, user.Roles.Split(',')), next, user.Id, false);
        });

        g.MapPost("/auth/logout", async (RefreshIn body, IdentityDb db) =>
        {
            if (await db.RefreshTokens.FindAsync(Signer.Hash(body.RefreshToken)) is { } rt)
            {
                rt.Revoked = true;
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });

        app.MapGet("/.well-known/jwks.json", (Signer signer) => new { keys = new[] { signer.Jwk } }).WithTags("ops");

        g.MapGet("/me", async (HttpContext ctx, IdentityDb db) => MeOut(await Me(ctx, db)));

        g.MapPut("/me/consents", async (Consents body, HttpContext ctx, IdentityDb db, IEventBus bus) =>
        {
            var u = await Me(ctx, db);
            u.ConsentsJson = Json.Serialize(body);
            await db.SaveChangesAsync();
            await bus.PublishAsync("identity.user.consents_changed", new { user_id = u.Id, body.TripAlerts, body.NearestStation, body.UsageCounts });
            return MeOut(u);
        });

        // POPIA s23: a copy of the personal information we hold. Other services add their part later.
        g.MapGet("/me/export", async (HttpContext ctx, IdentityDb db) =>
        {
            var u = await Me(ctx, db);
            return new { account = new { id = u.Id, phone = u.Phone, email = u.Email, plan = u.Plan,
                consents = Json.Deserialize<Consents>(u.ConsentsJson), created_at = u.CreatedAt } };
        });

        // POPIA s24: delete the account. Other services purge on identity.user.deleted.
        g.MapDelete("/me", async (HttpContext ctx, IdentityDb db, IEventBus bus) =>
        {
            var u = await Me(ctx, db);
            db.RefreshTokens.RemoveRange(db.RefreshTokens.Where(r => r.UserId == u.Id));
            db.Users.Remove(u);
            await db.SaveChangesAsync();
            await bus.PublishAsync("identity.user.deleted", new { user_id = u.Id });
            return Results.NoContent();
        });
    }
}
