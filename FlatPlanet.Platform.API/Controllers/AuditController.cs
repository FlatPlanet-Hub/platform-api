using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/audit")]
[Authorize]
public sealed class AuditController(IAuditService auditService) : ApiControllerBase
{
    [HttpGet("auth")]
    public async Task<ActionResult<ApiResponse<object>>> QueryAuthLog(
        [FromQuery] Guid? userId,
        [FromQuery] Guid? appId,
        [FromQuery] string? eventType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var logs = await auditService.QueryAsync(userId, appId, eventType, from, to, page, pageSize);
        return Ok(ApiResponse<object>.Ok(logs));
    }
}
