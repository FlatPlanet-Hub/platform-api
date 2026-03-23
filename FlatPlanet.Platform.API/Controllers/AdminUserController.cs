using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Admin;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.API.Filters;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/admin/users")]
[Authorize]
[RequirePermission("manage_users")]
public sealed class AdminUserController : ApiControllerBase
{
    private readonly IAdminUserService _adminUserService;

    public AdminUserController(IAdminUserService adminUserService) =>
        _adminUserService = adminUserService;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<AdminUserListResponse>>> List(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? roleId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var filter = new AdminUserListFilter
        {
            Search = search,
            IsActive = isActive,
            RoleId = roleId,
            Page = page,
            PageSize = Math.Clamp(pageSize, 1, 100)
        };

        var result = await _adminUserService.ListUsersAsync(filter);
        return Ok(ApiResponse<AdminUserListResponse>.Ok(result));
    }

    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> Get(Guid userId)
    {
        var user = await _adminUserService.GetUserAsync(userId);
        return Ok(ApiResponse<AdminUserDto>.Ok(user));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> Create([FromBody] CreateAdminUserRequest request)
    {
        var adminId = GetUserId();
        if (adminId is null) return Unauthorized();

        var user = await _adminUserService.CreateUserAsync(adminId.Value, request);
        return CreatedAtAction(nameof(Get), new { userId = user.Id }, ApiResponse<AdminUserDto>.Ok(user));
    }

    [HttpPost("bulk")]
    public async Task<ActionResult<ApiResponse<IEnumerable<AdminUserDto>>>> BulkCreate([FromBody] BulkCreateUsersRequest request)
    {
        var adminId = GetUserId();
        if (adminId is null) return Unauthorized();

        var users = await _adminUserService.BulkCreateUsersAsync(adminId.Value, request);
        return Ok(ApiResponse<IEnumerable<AdminUserDto>>.Ok(users));
    }

    [HttpPut("{userId:guid}")]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> Update(Guid userId, [FromBody] UpdateAdminUserRequest request)
    {
        var adminId = GetUserId();
        if (adminId is null) return Unauthorized();

        var user = await _adminUserService.UpdateUserAsync(adminId.Value, userId, request);
        return Ok(ApiResponse<AdminUserDto>.Ok(user));
    }

    [HttpPut("{userId:guid}/roles")]
    public async Task<ActionResult<ApiResponse<object?>>> UpdateRoles(Guid userId, [FromBody] UpdateUserRolesRequest request)
    {
        var adminId = GetUserId();
        if (adminId is null) return Unauthorized();

        await _adminUserService.UpdateUserRolesAsync(adminId.Value, userId, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpPut("{userId:guid}/projects/{projectId:guid}/role")]
    public async Task<ActionResult<ApiResponse<object?>>> UpdateProjectRole(
        Guid userId, Guid projectId, [FromBody] UpdateUserProjectRoleRequest request)
    {
        var adminId = GetUserId();
        if (adminId is null) return Unauthorized();

        await _adminUserService.UpdateUserProjectRoleAsync(adminId.Value, userId, projectId, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpDelete("{userId:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> Deactivate(Guid userId)
    {
        var adminId = GetUserId();
        if (adminId is null) return Unauthorized();

        await _adminUserService.DeactivateUserAsync(adminId.Value, userId);
        return Ok(ApiResponse<object?>.Ok(null));
    }
}
