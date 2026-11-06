using CodeLearning.Application.Services;
using CodeLearning.Application.Settings;
using CodeLearning.Core.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CodeLearning.Infrastructure.Services;

public class TokenService(IOptions<JwtSettings> jwtOptions, IConnectionMultiplexer redis) : ITokenService
{
    private readonly JwtSettings _jwtSettings = jwtOptions.Value;
    private readonly IDatabase _db = redis.GetDatabase();

    public string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenId = Guid.NewGuid().ToString();

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(ClaimTypes.GivenName, user.FirstName),
            new Claim(ClaimTypes.Surname, user.LastName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, tokenId)
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    // Keys hold a hash of the token, so reading Redis does not reveal usable refresh tokens:
    //   refresh:{hash}        -> user id   (looks up the owner from the cookie alone)
    //   refresh_user:{userId} -> hash      (finds the user's current token to replace or revoke it)
    public async Task StoreRefreshTokenAsync(string refreshToken, Guid userId, TimeSpan expiration)
    {
        var hash = HashToken(refreshToken);
        var previousHash = await _db.StringGetSetAsync(UserKey(userId), hash);
        await _db.KeyExpireAsync(UserKey(userId), expiration);

        if (!previousHash.IsNullOrEmpty && previousHash != hash)
        {
            await _db.KeyDeleteAsync(TokenKey(previousHash.ToString()));
        }

        await _db.StringSetAsync(TokenKey(hash), userId.ToString(), expiration);
    }

    public async Task<Guid?> ConsumeRefreshTokenAsync(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return null;
        }

        var hash = HashToken(refreshToken);

        // GETDEL is atomic, so two requests racing with the same token cannot both succeed
        var owner = await _db.StringGetDeleteAsync(TokenKey(hash));
        if (owner.IsNullOrEmpty || !Guid.TryParse(owner.ToString(), out var userId))
        {
            return null;
        }

        if (await _db.StringGetAsync(UserKey(userId)) == hash)
        {
            await _db.KeyDeleteAsync(UserKey(userId));
        }

        return userId;
    }

    public async Task RevokeRefreshTokenAsync(Guid userId)
    {
        var hash = await _db.StringGetDeleteAsync(UserKey(userId));
        if (!hash.IsNullOrEmpty)
        {
            await _db.KeyDeleteAsync(TokenKey(hash.ToString()));
        }
    }

    private static string TokenKey(string hash) => $"refresh:{hash}";

    private static string UserKey(Guid userId) => $"refresh_user:{userId}";

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task RevokeAccessTokenAsync(string jti, TimeSpan expiration)
    {
        // Add JWT ID to blacklist with expiration matching token expiration
        var key = $"blacklist:jwt:{jti}";
        await _db.StringSetAsync(key, "revoked", expiration);
    }

    public async Task<bool> IsAccessTokenRevokedAsync(string jti)
    {
        var key = $"blacklist:jwt:{jti}";
        return await _db.KeyExistsAsync(key);
    }
}
