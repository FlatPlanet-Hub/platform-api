using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/platform/audit-log")]
[Authorize]
public sealed class AuditLogController(
    IAuditLogRepository auditLog,
    ISecurityPlatformService securityPlatform) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<AuditLogDto>>>> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? actorId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var appAccess = await securityPlatform.GetUserAppAccessAsync(userId.Value);
        var isAuthorized = appAccess.Any(a =>
            a.AppSlug.Equals("dashboard-hub", StringComparison.OrdinalIgnoreCase) &&
            (a.RoleName.Equals("platform_owner", StringComparison.OrdinalIgnoreCase) ||
             a.Permissions.Contains("view_all_projects", StringComparer.OrdinalIgnoreCase)));

        if (!isAuthorized)
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Insufficient permissions."));

        pageSize = Math.Clamp(pageSize, 1, 200);
        page     = Math.Max(page, 1);

        var result = await auditLog.GetPagedAsync(page, pageSize, actorId, from, to);
        return OkData(result);
    }
}
