using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.API.Filters;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/roles")]
[Authorize]
public sealed class RoleController : ApiControllerBase
{
    private readonly IUserService _userService;

    public RoleController(IUserService userService) => _userService = userService;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<object>>>> GetRoles()
    {
        var roles = await _userService.GetSystemRolesAsync();
        var result = roles.Select(r => new { r.Id, r.Name, r.Description, r.IsSystem });
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpPost("assign")]
    [RequireSystemRole("platform_admin")]
    public async Task<ActionResult<ApiResponse<object?>>> Assign([FromBody] RoleAssignRequest request)
    {
        var requestingUserId = GetUserId();
        if (requestingUserId is null) return Unauthorized();

        await _userService.AssignSystemRoleAsync(requestingUserId.Value, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpDelete("revoke")]
    [RequireSystemRole("platform_admin")]
    public async Task<ActionResult<ApiResponse<object?>>> Revoke([FromBody] RoleRevokeRequest request)
    {
        var requestingUserId = GetUserId();
        if (requestingUserId is null) return Unauthorized();

        await _userService.RevokeSystemRoleAsync(requestingUserId.Value, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }
}
