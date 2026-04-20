using System.Text.Json.Serialization;

namespace FlatPlanet.Platform.Application.DTOs.Dataverse;

public sealed record EmployeeDto(
    [property: JsonPropertyName("fp_name")]
    string Name,

    [property: JsonPropertyName("fp_employmentdate")]
    DateTime? EmploymentDate,

    [property: JsonPropertyName("fp_separationdate")]
    DateTime? SeparationDate,

    [property: JsonPropertyName("fp_employmentstatus@OData.Community.Display.V1.FormattedValue")]
    string? EmploymentStatus,

    [property: JsonPropertyName("_fp_activereportingto_value@OData.Community.Display.V1.FormattedValue")]
    string? ClientOpsLead,

    [property: JsonPropertyName("_fp_activeclient_value@OData.Community.Display.V1.FormattedValue")]
    string? Client);
