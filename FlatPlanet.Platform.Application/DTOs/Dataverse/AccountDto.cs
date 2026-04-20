using System.Text.Json.Serialization;

namespace FlatPlanet.Platform.Application.DTOs.Dataverse;

public sealed record AccountDto(
    [property: JsonPropertyName("name")]
    string? Name);
