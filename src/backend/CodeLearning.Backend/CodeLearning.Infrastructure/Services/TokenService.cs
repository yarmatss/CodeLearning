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

    public async Task<bool> ValidateRefreshTokenAsync(string refreshToken, Guid userId)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return false;

        // Check if refresh token exists in Redis and belongs to the user
        var key = $"refresh_token:{userId}";
        var storedToken = await _db.StringGetAsync(key);

        return !storedToken.IsNullOrEmpty && storedToken == refreshToken;
    }

    public async Task StoreRefreshTokenAsync(string refreshToken, Guid userId, TimeSpan expiration)
    {
        var key = $"refresh_token:{userId}";
        await _db.StringSetAsync(key, refreshToken, expiration);
    }

    public async Task RevokeRefreshTokenAsync(Guid userId)
    {
        var key = $"refresh_token:{userId}";
        await _db.KeyDeleteAsync(key);
    }

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
