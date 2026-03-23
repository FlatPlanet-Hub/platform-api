using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/projects/{projectId:guid}/members")]
[Authorize]
public sealed class ProjectMemberController : ApiControllerBase
{
    private readonly IProjectMemberService _memberService;

    public ProjectMemberController(IProjectMemberService memberService) => _memberService = memberService;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<ProjectMemberResponse>>>> GetMembers(Guid projectId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var members = await _memberService.GetMembersAsync(projectId, userId.Value);
        return Ok(ApiResponse<IEnumerable<ProjectMemberResponse>>.Ok(members));
    }

    [HttpPost("invite")]
    public async Task<ActionResult<ApiResponse<object?>>> Invite(Guid projectId, [FromBody] InviteUserRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _memberService.InviteMemberAsync(projectId, userId.Value, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpPut("{targetUserId:guid}/role")]
    public async Task<ActionResult<ApiResponse<object?>>> UpdateRole(Guid projectId, Guid targetUserId, [FromBody] UpdateMemberRoleRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _memberService.UpdateMemberRoleAsync(projectId, targetUserId, userId.Value, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpDelete("{targetUserId:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> RemoveMember(Guid projectId, Guid targetUserId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _memberService.RemoveMemberAsync(projectId, targetUserId, userId.Value);
        return Ok(ApiResponse<object?>.Ok(null));
    }
}
