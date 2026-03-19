using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupabaseProxy.API.Middleware;
using SupabaseProxy.Application.Common.Helpers;
using SupabaseProxy.Application.DTOs;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;

namespace SupabaseProxy.API.Controllers;

[ApiController]
[Route("api/query")]
[Authorize]
public sealed class QueryController : ControllerBase
{
    private readonly IDbProxyService _dbProxy;

    public QueryController(IDbProxyService dbProxy)
    {
        _dbProxy = dbProxy;
    }

    [HttpPost("read")]
    public async Task<ActionResult<ApiResponse<IEnumerable<dynamic>>>> Read([FromBody] ReadQueryRequest request)
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();

        if (!claims.HasPermission("read"))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Missing 'read' permission."));

        var (isValid, error) = SqlValidationHelper.ValidateReadQuery(request.Sql);
        if (!isValid)
            return BadRequest(ApiResponse<object>.Fail(error!));

        var result = await _dbProxy.ExecuteReadAsync(claims.Schema, request);
        return Ok(ApiResponse<IEnumerable<dynamic>>.Ok(result));
    }

    [HttpPost("write")]
    public async Task<ActionResult<ApiResponse<object?>>> Write([FromBody] WriteQueryRequest request)
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();

        if (!claims.HasPermission("write"))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Missing 'write' permission."));

        var (isValid, error) = SqlValidationHelper.ValidateWriteQuery(request.Sql);
        if (!isValid)
            return BadRequest(ApiResponse<object>.Fail(error!));

        var rowsAffected = await _dbProxy.ExecuteWriteAsync(claims.Schema, request);
        return Ok(ApiResponse<object?>.Ok(null, rowsAffected));
    }

    private ProjectClaims? GetClaims() =>
        HttpContext.Items[ProjectScopeMiddleware.ClaimsKey] as ProjectClaims;
}
