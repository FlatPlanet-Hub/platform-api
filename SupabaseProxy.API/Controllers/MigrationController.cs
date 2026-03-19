using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupabaseProxy.API.Middleware;
using SupabaseProxy.Application.Common.Helpers;
using SupabaseProxy.Application.DTOs;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;

namespace SupabaseProxy.API.Controllers;

[ApiController]
[Route("api/migration")]
[Authorize]
public sealed class MigrationController : ControllerBase
{
    private readonly IDbProxyService _dbProxy;

    public MigrationController(IDbProxyService dbProxy)
    {
        _dbProxy = dbProxy;
    }

    [HttpPost("create-schema")]
    public async Task<ActionResult<ApiResponse<object?>>> CreateSchema()
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();

        if (!claims.HasPermission("ddl"))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Missing 'ddl' permission."));

        await _dbProxy.CreateSchemaAsync(claims.Schema);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpPost("create-table")]
    public async Task<ActionResult<ApiResponse<object?>>> CreateTable([FromBody] CreateTableRequest request)
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();

        if (!claims.HasPermission("ddl"))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Missing 'ddl' permission."));

        if (!SqlValidationHelper.IsValidIdentifier(request.TableName))
            return BadRequest(ApiResponse<object>.Fail("Invalid table name."));

        if (request.Columns is null || request.Columns.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("At least one column is required."));

        foreach (var col in request.Columns)
        {
            if (!SqlValidationHelper.IsValidIdentifier(col.Name))
                return BadRequest(ApiResponse<object>.Fail($"Invalid column name: {col.Name}"));
        }

        await _dbProxy.CreateTableAsync(claims.Schema, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpPut("alter-table")]
    public async Task<ActionResult<ApiResponse<object?>>> AlterTable([FromBody] AlterTableRequest request)
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();

        if (!claims.HasPermission("ddl"))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Missing 'ddl' permission."));

        if (!SqlValidationHelper.IsValidIdentifier(request.TableName))
            return BadRequest(ApiResponse<object>.Fail("Invalid table name."));

        foreach (var op in request.Operations)
        {
            if (!SqlValidationHelper.IsValidIdentifier(op.ColumnName))
                return BadRequest(ApiResponse<object>.Fail($"Invalid column name: {op.ColumnName}"));

            if (op.NewColumnName is not null && !SqlValidationHelper.IsValidIdentifier(op.NewColumnName))
                return BadRequest(ApiResponse<object>.Fail($"Invalid new column name: {op.NewColumnName}"));
        }

        await _dbProxy.AlterTableAsync(claims.Schema, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpDelete("drop-table")]
    public async Task<ActionResult<ApiResponse<object?>>> DropTable([FromQuery] string table)
    {
        var claims = GetClaims();
        if (claims is null) return Forbid();

        if (!claims.HasPermission("ddl"))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Missing 'ddl' permission."));

        if (!SqlValidationHelper.IsValidIdentifier(table))
            return BadRequest(ApiResponse<object>.Fail("Invalid table name."));

        await _dbProxy.DropTableAsync(claims.Schema, table);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    private ProjectClaims? GetClaims() =>
        HttpContext.Items[ProjectScopeMiddleware.ClaimsKey] as ProjectClaims;
}
