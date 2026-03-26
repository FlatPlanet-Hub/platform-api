using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/auth/api-tokens")]
[Authorize]
public sealed class ApiTokensController(IApiTokenService apiTokenService) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ApiTokenResponse>>> Create([FromBody] CreateApiTokenRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var apiBaseUrl = $"{Request.Scheme}://{Request.Host}";
        var userName = User.FindFirst("full_name")?.Value ?? string.Empty;
        var userEmail = User.FindFirst("email")?.Value ?? string.Empty;
        var result = await apiTokenService.CreateAsync(userId.Value, userName, userEmail, request, apiBaseUrl);
        return Ok(ApiResponse<ApiTokenResponse>.Ok(result));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<ApiTokenSummaryDto>>>> List()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await apiTokenService.ListActiveAsync(userId.Value);
        return Ok(ApiResponse<IEnumerable<ApiTokenSummaryDto>>.Ok(result));
    }

    [HttpDelete("{tokenId:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> Revoke(Guid tokenId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await apiTokenService.RevokeAsync(tokenId, userId.Value);
        return Ok(ApiResponse<object?>.Ok(null));
    }
}
