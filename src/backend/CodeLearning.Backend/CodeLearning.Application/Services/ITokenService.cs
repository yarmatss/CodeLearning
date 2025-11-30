using CodeLearning.Core.Entities;

namespace CodeLearning.Application.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    bool ValidateRefreshToken(string refreshToken);
    void RevokeRefreshToken(string refreshToken);
}
