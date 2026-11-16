namespace PortfolioLens.Services;

public sealed class SampleAnalytics(AnalyticsOptions options) : IAnalyticsService
{
    public static FabricAgent[] DefaultAgents =>
    [
        new() { Label = "portfolio", Name = "Portfolio & valuations", Description = "Asset allocation, valuations and investment exposure." },
        new() { Label = "operations", Name = "Property operations", Description = "Occupancy and net operating income by sector." },
        new() { Label = "sustainability", Name = "Sustainability", Description = "Energy use and emissions across real assets." }
    ];

    public Task<ChartResult> QueryAsync(string question, string chartType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var q = question.ToLowerInvariant();
        var sectors = new[] { "Senior housing", "Student housing", "Healthcare", "Storage", "Digital infrastructure" };
        (string Title, string Unit, decimal[] Values, string Source) dataset = q switch
        {
            _ when q.Contains("occupan") => ("Occupancy by sector", "%", [94.2m, 96.5m, 92.1m, 89.8m, 97.3m], "operations"),
            _ when q.Contains("noi") || q.Contains("operating income") => ("Annual net operating income", "USD millions", [68.4m, 51.2m, 42.8m, 29.6m, 36.1m], "operations"),
            _ when q.Contains("energy") => ("Annual energy intensity by sector", "kWh / sq ft", [18.4m, 12.6m, 23.1m, 5.2m, 82.4m], "sustainability"),
            _ when q.Contains("emission") || q.Contains("carbon") => ("Annual operational emissions", "tCO2e", [4200m, 2800m, 5100m, 900m, 8600m], "sustainability"),
            _ when q.Contains("allocation") || q.Contains("aum") || q.Contains("valuation") || q.Contains("exposure") =>
                ("Portfolio allocation by sector", "USD millions", [1250m, 980m, 850m, 620m, 1100m], "portfolio"),
            _ => throw new ChartException("Sample mode supports allocation, occupancy, NOI, energy and emissions questions. Use a suggested question, or configure Live mode for open-ended Fabric queries.")
        };
        var (title, unit, values, source) = dataset;
        if (!options.FabricAgents.Any(a => a.Label == source))
            throw new ChartException($"Sample dataset '{source}' is not configured.");
        var type = chartType == "auto" ? (source == "portfolio" ? "pie" : "bar") : chartType;
        var result = new ChartResult(title,
            "Illustrative real assets portfolio across needs-based property sectors. These figures are synthetic and are not investment advice.",
            type, unit, sectors.Select((label, index) => new ChartPoint(label, values[index])).ToArray(),
            [source], "2026-09-30", "SYNTHETIC SAMPLE DATA - not connected to Microsoft Fabric.");
        result.Validate(options.FabricAgents.Select(a => a.Label));
        return Task.FromResult(result);
    }
}
