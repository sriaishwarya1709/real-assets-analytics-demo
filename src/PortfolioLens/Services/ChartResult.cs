using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortfolioLens.Services;

public sealed record ChartPoint(string Label, decimal Value);
public sealed record ChartResult(
    string Title, string Summary, string ChartType, string Unit,
    ChartPoint[] Points, string[] Sources, string AsOf, string Warning)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public static ChartResult Parse(string json, IEnumerable<string> allowedSources)
    {
        ChartResult result;
        try
        {
            result = JsonSerializer.Deserialize<ChartResult>(json, JsonOptions)
                ?? throw new ChartException("The agent returned an empty answer.");
        }
        catch (JsonException ex)
        {
            throw new ChartException("The agent did not return the required chart JSON. Refine the question or check the agent instructions.", ex);
        }
        result.Validate(allowedSources);
        return result;
    }

    public void Validate(IEnumerable<string> allowedSources)
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 160 ||
            string.IsNullOrWhiteSpace(Summary) || Summary.Length > 2000 ||
            Unit is null || Unit.Length > 40 || Warning is null || Warning.Length > 1000 ||
            AsOf is null || AsOf.Length > 80 || ChartType is not ("bar" or "pie") ||
            Points is null || Points.Length is < 1 or > 20 ||
            Points.Any(p => p is null || string.IsNullOrWhiteSpace(p.Label) ||
                p.Label.Length > 80 || p.Value is < -1_000_000_000_000_000m or > 1_000_000_000_000_000m) ||
            Points.Select(p => p.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Points.Length ||
            Sources is null || Sources.Length == 0 ||
            Sources.Any(s => !allowedSources.Contains(s, StringComparer.Ordinal)))
            throw new ChartException("The agent returned an invalid chart or an unknown data source.");
        if (ChartType == "pie" && !CanUsePie)
            throw new ChartException("A pie chart needs non-negative values and a positive total. Request a bar chart instead.");
    }

    [JsonIgnore]
    public bool CanUsePie => Points.All(p => p.Value >= 0) && Points.Sum(p => p.Value) > 0;
}

public sealed class ChartException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class AnalyticsOptions
{
    public string Mode { get; set; } = "Sample";
    public string ProjectEndpoint { get; set; } = "";
    public string AgentName { get; set; } = "real-assets-analyst";
    public string AgentVersion { get; set; } = "";
    public string ModelDeployment { get; set; } = "";
    public bool Background { get; set; }
    public FabricAgent[] FabricAgents { get; set; } = [];

    public void Validate()
    {
        if (Mode is not ("Sample" or "Live"))
            throw new InvalidOperationException("Analytics:Mode must be Sample or Live.");
        if (FabricAgents.Length == 0 ||
            FabricAgents.Any(a => !System.Text.RegularExpressions.Regex.IsMatch(a.Label, "^[a-z][a-z0-9-]{0,39}$") ||
                string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.Description)) ||
            FabricAgents.Select(a => a.Label).Distinct().Count() != FabricAgents.Length)
            throw new InvalidOperationException("Configure unique Fabric agent labels, names and descriptions.");
        if (Mode == "Live" && (!Uri.TryCreate(ProjectEndpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || !uri.Host.EndsWith(".ai.azure.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith("/api/projects/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            string.IsNullOrWhiteSpace(AgentName)))
            throw new InvalidOperationException("Configure a valid HTTPS Foundry project endpoint and agent name.");
    }
}

public sealed class FabricAgent
{
    public string Label { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ServerUrl { get; set; } = "";
    public string ConnectionId { get; set; } = "";
}

public interface IAnalyticsService
{
    Task<ChartResult> QueryAsync(string question, string chartType, CancellationToken cancellationToken);
}
