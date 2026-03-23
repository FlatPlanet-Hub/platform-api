namespace FlatPlanet.Platform.Application.DTOs.Iam;

/// <summary>Per-app claims embedded in the JWT apps[] array.</summary>
public sealed class IamAppClaims
{
    public string AppId { get; init; } = string.Empty;
    public string AppSlug { get; init; } = string.Empty;
    public string? Schema { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];
}
