using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/projects")]
[Authorize]
public sealed class ProjectController : ApiControllerBase
{
    private readonly IProjectService _projectService;
    private readonly IProjectRoleService _roleService;

    public ProjectController(IProjectService projectService, IProjectRoleService roleService)
    {
        _projectService = projectService;
        _roleService = roleService;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> Create([FromBody] CreateProjectRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("Project name is required."));

        var result = await _projectService.CreateProjectAsync(userId.Value, request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<ProjectResponse>.Ok(result));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<ProjectResponse>>>> GetAll()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var projects = await _projectService.GetUserProjectsAsync(userId.Value);
        return Ok(ApiResponse<IEnumerable<ProjectResponse>>.Ok(projects));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> GetById(Guid id)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var project = await _projectService.GetProjectAsync(id, userId.Value);
        return Ok(ApiResponse<ProjectResponse>.Ok(project));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> Update(Guid id, [FromBody] UpdateProjectRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _projectService.UpdateProjectAsync(id, userId.Value, request);
        return Ok(ApiResponse<ProjectResponse>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> Deactivate(Guid id)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _projectService.DeactivateProjectAsync(id, userId.Value);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    // ── Project Roles ────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/roles")]
    public async Task<ActionResult<ApiResponse<IEnumerable<ProjectRoleResponse>>>> GetRoles(Guid id)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var roles = await _roleService.GetProjectRolesAsync(id, userId.Value);
        return Ok(ApiResponse<IEnumerable<ProjectRoleResponse>>.Ok(roles));
    }

    [HttpPost("{id:guid}/roles")]
    public async Task<ActionResult<ApiResponse<ProjectRoleResponse>>> CreateRole(Guid id, [FromBody] CreateProjectRoleRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var role = await _roleService.CreateProjectRoleAsync(id, userId.Value, request);
        return Ok(ApiResponse<ProjectRoleResponse>.Ok(role));
    }

    [HttpPut("{id:guid}/roles/{roleId:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> UpdateRole(Guid id, Guid roleId, [FromBody] UpdateProjectRoleRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _roleService.UpdateProjectRoleAsync(id, roleId, userId.Value, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpDelete("{id:guid}/roles/{roleId:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteRole(Guid id, Guid roleId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _roleService.DeleteProjectRoleAsync(id, roleId, userId.Value);
        return Ok(ApiResponse<object?>.Ok(null));
    }

}
