using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Platform.Application.DTOs.Azure;

public class ProvisionAzureRequest
{
    /// <summary>
    /// Optional. Overrides the auto-generated App Service name (derived from project slug).
    /// Must be 3–60 characters, lowercase alphanumeric and hyphens only, no leading/trailing hyphens.
    /// If omitted, the name is derived from the project slug with an -api suffix.
    /// </summary>
    [MinLength(3)]
    [MaxLength(60)]
    [RegularExpression(@"^[a-z0-9][a-z0-9-]*[a-z0-9]$",
        ErrorMessage = "App Service name must be lowercase alphanumeric and hyphens, with no leading or trailing hyphens.")]
    public string? AppServiceName { get; set; }
}
