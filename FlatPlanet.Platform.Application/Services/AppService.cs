using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Services;

public sealed class AppService(
    IAppRepository appRepo,
    IUserAppRoleRepository userAppRoleRepo,
    IUserRepository userRepo,
    IRoleRepository roleRepo) : IAppService
{
    public async Task<AppDto> RegisterAsync(RegisterAppRequest request, Guid registeredBy)
    {
        var existing = await appRepo.GetBySlugAsync(request.Slug);
        if (existing is not null)
            throw new InvalidOperationException($"App slug '{request.Slug}' is already in use.");

        var app = new App
        {
            Id = Guid.NewGuid(),
            CompanyId = request.CompanyId,
            Name = request.Name,
            Slug = request.Slug,
            BaseUrl = request.BaseUrl,
            Status = "active",
            RegisteredBy = registeredBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await appRepo.CreateAsync(app);
        return ToDto(app);
    }

    public async Task<IEnumerable<AppDto>> ListAsync(Guid? userId = null)
    {
        var apps = userId.HasValue
            ? await appRepo.GetByUserIdAsync(userId.Value)
            : await appRepo.GetAllAsync();
        return apps.Select(ToDto);
    }

    public async Task<AppDto?> GetByIdAsync(Guid id)
    {
        var app = await appRepo.GetByIdAsync(id);
        return app is null ? null : ToDto(app);
    }

    public async Task<AppDto> UpdateAsync(Guid id, UpdateAppRequest request)
    {
        var app = await appRepo.GetByIdAsync(id)
            ?? throw new InvalidOperationException("App not found.");

        if (request.Name is not null) app.Name = request.Name;
        if (request.BaseUrl is not null) app.BaseUrl = request.BaseUrl;
        app.UpdatedAt = DateTime.UtcNow;

        await appRepo.UpdateAsync(app);
        return ToDto(app);
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        var app = await appRepo.GetByIdAsync(id)
            ?? throw new InvalidOperationException("App not found.");
        await appRepo.UpdateStatusAsync(id, status);
    }

    public async Task<IEnumerable<AppUserDto>> GetUsersAsync(Guid appId)
    {
        var userRoles = (await userAppRoleRepo.GetByAppAsync(appId)).ToList();
        var result = new List<AppUserDto>();

        foreach (var ur in userRoles)
        {
            var user = await userRepo.GetByIdAsync(ur.UserId);
            var role = await roleRepo.GetByIdAsync(ur.RoleId);
            if (user is null) continue;

            result.Add(new AppUserDto
            {
                UserId = ur.UserId,
                FullName = user.FullName,
                Email = user.Email,
                RoleId = ur.RoleId,
                RoleName = role?.Name ?? string.Empty,
                GrantedAt = ur.GrantedAt,
                ExpiresAt = ur.ExpiresAt,
                Status = ur.Status
            });
        }

        return result;
    }

    public async Task GrantAccessAsync(Guid appId, GrantUserAccessRequest request, Guid grantedBy)
    {
        await userAppRoleRepo.GrantAsync(new UserAppRole
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            AppId = appId,
            RoleId = request.RoleId,
            GrantedBy = grantedBy,
            GrantedAt = DateTime.UtcNow,
            ExpiresAt = request.ExpiresAt,
            Status = "active"
        });
    }

    public async Task RevokeAccessAsync(Guid appId, Guid userId) =>
        await userAppRoleRepo.RevokeAsync(userId, appId);

    public async Task ChangeUserRoleAsync(Guid appId, Guid userId, Guid newRoleId) =>
        await userAppRoleRepo.ChangeRoleAsync(userId, appId, newRoleId);

    private static AppDto ToDto(App a) => new()
    {
        Id = a.Id,
        CompanyId = a.CompanyId,
        Name = a.Name,
        Slug = a.Slug,
        BaseUrl = a.BaseUrl,
        SchemaName = a.SchemaName,
        Status = a.Status,
        CreatedAt = a.CreatedAt
    };
}
