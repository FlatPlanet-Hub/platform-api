namespace FlatPlanet.Platform.Application.DTOs.Iam;

public sealed class AuthorizeRequest
{
    public Guid UserId { get; init; }
    public string AppSlug { get; init; } = string.Empty;
    public string ResourceIdentifier { get; init; } = string.Empty;
    public string? RequiredPermission { get; init; }
}

public sealed class AuthorizeResponse
{
    public bool Allowed { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public IReadOnlyDictionary<string, string> Policies { get; init; } = new Dictionary<string, string>();
    public bool MfaRequired { get; init; }
}
