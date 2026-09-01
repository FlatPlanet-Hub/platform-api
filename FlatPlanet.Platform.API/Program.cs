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
//   1. GlobalLimiter[user]     — 1000/min per user (or IP)   — runaway single client
//   2. GlobalLimiter[project]  —  500/min per project        — noisy neighbour across users
//   3. "project-query" policy  —   40/min per (project,user) — one user hoarding a project's quota
//
// Rationale for a multi-user app like ApprovalFlow (~25 concurrent users on ONE projectId):
//   - The old per-project 100/min meant those 25 users SHARED 100 requests/min. Five simultaneous
//     `get-app-data` calls (~21 queries each) exhausted the window.
//   - Layering the cap: any single user is bounded to 40/min inside a project (stops a
//     runaway Claude loop or a single misbehaving script), while the project as a whole
//     can absorb up to 500/min. Sizing: realistic interactive load is ~8/min per user; at 25
//     users that averages 200/min, so 500/min gives ~2.5x headroom for concurrent bursts
//     (bulk approvals, morning login stampede). Twelve users simultaneously at their full
//     40/min personal cap = 480/min — still fits under the project ceiling. Beyond that, the
//     project cap engages before the DB connection pool is at risk.
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
        // Layer 2: per-project — 500/min. Only fires on routes that carry a
        // {projectId} template value. Endpoints without one get a no-op partition,
        // so this layer is inert for auth/admin endpoints and only guards the
        // project-scoped surface (/api/projects/{id}/*).
        //
        // 500 is chosen against the per-user layer below (40/min per user):
        //   - realistic interactive load: 25 users × 8/min = 200/min → 2.5x headroom
        //   - up to 12 users at their FULL 40/min personal cap = 480/min → still fits
        //   - a runaway app that somehow gets past layer 3 caps out here
        PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            var projectId = httpContext.GetRouteValue("projectId")?.ToString();
            if (string.IsNullOrEmpty(projectId))
            {
                return RateLimitPartition.GetNoLimiter<string>("no-project");
            }
            return RateLimitPartition.GetFixedWindowLimiter(projectId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 500,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
        })
    );

    // Layer 3: named policy for query endpoints only.
    // Applied via [EnableRateLimiting("project-query")] on QueryController actions.
    // Partitioned by (projectId, userId) so one user's runaway retries burn only
    // their own 40/min slice — other users on the same project stay responsive.
    options.AddPolicy("project-query", httpContext =>
    {
        var projectId = httpContext.GetRouteValue("projectId")?.ToString() ?? "unknown";
        var userId    = httpContext.User.FindFirst("sub")?.Value
                        ?? httpContext.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous";
        var key = $"{projectId}::{userId}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            // 40/min per (project, user) covers realistic interactive load —
            // ApprovalFlow's batched `get-app-data` fires ~21 queries per login,
            // so a user can log in and browse without tripping. A tight retry
            // loop hits the ceiling in ~1 second and self-throttles.
            PermitLimit = 40,
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
