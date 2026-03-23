using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.API.Middleware;
using FlatPlanet.Platform.Application.Common.Helpers;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/migration")]
[Authorize]
public sealed class MigrationController : ApiControllerBase
{
    private readonly IDbProxyService _dbProxy;
    private readonly IGitHubRepoService _repoService;

    public MigrationController(IDbProxyService dbProxy, IGitHubRepoService repoService)
    {
        _dbProxy = dbProxy;
        _repoService = repoService;
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
        await TrySyncDataDictionaryAsync(claims);
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
        await TrySyncDataDictionaryAsync(claims);
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
        await TrySyncDataDictionaryAsync(claims);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    // Fire-and-forget style: DATA_DICTIONARY sync is best-effort — a GitHub failure
    // must never roll back a successful DDL operation.
    private async Task TrySyncDataDictionaryAsync(Domain.Entities.ProjectClaims claims)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _repoService.SyncDataDictionaryAsync(userId, Guid.Parse(claims.ProjectId), claims.Schema);
        }
        catch
        {
            // Intentionally swallowed — DDL succeeded; sync failure is non-fatal.
        }
    }

    private Domain.Entities.ProjectClaims? GetClaims() =>
        HttpContext.Items[ProjectScopeMiddleware.ClaimsKey] as Domain.Entities.ProjectClaims;
}
