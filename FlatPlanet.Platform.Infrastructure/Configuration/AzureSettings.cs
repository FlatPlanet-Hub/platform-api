namespace FlatPlanet.Platform.Infrastructure.Configuration;

public sealed class AzureSettings
{
    public string SubscriptionId { get; init; } = string.Empty;
    public string ResourceGroupName { get; init; } = string.Empty;
    /// <summary>
    /// Full ARM resource ID format:
    /// /subscriptions/{subscriptionId}/resourceGroups/{rg}/providers/Microsoft.Web/serverfarms/{planName}
    /// </summary>
    public string AppServicePlanResourceId { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
}
