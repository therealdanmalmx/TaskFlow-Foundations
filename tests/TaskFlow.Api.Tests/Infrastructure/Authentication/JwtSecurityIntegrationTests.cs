using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Persistence;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Infrastructure.Authentication;

[Collection(TaskFlowApiCollection.Name)]
public sealed class JwtSecurityIntegrationTests(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output) : IntegrationTestBase(environment, output)
{
    [Theory]
    [InlineData("malformed")]
    [InlineData("unsigned")]
    [InlineData("wrong-signature")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("expired")]
    [InlineData("not-yet-valid")]
    [InlineData("missing-subject")]
    [InlineData("invalid-subject")]
    [InlineData("integer-subject")]
    [InlineData("empty-uuid-subject")]
    [InlineData("missing-version")]
    [InlineData("invalid-version")]
    [InlineData("stale-version")]
    [InlineData("unknown-user")]
    public async Task Invalid_cryptographic_registered_and_session_claims_are_rejected(string scenario)
    {
        var userId = await GetAdminUserIdAsync();
        var token = CreateScenarioToken(scenario, userId);

        using var response = await Client.SendAuthenticatedAsync(
            token,
            HttpMethod.Get,
            "/api/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_cannot_reuse_a_token_or_log_in()
    {
        var token = await Application.CreateAdminTokenAsync();
        using (var scope = Application.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync();
            user.IsActive = false;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        using var profile = await Client.SendAuthenticatedAsync(
            token,
            HttpMethod.Get,
            "/api/users/me");
        using var login = await Client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                username = TaskFlowApplicationFactory.AdminUsername,
                password = TaskFlowApplicationFactory.AdminPassword,
            });

        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Session_cookie_authenticates_only_when_authorization_header_is_absent()
    {
        var token = await Application.CreateAdminTokenAsync();
        using (var cookieOnly = new HttpRequestMessage(HttpMethod.Get, "/api/users/me"))
        {
            cookieOnly.Headers.TryAddWithoutValidation(
                "Cookie",
                $"{TokenService.SessionCookieName}={token}");
            using var accepted = await Client.SendAsync(cookieOnly);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        using var conflicting = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        conflicting.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        conflicting.Headers.TryAddWithoutValidation(
            "Cookie",
            $"{TokenService.SessionCookieName}={token}");
        using var rejected = await Client.SendAsync(conflicting);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [Fact]
    public async Task Production_session_cookie_has_strict_security_attributes()
    {
        using var productionFactory = Application.WithWebHostBuilder(
            builder => builder.UseEnvironment("Production"));
        using var client = productionFactory.CreateIntegrationClient();
        using var response = await client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                username = TaskFlowApplicationFactory.AdminUsername,
                password = TaskFlowApplicationFactory.AdminPassword,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sessionCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                $"{TokenService.SessionCookieName}=",
                StringComparison.Ordinal));
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", sessionCookie, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> GetAdminUserIdAsync()
    {
        using var scope = Application.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Select(user => user.Id).SingleAsync();
    }

    private static string CreateScenarioToken(string scenario, Guid userId)
    {
        if (scenario == "malformed")
        {
            return "not-a-jwt";
        }

        var subject = scenario switch
        {
            "missing-subject" => null,
            "invalid-subject" => "not-a-uuid",
            "integer-subject" => "1",
            "empty-uuid-subject" => Guid.Empty.ToString(),
            "unknown-user" => Guid.NewGuid().ToString(),
            _ => userId.ToString(),
        };
        var version = scenario switch
        {
            "missing-version" => null,
            "invalid-version" => "not-an-integer",
            "stale-version" => "1",
            _ => "0",
        };

        List<Claim> claims = [new(JwtRegisteredClaimNames.Name, "admin")];
        if (subject is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subject));
        }

        if (version is not null)
        {
            claims.Add(new Claim(TokenService.TokenVersionClaim, version));
        }

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = scenario == "wrong-issuer" ? "another-issuer" : TokenService.Issuer,
            Audience = scenario == "wrong-audience" ? "another-audience" : TokenService.Issuer,
            NotBefore = scenario switch
            {
                "not-yet-valid" => now.AddMinutes(2),
                "expired" => now.AddMinutes(-10),
                _ => now.AddMinutes(-1),
            },
            Expires = scenario == "expired" ? now.AddMinutes(-2) : now.AddMinutes(10),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = scenario == "unsigned"
                ? null
                : new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                        scenario == "wrong-signature"
                            ? "wrong-test-secret-0123456789abcdef-0123456789abcdef"
                            : TaskFlowApplicationFactory.JwtSecret)),
                    SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
