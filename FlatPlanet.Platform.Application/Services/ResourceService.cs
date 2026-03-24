using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ResourceService(IResourceRepository repo) : IResourceService
{
    public async Task<ResourceDto> CreateAsync(Guid appId, CreateResourceRequest request)
    {
        var resource = new Resource
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            ResourceTypeId = request.ResourceTypeId,
            Name = request.Name,
            Identifier = request.Identifier,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        await repo.CreateAsync(resource);
        return ToDto(resource, string.Empty);
    }

    public async Task<IEnumerable<ResourceDto>> GetByAppIdAsync(Guid appId)
    {
        var resources = await repo.GetByAppIdAsync(appId);
        return resources.Select(r => ToDto(r, string.Empty));
    }

    public async Task<ResourceDto?> GetByIdAsync(Guid id)
    {
        var resource = await repo.GetByIdAsync(id);
        return resource is null ? null : ToDto(resource, string.Empty);
    }

    public async Task<ResourceDto> UpdateAsync(Guid appId, Guid id, UpdateResourceRequest request)
    {
        var resource = await repo.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Resource not found.");

        if (resource.AppId != appId)
            throw new UnauthorizedAccessException("Resource does not belong to this app.");

        if (request.Name is not null) resource.Name = request.Name;
        if (request.Identifier is not null) resource.Identifier = request.Identifier;
        if (request.Status is not null) resource.Status = request.Status;

        await repo.UpdateAsync(resource);
        return ToDto(resource, string.Empty);
    }

    public async Task DeactivateAsync(Guid appId, Guid id)
    {
        var resource = await repo.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Resource not found.");

        if (resource.AppId != appId)
            throw new UnauthorizedAccessException("Resource does not belong to this app.");

        await repo.DeactivateAsync(id);
    }

    public async Task<IEnumerable<ResourceTypeDto>> GetTypesAsync()
    {
        var types = await repo.GetAllTypesAsync();
        return types.Select(t => new ResourceTypeDto { Id = t.Id, Name = t.Name, Description = t.Description });
    }

    private static ResourceDto ToDto(Resource r, string typeName) => new()
    {
        Id = r.Id,
        AppId = r.AppId,
        ResourceTypeId = r.ResourceTypeId,
        ResourceTypeName = typeName,
        Name = r.Name,
        Identifier = r.Identifier,
        Status = r.Status,
        CreatedAt = r.CreatedAt
    };
}
