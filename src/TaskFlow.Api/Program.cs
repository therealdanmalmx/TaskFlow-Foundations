using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using TaskFlow.Api.Features;
using TaskFlow.Api.Infrastructure.Authentication;
using TaskFlow.Api.Infrastructure.Caching;
using TaskFlow.Api.Infrastructure.Persistence;
using TaskFlow.Api.Infrastructure.Routing;
using TaskFlow.Api.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Db"))
        .UseSnakeCaseNamingConvention());

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(allowIntegerValues: false)));

var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = ConfigurationOptions.Parse(redisConnection);
        options.AbortOnConnectFail = false;
        options.BacklogPolicy = BacklogPolicy.FailFast;
        options.ConnectTimeout = 1_000;
        options.SyncTimeout = 1_000;
        options.AsyncTimeout = 1_000;
        return ConnectionMultiplexer.Connect(options);
    });
}

builder.Services.AddSingleton(serviceProvider => new BoardCache(
    serviceProvider.GetService<IConnectionMultiplexer>(),
    serviceProvider.GetRequiredService<ILogger<BoardCache>>()));
builder.Services.AddScoped<WorkspaceAuthorizationService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = TokenService.Issuer,
            ValidAudience = TokenService.Issuer,
            IssuerSigningKey = TokenService.CreateSigningKey(builder.Configuration),
            NameClaimType = JwtRegisteredClaimNames.Name,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (!context.Request.Headers.ContainsKey("Authorization") &&
                    context.Request.Cookies.TryGetValue(TokenService.SessionCookieName, out var token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                var version = context.Principal?.FindFirst(TokenService.TokenVersionClaim)?.Value;
                if (!Guid.TryParse(subject, out var userId) || userId == Guid.Empty ||
                    !int.TryParse(version, out var tokenVersion))
                {
                    context.Fail("The session claims are invalid.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var user = await db.Users
                    .AsNoTracking()
                    .Where(candidate => candidate.Id == userId)
                    .Select(candidate => new { candidate.IsActive, candidate.TokenVersion })
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                if (user is null || !user.IsActive || user.TokenVersion != tokenVersion)
                {
                    context.Fail("The session is no longer valid.");
                }
            },
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<TokenService>();

// Production publishes no application host port: every public request reaches
// the container through Traefik. Development binds only to loopback. Trust one
// immediate proxy hop so authentication limits partition on the real client,
// while discarding any earlier X-Forwarded-For values supplied by that client.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthRateLimitPolicies.Login, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
            }));
    options.AddPolicy(AuthRateLimitPolicies.Registration, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1),
            }));
    options.AddPolicy(AuthRateLimitPolicies.PasswordChange, httpContext =>
    {
        var userId = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "unknown";
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"{userId}:{clientIp}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(5),
            });
    });
});

// Readiness includes the durable database, not the disposable Redis cache.
// Cache loss degrades board reads to Postgres and must not block deployment.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

// Validate the signing key during startup rather than on the first login.
_ = app.Services.GetRequiredService<TokenService>();

if (app.Configuration.GetValue<bool>("RUN_MIGRATIONS"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    app.Logger.LogInformation("RUN_MIGRATIONS=true - applying database migrations...");
    await DatabaseMigrationRunner.ApplyAsync(db);
    app.Logger.LogInformation("Database migrations applied.");

    await SeedData.ApplyAsync(db, app.Configuration, app.Logger);
}
else
{
    app.Logger.LogInformation("RUN_MIGRATIONS is not 'true' - skipping database migrations.");
}

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseStatusCodePages(async statusContext =>
{
    var context = statusContext.HttpContext;
    if (context.Response.StatusCode != StatusCodes.Status404NotFound ||
        context.GetEndpoint() is not null)
    {
        return;
    }

    if (ApiBoundaryPath.Matches(context.Request.Path, context.Request.Method))
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "Endpoint not found." });
        return;
    }

    if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
        !StaticAssetPath.Matches(context.Request.Path))
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(
            Path.Combine(app.Environment.WebRootPath, "index.html"),
            context.RequestAborted);
    }
});

var health = app.MapGroup("/health");
health.MapHealthChecks("/live", new HealthCheckOptions { Predicate = _ => false });
health.MapHealthChecks("/ready");

app.MapFeatureEndpoints();

app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();

public partial class Program;
