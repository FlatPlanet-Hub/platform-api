using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using FlatPlanet.Platform.API.Middleware;
using FlatPlanet.Platform.Infrastructure.Configuration;
using FlatPlanet.Platform.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Infrastructure (services + configuration)
builder.Services.AddInfrastructure(builder.Configuration);

// JWT authentication
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt settings are not configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var msg = context.Exception.Message.Replace(Environment.NewLine, " ");
                Console.WriteLine($"[JWT] Auth failed ({context.Exception.GetType().Name}): {msg}");
                Console.WriteLine($"[JWT] Issuer={jwtSettings.Issuer} Audience={jwtSettings.Audience} KeyLen={jwtSettings.SecretKey?.Length}");
                return Task.CompletedTask;
            },
            OnTokenValidated = _ =>
            {
                Console.WriteLine("[JWT] Token validated OK");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Rate limiting
//
// Token-type-differentiated ceilings (replaces the old per-project override
// hardcode — see git history for the Wayfinder-specific dictionary this
// superseded). The architecture problem that override was patching around is
// real: an app that authenticates with a single service/api token on behalf
// of N concurrent end-users collapses ALL of those users onto one rate-limit
// bucket at the per-project and per-(project,user) layers. A human `user_token`
// never has that problem — one token, one person. So instead of hardcoding a
// higher ceiling per projectId, we read the JWT's `token_type` claim and give
// service/api tokens a higher ceiling everywhere, automatically, for every
// FlatPlanet app — zero per-app configuration and zero client changes.
//
// GetRateLimitTokenType() below reads `token_type` and normalizes it to either
// "service" (service_token / api_token) or "user" (user_token, or anything
// else — missing claim, unrecognized value, etc). Unrecognized/missing always
// falls back to "user", i.e. the strict limits — fail closed, not open. A
// legitimate token minted before this claim existed just gets rate-limited
// more strictly, which is a safe degradation, not a data-loss risk.
//
// NOTE on ASP.NET Core rate limit middleware behavior:
//   - The RateLimitingMiddleware supports ONLY ONE named policy per endpoint.
//   - [EnableRateLimiting("X")] on an endpoint REPLACES any policy attached via
//     RequireRateLimiting("Y") at MapControllers level.
//   - To run TWO checks against a single request, use GlobalLimiter + a named policy.
//     GlobalLimiter runs first and is independent of endpoint policies, so it stacks.
//   - GlobalLimiter can chain multiple partitioned limiters via CreateChained — every
//     chained limiter must permit the request, so this stacks additional ceilings on
//     top of the per-user global cap.
//
// Strategy (three layers, all must pass):
//   1. GlobalLimiter[user]     — 1000/min per user (or IP), both token types — runaway single client
//   2. GlobalLimiter[project]  —  500/min (user_token) or 1500/min (service/api_token) per project
//   3. "project-query" policy  —   40/min (user_token) or  150/min (service/api_token) per (project,user)
//
// Rationale for the service/api ceilings — bounded by the DB connection pool, not by
// Wayfinder's traffic shape:
//   - The Npgsql pool is sized for Supabase's PgBouncer transaction-mode limits (see
//     `SupabaseSettings.cs`, Maximum Pool Size=20 — do NOT bump the pool to fit a higher
//     rate limit; it was deliberately tuned for PgBouncer and raising it risks exhausting
//     Supabase's connection budget across all FlatPlanet apps sharing that pooler).
//   - 20 connections × 60s ÷ ~200ms average query ≈ 6000/min theoretical max throughput;
//     realistic sustainable throughput under bursts (queueing, slower queries, concurrent
//     apps) is more like 1500-2000/min. 1500/min per project sits comfortably below that,
//     so the rate limiter throttles clients before the pool itself saturates and starts
//     queuing/timing out connection acquisition.
//   - 150/min per (project, user)-equivalent slice for a service token covers a single
//     misbehaving downstream user's traffic (routed through the shared token) without
//     starving the rest of the app's users, mirroring the 40/min headroom ratio the
//     user_token tier already uses relative to its own project cap (500/min).
//   - These ceilings apply today to `api_token` — the only token type JwtService.cs
//     actually stamps (`token_type: "api_token"`, see JwtService.cs). `service_token` is
//     reserved for future work; both hit this higher tier once it exists.
//   - Keep this pairing (1500/150) and the pool size (`SupabaseSettings.cs`, currently 20)
//     in sync — if the pool size ever changes, re-derive these ceilings from the formula
//     above rather than drifting them independently.
static string GetRateLimitTokenType(HttpContext httpContext)
{
    var tokenType = httpContext.User.FindFirst("token_type")?.Value;
    return tokenType switch
    {
        "service_token" or "api_token" => "service",
        _ => "user" // includes "user_token", missing claim, and any unrecognized value — fail closed
    };
}

builder.Services.AddRateLimiter(options =>
{
    // Global limiter — chained: user-cap AND project-cap must both permit.
    // Runs on every request, in addition to any named endpoint policy.
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        // Layer 1: per-user (or IP for anonymous) — 1000/min. Guards against a
        // runaway single client hammering ANY endpoint.
        PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            var userId = httpContext.User.FindFirst("sub")?.Value
                         ?? httpContext.Connection.RemoteIpAddress?.ToString()
                         ?? "anonymous";
            return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1000,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
        }),
        // Layer 2: per-project — 500/min (user_token) or 1500/min (service/api_token).
        // 1500/min is sized to stay below the Npgsql pool capacity (Maximum Pool Size=20
        // in SupabaseSettings.cs) — see the rationale block above this file's rate-limiting
        // section for the formula. Only fires on routes that carry a {projectId} template
        // value. Endpoints without one get a no-op partition, so this layer is inert for
        // auth/admin endpoints and only guards the project-scoped surface (/api/projects/{id}/*).
        PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            var projectId = httpContext.GetRouteValue("projectId")?.ToString();
            if (string.IsNullOrEmpty(projectId))
            {
                return RateLimitPartition.GetNoLimiter<string>("no-project");
            }
            var perProjectLimit = GetRateLimitTokenType(httpContext) == "service" ? 1500 : 500;
            return RateLimitPartition.GetFixedWindowLimiter(projectId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = perProjectLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
        })
    );

    // Layer 3: named policy for query endpoints only.
    // Applied via [EnableRateLimiting("project-query")] on QueryController actions.
    // Partitioned by (projectId, userId) so one user's runaway retries burn only
    // their own slice — other users on the same project stay responsive.
    options.AddPolicy("project-query", httpContext =>
    {
        var projectId = httpContext.GetRouteValue("projectId")?.ToString() ?? "unknown";
        var userId    = httpContext.User.FindFirst("sub")?.Value
                        ?? httpContext.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous";
        var key = $"{projectId}::{userId}";
        // 40/min per (project, user) covers realistic interactive load for a human
        // user_token — ApprovalFlow's batched `get-app-data` fires ~21 queries per
        // login, so a user can log in and browse without tripping. A tight retry
        // loop hits the ceiling in ~1 second and self-throttles.
        // service_token/api_token get 150/min: that identity represents an app's
        // whole user base sharing one token, so its "one user" slice needs to be
        // sized like a project, not like a person — bounded, like Layer 2, by the
        // Npgsql pool capacity (see SupabaseSettings.cs and the rationale block above).
        var perUserLimit = GetRateLimitTokenType(httpContext) == "service" ? 150 : 40;
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perUserLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Return our standard JSON envelope instead of an empty 429 body so the frontend
    // can detect rate limiting consistently with other API errors.
    // NOTE: must explicitly set StatusCode here — when OnRejected is provided,
    // RejectionStatusCode is NOT applied automatically.
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        // Retry-After header tells well-behaved HTTP clients (Claude Code's client,
        // most server-to-server libraries, browsers) to automatically back off for N
        // seconds before retrying. No client-side coordination needed — RFC 7231 §7.1.3.
        context.HttpContext.Response.Headers.RetryAfter = "60";
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"success\":false,\"message\":\"Too many requests for this project. Please retry after 60 seconds.\"}",
            token);
    };
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("default", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length > 0)
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        else
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

// OpenAPI (built-in .NET 10)
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new()
        {
            Title = "FlatPlanet Platform API",
            Version = "v1",
            Description = "Backend platform API for FlatPlanet Hub — Supabase, GitHub, and Claude Code integration."
        };
        return Task.CompletedTask;
    });
});

// Health checks
builder.Services.AddHealthChecks();

// Logging — mask sensitive headers
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestMethod
        | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestPath
        | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponseStatusCode
        | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.Duration;
});

var app = builder.Build();

app.UseHttpLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors("default");
app.UseMiddleware<GlobalExceptionMiddleware>();
// UseRouting MUST be called before UseRateLimiter so the rate limiter
// can read endpoint metadata ([EnableRateLimiting] attributes, RequireRateLimiting).
// Without this, both the per-user and project-query policies are silently inactive
// — the middleware runs but has no endpoint to inspect.
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ProjectScopeMiddleware>();

// Note: per-user limit is applied via GlobalLimiter in AddRateLimiter above —
// NOT via RequireRateLimiting here, because that would conflict with the
// [EnableRateLimiting] attribute on QueryController and prevent project-query
// from firing. GlobalLimiter runs alongside named policies; RequireRateLimiting does not.
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
