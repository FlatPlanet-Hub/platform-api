using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupabaseProxy.API.Filters;
using SupabaseProxy.Application.DTOs;
using SupabaseProxy.Application.DTOs.Auth;
using SupabaseProxy.Application.Interfaces;

namespace SupabaseProxy.API.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public sealed class RoleController : ControllerBase
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

    private Guid? GetUserId()
    {
        var sub = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
