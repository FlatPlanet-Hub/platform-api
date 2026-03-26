using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.API.Middleware;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/projects/{projectId:guid}/schema")]
[Authorize]
public sealed class SchemaController : ApiControllerBase
{
    private readonly IDbProxyService _dbProxy;

    public SchemaController(IDbProxyService dbProxy)
    {
        _dbProxy = dbProxy;
    }

    [HttpGet("tables")]
    public async Task<ActionResult<ApiResponse<IEnumerable<TableInfoDto>>>> GetTables()
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();
        if (!claims.HasPermission("read"))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Missing 'read' permission."));

        var tables = await _dbProxy.GetTablesAsync(claims.Schema);
        return Ok(ApiResponse<IEnumerable<TableInfoDto>>.Ok(tables));
    }

    [HttpGet("columns")]
    public async Task<ActionResult<ApiResponse<IEnumerable<ColumnInfoDto>>>> GetColumns([FromQuery] string? table)
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();
        if (!claims.HasPermission("read"))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Missing 'read' permission."));

        var columns = await _dbProxy.GetColumnsAsync(claims.Schema, table);
        return Ok(ApiResponse<IEnumerable<ColumnInfoDto>>.Ok(columns));
    }

    [HttpGet("relationships")]
    public async Task<ActionResult<ApiResponse<IEnumerable<RelationshipDto>>>> GetRelationships()
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();
        if (!claims.HasPermission("read"))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Missing 'read' permission."));

        var relationships = await _dbProxy.GetRelationshipsAsync(claims.Schema);
        return Ok(ApiResponse<IEnumerable<RelationshipDto>>.Ok(relationships));
    }

    [HttpGet("full")]
    public async Task<ActionResult<ApiResponse<FullSchemaDto>>> GetFull()
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();
        if (!claims.HasPermission("read"))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Missing 'read' permission."));

        var full = await _dbProxy.GetFullSchemaAsync(claims.Schema);
        return Ok(ApiResponse<FullSchemaDto>.Ok(full));
    }

    private ProjectClaims? GetClaims() =>
        HttpContext.Items[ProjectScopeMiddleware.ClaimsKey] as ProjectClaims;
}
