using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/projects/{projectId:guid}/claude-config")]
[Authorize]
public sealed class ClaudeConfigController : ApiControllerBase
{
    private readonly IClaudeConfigService _claudeConfigService;

    public ClaudeConfigController(IClaudeConfigService claudeConfigService)
    {
        _claudeConfigService = claudeConfigService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<ClaudeConfigResponse>>> Generate(Guid projectId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var userName = User.FindFirst("name")?.Value ?? string.Empty;
        var userEmail = User.FindFirst("email")?.Value ?? string.Empty;
        var result = await _claudeConfigService.GenerateAsync(userId.Value, projectId, GetBaseUrl(), userName, userEmail);
        return Ok(ApiResponse<ClaudeConfigResponse>.Ok(result));
    }

    [HttpPost("regenerate")]
    public async Task<ActionResult<ApiResponse<ClaudeConfigResponse>>> Regenerate(Guid projectId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var userName = User.FindFirst("name")?.Value ?? string.Empty;
        var userEmail = User.FindFirst("email")?.Value ?? string.Empty;
        var result = await _claudeConfigService.RegenerateAsync(userId.Value, projectId, GetBaseUrl(), userName, userEmail);
        return Ok(ApiResponse<ClaudeConfigResponse>.Ok(result));
    }

    [HttpDelete]
    public async Task<ActionResult<ApiResponse<object?>>> Revoke(Guid projectId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _claudeConfigService.RevokeAsync(userId.Value, projectId);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    private string GetBaseUrl() => $"{Request.Scheme}://{Request.Host}";
}
