using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Admin;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.API.Filters;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/admin/roles")]
[Authorize]
[RequirePermission("manage_roles")]
public sealed class AdminRoleController : ApiControllerBase
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
        var adminId = GetUserId();
        if (adminId is null) return Unauthorized();

        var role = await _adminRoleService.CreateRoleAsync(adminId.Value, request);
        return CreatedAtAction(nameof(List), ApiResponse<AdminRoleDto>.Ok(role));
    }

    [HttpPut("{roleId:guid}")]
    public async Task<ActionResult<ApiResponse<AdminRoleDto>>> Update(Guid roleId, [FromBody] UpdateCustomRoleRequest request)
    {
        var adminId = GetUserId();
        if (adminId is null) return Unauthorized();

        var role = await _adminRoleService.UpdateRoleAsync(adminId.Value, roleId, request);
        return Ok(ApiResponse<AdminRoleDto>.Ok(role));
    }

    [HttpDelete("{roleId:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> Deactivate(Guid roleId)
    {
        var adminId = GetUserId();
        if (adminId is null) return Unauthorized();

        await _adminRoleService.DeactivateRoleAsync(adminId.Value, roleId);
        return Ok(ApiResponse<object?>.Ok(null));
    }
}
