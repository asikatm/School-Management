using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SchoolManagement.Domain.Identity;
using SchoolManagement.Infrastructure.Persistence;

namespace SchoolManagement.Api.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "school-management";
    public string Audience { get; set; } = "school-management-clients";

    /// <summary>HMAC signing key, at least 32 bytes. Inject from a secret store in production, never commit it.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
}

public record TokenPair(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);

/// <summary>Short-lived JWT access tokens with rotating, hashed refresh tokens (spec 5.3 Identity).</summary>
public sealed class TokenService(AppDbContext db, IOptions<JwtOptions> options, TimeProvider clock)
{
    public async Task<TokenPair> IssueAsync(User user, string? ip, CancellationToken ct)
    {
        var o = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;

        var assignments = await db.UserRoleAssignments.AsNoTracking()
            .Where(a => a.UserId == user.Id)
            .Select(a => new { a.CampusId, Permissions = a.Role!.Permissions.Select(p => p.Permission).ToList() })
            .ToListAsync(ct);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new("full_name", user.FullName),
            new("user_type", user.UserType.ToString()),
            new(AppClaims.Language, user.PreferredLanguage),
            new(AppClaims.SecurityStamp, user.SecurityStamp.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (assignments.Any(a => a.CampusId is null))
            claims.Add(new Claim(AppClaims.AllCampuses, "true"));
        foreach (var campusId in assignments.Where(a => a.CampusId is not null).Select(a => a.CampusId!.Value).Distinct())
            claims.Add(new Claim(AppClaims.Campus, campusId.ToString()));

        var grants = assignments
            .SelectMany(a => a.Permissions.Select(p => $"{p}@{(a.CampusId?.ToString() ?? AppClaims.AnyCampus)}"))
            .Distinct();
        claims.AddRange(grants.Select(g => new Claim(AppClaims.Permission, g)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey));
        var expires = now.AddMinutes(o.AccessTokenMinutes);
        var jwt = new JwtSecurityToken(o.Issuer, o.Audience, claims, now, expires, new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var access = new JwtSecurityTokenHandler().WriteToken(jwt);

        var (refresh, refreshEntity) = CreateRefreshToken(user.Id, ip, now);
        db.RefreshTokens.Add(refreshEntity);
        await db.SaveChangesAsync(ct);

        return new TokenPair(access, expires, refresh, refreshEntity.ExpiresAtUtc);
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair. Presenting an already-rotated token is treated as theft and
    /// revokes every refresh token the user holds.
    /// </summary>
    public async Task<(User User, TokenPair Tokens)?> RefreshAsync(string refreshToken, string? ip, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return null;

        if (stored.RevokedAtUtc is not null)
        {
            await RevokeAllAsync(stored.UserId, ct);
            return null;
        }
        if (!stored.IsActive(now)) return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId && u.IsActive, ct);
        if (user is null || (user.LockoutEndUtc is { } lockout && lockout > now)) return null;

        stored.RevokedAtUtc = now;
        var pair = await IssueAsync(user, ip, ct);
        stored.ReplacedById = (await db.RefreshTokens.AsNoTracking().FirstAsync(t => t.TokenHash == Hash(pair.RefreshToken), ct)).Id;
        await db.SaveChangesAsync(ct);
        return (user, pair);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is { RevokedAtUtc: null })
        {
            stored.RevokedAtUtc = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);
    }

    private (string Token, RefreshToken Entity) CreateRefreshToken(Guid userId, string? ip, DateTime now)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(token),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(options.Value.RefreshTokenDays),
            CreatedByIp = ip
        });
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
