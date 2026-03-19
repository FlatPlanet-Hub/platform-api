using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupabaseProxy.Application.DTOs;
using SupabaseProxy.Application.DTOs.Project;
using SupabaseProxy.Application.Interfaces;

namespace SupabaseProxy.API.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/members")]
[Authorize]
public sealed class ProjectMemberController : ControllerBase
{
    private readonly IProjectService _projectService;

    public ProjectMemberController(IProjectService projectService) => _projectService = projectService;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<ProjectMemberResponse>>>> GetMembers(Guid projectId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var members = await _projectService.GetMembersAsync(projectId, userId.Value);
        return Ok(ApiResponse<IEnumerable<ProjectMemberResponse>>.Ok(members));
    }

    [HttpPost("invite")]
    public async Task<ActionResult<ApiResponse<object?>>> Invite(Guid projectId, [FromBody] InviteUserRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _projectService.InviteMemberAsync(projectId, userId.Value, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpPut("{targetUserId:guid}/role")]
    public async Task<ActionResult<ApiResponse<object?>>> UpdateRole(Guid projectId, Guid targetUserId, [FromBody] UpdateMemberRoleRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _projectService.UpdateMemberRoleAsync(projectId, targetUserId, userId.Value, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpDelete("{targetUserId:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> RemoveMember(Guid projectId, Guid targetUserId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _projectService.RemoveMemberAsync(projectId, targetUserId, userId.Value);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    private Guid? GetUserId()
    {
        var sub = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
