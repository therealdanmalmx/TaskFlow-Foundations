using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Api.Domain.Users;
using TaskFlow.Api.Features.Shared;

namespace TaskFlow.Api.Infrastructure.Authentication;

public sealed class TokenService(IConfiguration configuration, IHostEnvironment environment)
{
    public const string Issuer = "taskflow";
    public const string TokenVersionClaim = "token_version";
    public const string SessionCookieName = "taskflow_session";

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);
    private readonly SigningCredentials _credentials =
        new(CreateSigningKey(configuration), SecurityAlgorithms.HmacSha256);
    private readonly JsonWebTokenHandler _handler = new();

    public SessionResponse CreateSession(User user)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Issuer,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, user.Username),
                new Claim(TokenVersionClaim, user.TokenVersion.ToString()),
            ]),
            SigningCredentials = _credentials,
        });

        return new SessionResponse(token, expiresAt, user.Username, UserResponse.From(user));
    }

    public void SetSessionCookie(HttpResponse response, SessionResponse session)
    {
        response.Cookies.Append(SessionCookieName, session.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Path = "/",
            Expires = session.ExpiresAt,
        });
    }

    public void ClearSessionCookie(HttpResponse response)
    {
        response.Cookies.Delete(SessionCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Path = "/",
        });
    }

    public static SymmetricSecurityKey CreateSigningKey(IConfiguration configuration)
    {
        var secret = configuration["Auth:JwtSecret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            throw new InvalidOperationException(
                "Auth:JwtSecret (env Auth__JwtSecret) must be set to at least 32 characters. " +
                "Generate one:  openssl rand -hex 32");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }
}
