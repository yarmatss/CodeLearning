using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Services;
using CodeLearning.Application.Settings;
using CodeLearning.Core.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CodeLearning.Infrastructure.Services;

// Refresh tokens are stored as hashes, so reading Redis does not reveal usable tokens:
//   refresh:{hash}         -> "{userId}|{sessionId}"  (one per session, expires with the token)
//   refresh_user:{userId}  -> set of hashes            (every live session of the user, for revoke-all)
// Each multi-key change runs as one Lua script, so concurrent refreshes and logouts cannot interleave.
public class TokenService(IOptions<JwtSettings> jwtOptions, IConnectionMultiplexer redis) : ITokenService
{
    public const string SessionIdClaim = JwtRegisteredClaimNames.Sid;

    private const string TokenKeyPrefix = "refresh:";
    private const string UserKeyPrefix = "refresh_user:";

    // A user key left over from the single-session format is a string; it is replaced by a set
    private const string EnsureUserSetLua = """
        local function ensure_set(key)
          if redis.call('TYPE', key).ok ~= 'set' then redis.call('DEL', key) end
        end
        """;

    // KEYS[1] = refresh:{hash}, KEYS[2] = refresh_user:{userId}
    // ARGV[1] = session value, ARGV[2] = ttl seconds, ARGV[3] = hash
    private const string StoreScript = EnsureUserSetLua + """

        ensure_set(KEYS[2])
        for _, h in ipairs(redis.call('SMEMBERS', KEYS[2])) do
          if redis.call('EXISTS', 'refresh:' .. h) == 0 then redis.call('SREM', KEYS[2], h) end
        end
        redis.call('SET', KEYS[1], ARGV[1], 'EX', ARGV[2])
        redis.call('SADD', KEYS[2], ARGV[3])
        redis.call('EXPIRE', KEYS[2], ARGV[2])
        return 1
        """;

    // KEYS[1] = refresh:{oldHash}; ARGV[1] = old hash, ARGV[2] = new hash, ARGV[3] = ttl seconds
    // Get-and-delete of the old token and the write of the new one happen together, so a replayed
    // or concurrently used token finds nothing and fails
    private const string RotateScript = EnsureUserSetLua + """

        local v = redis.call('GET', KEYS[1])
        if not v then return false end
        redis.call('DEL', KEYS[1])
        local sep = string.find(v, '|', 1, true)
        if not sep then return false end
        local userKey = 'refresh_user:' .. string.sub(v, 1, sep - 1)
        ensure_set(userKey)
        redis.call('SREM', userKey, ARGV[1])
        redis.call('SET', 'refresh:' .. ARGV[2], v, 'EX', ARGV[3])
        redis.call('SADD', userKey, ARGV[2])
        redis.call('EXPIRE', userKey, ARGV[3])
        return v
        """;

    // KEYS[1] = refresh:{hash}; ARGV[1] = hash
    private const string RevokeTokenScript = """
        local v = redis.call('GET', KEYS[1])
        if not v then return false end
        redis.call('DEL', KEYS[1])
        local sep = string.find(v, '|', 1, true)
        if sep then
          local userKey = 'refresh_user:' .. string.sub(v, 1, sep - 1)
          if redis.call('TYPE', userKey).ok == 'set' then redis.call('SREM', userKey, ARGV[1]) end
        end
        return v
        """;

    // KEYS[1] = refresh_user:{userId}; ARGV[1] = session value to remove
    private const string RevokeSessionScript = """
        if redis.call('TYPE', KEYS[1]).ok ~= 'set' then return 0 end
        local removed = 0
        for _, h in ipairs(redis.call('SMEMBERS', KEYS[1])) do
          local key = 'refresh:' .. h
          local v = redis.call('GET', key)
          if not v then
            redis.call('SREM', KEYS[1], h)
          elseif v == ARGV[1] then
            redis.call('DEL', key)
            redis.call('SREM', KEYS[1], h)
            removed = removed + 1
          end
        end
        return removed
        """;

