using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupabaseProxy.API.Middleware;
using SupabaseProxy.Application.DTOs;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;

namespace SupabaseProxy.API.Controllers;

[ApiController]
[Route("api/schema")]
[Authorize]
public sealed class SchemaController : ControllerBase
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

        var tables = await _dbProxy.GetTablesAsync(claims.Schema);
        return Ok(ApiResponse<IEnumerable<TableInfoDto>>.Ok(tables));
    }

    [HttpGet("columns")]
    public async Task<ActionResult<ApiResponse<IEnumerable<ColumnInfoDto>>>> GetColumns([FromQuery] string? table)
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();

        var columns = await _dbProxy.GetColumnsAsync(claims.Schema, table);
        return Ok(ApiResponse<IEnumerable<ColumnInfoDto>>.Ok(columns));
    }

    [HttpGet("relationships")]
    public async Task<ActionResult<ApiResponse<IEnumerable<RelationshipDto>>>> GetRelationships()
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();

        var relationships = await _dbProxy.GetRelationshipsAsync(claims.Schema);
        return Ok(ApiResponse<IEnumerable<RelationshipDto>>.Ok(relationships));
    }

    [HttpGet("full")]
    public async Task<ActionResult<ApiResponse<FullSchemaDto>>> GetFull()
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();

        var full = await _dbProxy.GetFullSchemaAsync(claims.Schema);
        return Ok(ApiResponse<FullSchemaDto>.Ok(full));
    }

    private ProjectClaims? GetClaims() =>
        HttpContext.Items[ProjectScopeMiddleware.ClaimsKey] as ProjectClaims;
}
