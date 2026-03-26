using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.API.Middleware;
using FlatPlanet.Platform.Application.Common.Helpers;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/projects/{projectId:guid}/query")]
[Authorize]
public sealed class QueryController : ApiControllerBase
{
    private readonly IDbProxyService _dbProxy;
    private readonly IAuditService _audit;

    public QueryController(IDbProxyService dbProxy, IAuditService audit)
    {
        _dbProxy = dbProxy;
        _audit = audit;
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
        await _audit.LogAsync(GetUserId(), null, "query.write", claims.Schema,
            new { rowsAffected });
        return Ok(ApiResponse<object?>.Ok(null, rowsAffected));
    }

    private ProjectClaims? GetClaims() =>
        HttpContext.Items[ProjectScopeMiddleware.ClaimsKey] as ProjectClaims;
}
