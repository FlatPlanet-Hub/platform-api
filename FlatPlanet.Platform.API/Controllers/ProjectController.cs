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

    public ProjectController(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> Create([FromBody] CreateProjectRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("Project name is required."));

        if (!Guid.TryParse(User.FindFirst("company_id")?.Value, out var companyId) || companyId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail("Valid company_id claim is required."));

        var actorEmail = User.FindFirst("email")?.Value ?? string.Empty;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await _projectService.CreateProjectAsync(userId.Value, actorEmail, companyId, baseUrl, request, ip);
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

        var actorEmail = User.FindFirst("email")?.Value ?? string.Empty;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _projectService.DeactivateProjectAsync(id, userId.Value, actorEmail, ip);
        return Ok(ApiResponse<object?>.Ok(null));
    }


}
