using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/apps")]
[Authorize]
public sealed class AppsController(IAppService appService) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApiResponse<AppDto>>> Register([FromBody] RegisterAppRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();
        var result = await appService.RegisterAsync(request, userId.Value);
        return Ok(ApiResponse<AppDto>.Ok(result));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<AppDto>>>> List()
    {
        var userId = GetUserId();
        var result = await appService.ListAsync(userId);
        return Ok(ApiResponse<IEnumerable<AppDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AppDto>>> GetById(Guid id)
    {
        var result = await appService.GetByIdAsync(id);
        if (result is null) return NotFound(ApiResponse<object>.Fail("App not found."));
        return Ok(ApiResponse<AppDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AppDto>>> Update(Guid id, [FromBody] UpdateAppRequest request)
    {
        var result = await appService.UpdateAsync(id, request);
        return Ok(ApiResponse<AppDto>.Ok(result));
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<object?>>> UpdateStatus(Guid id, [FromBody] UpdateAppStatusRequest request)
    {
        await appService.UpdateStatusAsync(id, request.Status);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    // ── User Access Management ─────────────────────────────────────────────

    [HttpGet("{appId:guid}/users")]
    public async Task<ActionResult<ApiResponse<IEnumerable<AppUserDto>>>> GetUsers(Guid appId)
    {
        var result = await appService.GetUsersAsync(appId);
        return Ok(ApiResponse<IEnumerable<AppUserDto>>.Ok(result));
    }

    [HttpPost("{appId:guid}/users")]
    public async Task<ActionResult<ApiResponse<object?>>> GrantAccess(Guid appId, [FromBody] GrantUserAccessRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();
        await appService.GrantAccessAsync(appId, request, userId.Value);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpDelete("{appId:guid}/users/{userId:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> RevokeAccess(Guid appId, Guid userId)
    {
        await appService.RevokeAccessAsync(appId, userId);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpPut("{appId:guid}/users/{userId:guid}/role")]
    public async Task<ActionResult<ApiResponse<object?>>> ChangeRole(Guid appId, Guid userId, [FromBody] ChangeUserRoleRequest request)
    {
        await appService.ChangeUserRoleAsync(appId, userId, request.RoleId);
        return Ok(ApiResponse<object?>.Ok(null));
    }
}
