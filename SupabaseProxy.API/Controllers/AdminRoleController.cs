using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupabaseProxy.Application.DTOs;
using SupabaseProxy.Application.DTOs.Admin;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.API.Filters;

namespace SupabaseProxy.API.Controllers;

[ApiController]
[Route("api/admin/roles")]
[Authorize]
[RequirePermission("manage_roles")]
public sealed class AdminRoleController : ControllerBase
{
    private readonly IAdminRoleService _adminRoleService;

    public AdminRoleController(IAdminRoleService adminRoleService) =>
        _adminRoleService = adminRoleService;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<AdminRoleDto>>>> List()
    {
        var roles = await _adminRoleService.ListRolesAsync();
        return Ok(ApiResponse<IEnumerable<AdminRoleDto>>.Ok(roles));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminRoleDto>>> Create([FromBody] CreateCustomRoleRequest request)
    {
        var adminId = GetAdminId();
        if (adminId is null) return Unauthorized();

        var role = await _adminRoleService.CreateRoleAsync(adminId.Value, request);
        return CreatedAtAction(nameof(List), ApiResponse<AdminRoleDto>.Ok(role));
    }

    [HttpPut("{roleId:guid}")]
    public async Task<ActionResult<ApiResponse<AdminRoleDto>>> Update(Guid roleId, [FromBody] UpdateCustomRoleRequest request)
    {
        var adminId = GetAdminId();
        if (adminId is null) return Unauthorized();

        var role = await _adminRoleService.UpdateRoleAsync(adminId.Value, roleId, request);
        return Ok(ApiResponse<AdminRoleDto>.Ok(role));
    }

    [HttpDelete("{roleId:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> Deactivate(Guid roleId)
    {
        var adminId = GetAdminId();
        if (adminId is null) return Unauthorized();

        await _adminRoleService.DeactivateRoleAsync(adminId.Value, roleId);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    private Guid? GetAdminId()
    {
        var sub = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
