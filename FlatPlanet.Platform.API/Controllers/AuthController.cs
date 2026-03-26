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
                  ?? User.FindFirst(
                      System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(sub, out var userId))
            return Unauthorized();

        var email    = User.FindFirst("email")?.Value ?? string.Empty;
        var fullName = User.FindFirst("full_name")?.Value ?? string.Empty;
        var companyId = User.FindFirst("company_id")?.Value;

        var permissions = (User.FindFirst("permissions")?.Value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return Ok(ApiResponse<MeResponse>.Ok(new MeResponse
        {
            UserId = userId,
            Email = email,
            FullName = fullName,
            CompanyId = companyId,
            CanCreateProject = permissions.Contains("create_project",
                StringComparer.OrdinalIgnoreCase)
        }));
    }
}
