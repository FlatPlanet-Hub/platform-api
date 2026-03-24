using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using Dapper;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/compliance")]
[Authorize]
public sealed class ComplianceController(IDbConnectionFactory connectionFactory) : ApiControllerBase
{
    [HttpPost("consent")]
    public async Task<ActionResult<ApiResponse<object?>>> RecordConsent([FromBody] RecordConsentRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO platform.consent_records
                (id, user_id, consent_type, version, consented, ip_address, consented_at)
            VALUES
                (gen_random_uuid(), @UserId, @ConsentType, @Version, @Consented, @IpAddress, now())
            """, new
        {
            UserId = userId,
            request.ConsentType,
            request.Version,
            request.Consented,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpGet("consent/{userId:guid}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<ConsentRecord>>>> GetConsents(Guid userId)
    {
        using var conn = connectionFactory.CreateConnection();
        var records = await conn.QueryAsync<ConsentRecord>(
            "SELECT * FROM platform.consent_records WHERE user_id = @UserId ORDER BY consented_at DESC",
            new { UserId = userId });
        return Ok(ApiResponse<IEnumerable<ConsentRecord>>.Ok(records));
    }

    [HttpPost("incidents")]
    public async Task<ActionResult<ApiResponse<IncidentDto>>> ReportIncident([FromBody] ReportIncidentRequest request)
    {
        var userId = GetUserId();
        using var conn = connectionFactory.CreateConnection();
        var id = Guid.NewGuid();

        await conn.ExecuteAsync("""
            INSERT INTO platform.incident_log
                (id, reported_by, severity, title, description, affected_app_id, affected_users_count, status, reported_at)
            VALUES
                (@Id, @ReportedBy, @Severity, @Title, @Description, @AffectedAppId, @AffectedUsersCount, 'open', now())
            """, new
        {
            Id = id,
            ReportedBy = userId,
            request.Severity,
            request.Title,
            request.Description,
            request.AffectedAppId,
            request.AffectedUsersCount
        });

        return Ok(ApiResponse<IncidentDto>.Ok(new IncidentDto
        {
            Id = id,
            Severity = request.Severity,
            Title = request.Title,
            Description = request.Description,
            Status = "open",
            ReportedAt = DateTime.UtcNow
        }));
    }

    [HttpGet("incidents")]
    public async Task<ActionResult<ApiResponse<IEnumerable<IncidentDto>>>> ListIncidents()
    {
        using var conn = connectionFactory.CreateConnection();
        var incidents = await conn.QueryAsync<IncidentDto>(
            "SELECT id, severity, title, description, status, resolution, reported_at, resolved_at FROM platform.incident_log ORDER BY reported_at DESC");
        return Ok(ApiResponse<IEnumerable<IncidentDto>>.Ok(incidents));
    }

    [HttpPut("incidents/{id:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> UpdateIncident(Guid id, [FromBody] UpdateIncidentRequest request)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            UPDATE platform.incident_log
            SET status = COALESCE(@Status, status),
                resolution = COALESCE(@Resolution, resolution),
                resolved_at = CASE WHEN @Status = 'resolved' THEN now() ELSE resolved_at END,
                closed_at   = CASE WHEN @Status = 'closed'   THEN now() ELSE closed_at   END
            WHERE id = @Id
            """, new { Id = id, request.Status, request.Resolution });

        return Ok(ApiResponse<object?>.Ok(null));
    }
}
