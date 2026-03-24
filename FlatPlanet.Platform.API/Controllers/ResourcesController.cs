using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/apps/{appId:guid}/resources")]
[Authorize]
public sealed class ResourcesController(IResourceService resourceService) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ResourceDto>>> Create(Guid appId, [FromBody] CreateResourceRequest request)
    {
        var result = await resourceService.CreateAsync(appId, request);
        return Ok(ApiResponse<ResourceDto>.Ok(result));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<ResourceDto>>>> List(Guid appId)
    {
        var result = await resourceService.GetByAppIdAsync(appId);
        return Ok(ApiResponse<IEnumerable<ResourceDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ResourceDto>>> GetById(Guid appId, Guid id)
    {
        var result = await resourceService.GetByIdAsync(id);
        if (result is null) return NotFound(ApiResponse<object>.Fail("Resource not found."));
        return Ok(ApiResponse<ResourceDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ResourceDto>>> Update(Guid appId, Guid id, [FromBody] UpdateResourceRequest request)
    {
        var result = await resourceService.UpdateAsync(appId, id, request);
        return Ok(ApiResponse<ResourceDto>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> Deactivate(Guid appId, Guid id)
    {
        await resourceService.DeactivateAsync(appId, id);
        return Ok(ApiResponse<object?>.Ok(null));
    }
}

[Route("api/resource-types")]
[Authorize]
public sealed class ResourceTypesController(IResourceService resourceService) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<ResourceTypeDto>>>> List()
    {
        var result = await resourceService.GetTypesAsync();
        return Ok(ApiResponse<IEnumerable<ResourceTypeDto>>.Ok(result));
    }
}
