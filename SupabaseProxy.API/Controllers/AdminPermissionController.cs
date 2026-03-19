using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupabaseProxy.Application.DTOs;
using SupabaseProxy.Application.DTOs.Admin;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.API.Filters;

namespace SupabaseProxy.API.Controllers;

[ApiController]
[Route("api/admin/permissions")]
[Authorize]
[RequirePermission("manage_roles")]
public sealed class AdminPermissionController : ControllerBase
{
    private readonly IPermissionRepository _permissionRepo;

    public AdminPermissionController(IPermissionRepository permissionRepo) =>
        _permissionRepo = permissionRepo;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<PermissionDto>>>> List()
    {
        var permissions = await _permissionRepo.GetAllAsync();
        var dtos = permissions.Select(p => new PermissionDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Category = p.Category
        });
        return Ok(ApiResponse<IEnumerable<PermissionDto>>.Ok(dtos));
    }
}
