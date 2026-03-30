using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.API.Filters;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/platform/audit-log")]
[Authorize]
[RequirePermission("view_all_projects")]
public sealed class AuditLogController(IAuditLogRepository auditLog) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<AuditLogDto>>>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? actorId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var items = await auditLog.GetPagedAsync(page, pageSize, actorId, from, to);
        return Ok(ApiResponse<IEnumerable<AuditLogDto>>.Ok(items));
    }
}