    // KEYS[1] = refresh_user:{userId}
    private const string RevokeAllScript = """
        if redis.call('TYPE', KEYS[1]).ok == 'set' then
          for _, h in ipairs(redis.call('SMEMBERS', KEYS[1])) do
            redis.call('DEL', 'refresh:' .. h)
          end
        end
        redis.call('DEL', KEYS[1])
        return 1
        """;

    private readonly JwtSettings _jwtSettings = jwtOptions.Value;

    // Resolved per call rather than in the constructor: GetDatabase is cheap and never blocks, and the
    // multiplexer reconnects on its own after Redis comes back
    private IDatabase Db => redis.GetDatabase();

    public IssuedAccessToken GenerateAccessToken(User user, Guid sessionId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenId = Guid.NewGuid().ToString();
        var expires = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(ClaimTypes.GivenName, user.FirstName),
            new Claim(ClaimTypes.Surname, user.LastName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, tokenId),
            new Claim(SessionIdClaim, sessionId.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials
        );

        // ValidTo is the exp claim as written (whole seconds), so the cookie and the response match it
        return new IssuedAccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            tokenId,
            new DateTimeOffset(token.ValidTo, TimeSpan.Zero));
    }

    public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public async Task StoreRefreshTokenAsync(string refreshToken, RefreshSession session, TimeSpan expiration)
    {
        var hash = HashToken(refreshToken);

        await Db.ScriptEvaluateAsync(
            StoreScript,
            [TokenKey(hash), UserKey(session.UserId)],
            [FormatSession(session), TtlSeconds(expiration), hash]);
    }

    public async Task<RefreshSession?> RotateRefreshTokenAsync(string currentRefreshToken, string newRefreshToken, TimeSpan expiration)
    {
        if (string.IsNullOrEmpty(currentRefreshToken))
        {
            return null;
        }

        var oldHash = HashToken(currentRefreshToken);

        var result = await Db.ScriptEvaluateAsync(
            RotateScript,
            [TokenKey(oldHash)],
            [oldHash, HashToken(newRefreshToken), TtlSeconds(expiration)]);

        return ParseSession(result);
    }

    public async Task<RefreshSession?> RevokeRefreshTokenAsync(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return null;
        }

        var hash = HashToken(refreshToken);
        var result = await Db.ScriptEvaluateAsync(RevokeTokenScript, [TokenKey(hash)], [hash]);

        return ParseSession(result);
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId) =>
        await Db.ScriptEvaluateAsync(
            RevokeSessionScript,
            [UserKey(userId)],
            [FormatSession(new RefreshSession(userId, sessionId))]);

    public async Task RevokeAllRefreshTokensAsync(Guid userId) =>
        await Db.ScriptEvaluateAsync(RevokeAllScript, [UserKey(userId)]);

    public async Task RevokeAccessTokenAsync(string jti, TimeSpan expiration)
    {
        // Kept until the token would have expired anyway
        await Db.StringSetAsync(BlacklistKey(jti), "revoked", expiration);
    }

    public async Task<bool> IsAccessTokenRevokedAsync(string jti) =>
        await Db.KeyExistsAsync(BlacklistKey(jti));

    private static RefreshSession? ParseSession(RedisResult result)
    {
        if (result.IsNull)
        {
            return null;
        }

        var parts = ((string?)result)?.Split('|');
        return parts is [var user, var session]
               && Guid.TryParse(user, out var userId)
               && Guid.TryParse(session, out var sessionId)
            ? new RefreshSession(userId, sessionId)
            : null;
    }

    private static string FormatSession(RefreshSession session) => $"{session.UserId}|{session.SessionId}";

    private static string TtlSeconds(TimeSpan expiration) =>
        Math.Max(1, (long)Math.Ceiling(expiration.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    private static RedisKey TokenKey(string hash) => TokenKeyPrefix + hash;

    private static RedisKey UserKey(Guid userId) => UserKeyPrefix + userId;

    private static RedisKey BlacklistKey(string jti) => $"blacklist:jwt:{jti}";

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
