using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DistrictSync.Api.Auth;
using DistrictSync.Api.Endpoints;
using DistrictSync.Api.Hubs;
using DistrictSync.Api.Middleware;
using DistrictSync.Application;
using DistrictSync.Infrastructure;
using DistrictSync.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddApplication();
builder.AddInfrastructure();

// ----- Auth: JWT bearer (dev tokens locally; swap the authority/keys for a real IdP in production) -----
var authOptions = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.Section));
builder.Services.AddSingleton<DevTokenIssuer>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = authOptions.Issuer,
            ValidAudience = authOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey)),
            NameClaimType = ClaimNames.Subject,
            RoleClaimType = ClaimNames.Role,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        options.Events = new JwtBearerEvents
        {
            // SignalR's browser client cannot set headers on the WebSocket handshake; it sends the token as a query parameter.
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"].FirstOrDefault();
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("admin", p => p.RequireRole("admin"))
    .AddPolicy("approver", p => p.RequireRole("approver", "admin"))
    .AddPolicy("analyst", p => p.RequireRole("analyst", "admin"));

// ----- Per-tenant rate limiting on the ingestion surface -----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("ingestion", context =>
    {
        var key = context.User.FindFirst(ClaimNames.TenantId)?.Value
            ?? context.Request.RouteValues["sourceSystemId"]?.ToString()
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 600,
            TokensPerPeriod = 600,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
});

// ----- SignalR with Redis backplane so every API instance can reach every tenant connection -----
var signalR = builder.Services.AddSignalR(o => o.EnableDetailedErrors = builder.Environment.IsDevelopment());
var redisConnectionString = builder.Configuration.GetConnectionString(DistrictSync.Infrastructure.DependencyInjection.RedisConnectionName);
if (!string.IsNullOrEmpty(redisConnectionString))
{
    signalR.AddStackExchangeRedis(redisConnectionString, o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("districtsync"));
}

builder.Services.AddLiveUpdateBroadcasters();
builder.Services.AddMessageConsumer("api.dashboard", LiveUpdateRegistration.RoutingKeys);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = long.MaxValue);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapOpenApi();
app.MapScalarApiReference(o => o.Title = "DistrictSync API");

var api = app.MapGroup("/api/v1");
api.MapAuth();
api.MapTenants();
api.MapSourceSystems();
api.MapIngestion();
api.MapReconciliation();
api.MapRules();
api.MapCases();
api.MapDashboard();
api.MapOperations();

app.MapHub<OperationsHub>("/hubs/operations");
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

await app.RunAsync();
