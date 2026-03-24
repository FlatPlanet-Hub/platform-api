using System.Text;
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
    });

builder.Services.AddAuthorization();

// Rate limiting — fixed window per user (sub claim)
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("per-user", httpContext =>
    {
        var userId = httpContext.User.FindFirst("sub")?.Value
                     ?? httpContext.Connection.RemoteIpAddress?.ToString()
                     ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
builder.Services.AddControllers();

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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ProjectScopeMiddleware>();

app.MapControllers().RequireRateLimiting("per-user");
app.MapHealthChecks("/health");

app.Run();
