using System.Text.Json.Serialization;

namespace FlatPlanet.Platform.Application.DTOs.Dataverse;

public sealed record AccountDto(
    [property: JsonPropertyName("fp_name")]
    string? Name);
