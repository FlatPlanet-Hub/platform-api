using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Admin;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.API.Filters;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/admin/permissions")]
[Authorize]
[RequirePermission("manage_roles")]
public sealed class AdminPermissionController : ApiControllerBase
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
