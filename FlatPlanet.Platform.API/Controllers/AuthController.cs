using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Auth;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    [HttpGet("me")]
    [Authorize]
    public ActionResult<ApiResponse<MeResponse>> Me()
    {
        var sub = User.FindFirst("sub")?.Value
               ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sub, out var userId))
            return Unauthorized();

        Guid.TryParse(User.FindFirst("company_id")?.Value, out var companyId);

        var permissionsClaim = User.FindFirst("permissions")?.Value ?? string.Empty;
        var canCreateProject = permissionsClaim
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains("create_project", StringComparer.OrdinalIgnoreCase);

        var response = new MeResponse
        {
            UserId = userId,
            Email = User.FindFirst("email")?.Value ?? string.Empty,
            FullName = User.FindFirst("name")?.Value ?? string.Empty,
            CompanyId = companyId == Guid.Empty ? null : companyId,
            CanCreateProject = canCreateProject
        };

        return Ok(ApiResponse<MeResponse>.Ok(response));
    }
}
