using Microsoft.AspNetCore.Mvc;
using SupabaseProxy.Application.Common.Helpers;
using SupabaseProxy.Application.DTOs;
using SupabaseProxy.Application.Interfaces;

namespace SupabaseProxy.API.Controllers;

[ApiController]
[Route("api/token")]
public sealed class TokenController : ControllerBase
{
    private readonly IJwtService _jwtService;

    public TokenController(IJwtService jwtService)
    {
        _jwtService = jwtService;
    }

    [HttpPost("generate")]
    public ActionResult<ApiResponse<string>> Generate([FromBody] GenerateTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(ApiResponse<string>.Fail("UserId is required."));

        if (string.IsNullOrWhiteSpace(request.ProjectId))
            return BadRequest(ApiResponse<string>.Fail("ProjectId is required."));

        if (!SqlValidationHelper.IsValidSchemaName(request.Schema))
            return BadRequest(ApiResponse<string>.Fail("Schema must match pattern project_<name> with only lowercase letters, digits, and underscores."));

        if (string.IsNullOrWhiteSpace(request.Permissions))
            return BadRequest(ApiResponse<string>.Fail("Permissions are required."));

        var token = _jwtService.GenerateToken(request);
        return Ok(ApiResponse<string>.Ok(token));
    }
}
