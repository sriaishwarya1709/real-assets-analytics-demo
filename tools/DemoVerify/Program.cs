using System.Globalization;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using PortfolioLens.Services;

if (args.Length != 4)
    throw new ArgumentException("Usage: DemoVerify <fabric-manifest> <project-endpoint> <agent-version> <demo-data-directory>");
using var manifest = JsonDocument.Parse(File.ReadAllText(args[0]));
var agents = JsonSerializer.Deserialize<FabricAgent[]>(manifest.RootElement.GetProperty("agents"),
    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
var options = new AnalyticsOptions
{
    Mode = "Live", SyntheticData = true, ProjectEndpoint = args[1], AgentVersion = args[2], FabricAgents = agents
};
options.Validate();
var credential = new AzureCliCredential();
using var trace = new ToolTrace(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
using var http = new HttpClient(trace) { Timeout = TimeSpan.FromSeconds(155) };
var analytics = new FoundryAnalytics(http, async () =>
    (await credential.GetTokenAsync(new TokenRequestContext([FoundryAnalytics.Scope]))).Token, options);

Dictionary<string, decimal> ReadMetric(string table, string column)
{
    var lines = File.ReadAllLines(Path.Combine(args[3], table + ".csv"));
    var header = lines[0].Split(',');
    var index = Array.IndexOf(header, column);
    if (index < 0) throw new InvalidOperationException($"Missing metric {column}.");
    return lines.Skip(1).Select(line => line.Split(',')).ToDictionary(row => row[0],
        row => decimal.Parse(row[index], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
}
var valuations = ReadMetric("portfolio_snapshot", "valuation_usd_millions");
var noi = ReadMetric("operations_snapshot", "annual_noi_usd_millions");
var cases = new[]
{
    new Verification("Show portfolio valuation by sector on 2026-09-30 in USD millions.", "pie",
        new[] { "portfolio" }, valuations),
    new Verification("Compare occupancy percent by sector on 2026-09-30.", "bar",
        new[] { "operations" }, ReadMetric("operations_snapshot", "occupancy_percent")),
    new Verification("Compare annual energy intensity in kWh per square foot by sector at 2026-09-30.", "bar",
        new[] { "sustainability" }, ReadMetric("sustainability_snapshot", "annual_energy_kwh_per_sq_ft")),
    new Verification("Use portfolio and operations agents to calculate annual NOI yield by sector: annual_noi_usd_millions divided by valuation_usd_millions times 100 at the common reporting date 2026-09-30. Round to two decimal places. Show all five sectors.", "bar",
        new[] { "portfolio", "operations" }, valuations.ToDictionary(kv => kv.Key,
            kv => Math.Round(noi[kv.Key] / kv.Value * 100, 2), StringComparer.OrdinalIgnoreCase))
};
foreach (var test in cases)
{
    trace.SuccessfulSources.Clear();
    var result = await analytics.QueryAsync(test.Question, test.ChartType, default);
    if (result.ChartType != test.ChartType || result.Points.Length != test.Expected.Count ||
        result.AsOf != "2026-09-30" || !result.Warning.Contains("synthetic", StringComparison.OrdinalIgnoreCase) ||
        !test.Sources.Order().SequenceEqual(result.Sources.Order()) ||
        test.Sources.Any(source => !trace.SuccessfulSources.Contains(source)))
        throw new InvalidOperationException($"Live result/provenance mismatch: {JsonSerializer.Serialize(result)}");
    foreach (var point in result.Points)
        if (!test.Expected.TryGetValue(point.Label, out var expected) || Math.Abs(point.Value - expected) > 0.005m)
            throw new InvalidOperationException($"Unexpected value for {point.Label}: {point.Value}.");
    var pdf = PdfReport.Create(result, test.Question, "Live");
    if (pdf.Length < 1000) throw new InvalidOperationException("Live PDF was not generated.");
    Console.WriteLine($"PASS: {string.Join(" + ", test.Sources)}; {result.ChartType}; {result.Points.Length} exact CSV-grounded points; actual Fabric tool calls; PDF {pdf.Length} bytes.");
}
Console.WriteLine("All four live integration checks passed.");

sealed record Verification(string Question, string ChartType, string[] Sources, Dictionary<string, decimal> Expected);

sealed class ToolTrace(string outputDirectory) : DelegatingHandler(new HttpClientHandler())
{
    private int requestNumber;
    public HashSet<string> SuccessfulSources { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"foundry-check-{++requestNumber}.json"), body, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Live Foundry HTTP {(int)response.StatusCode}: {body}", null, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        foreach (var item in document.RootElement.GetProperty("output").EnumerateArray())
            if (item.GetProperty("type").GetString() == "mcp_call" &&
                item.GetProperty("status").GetString() == "completed" &&
                (!item.TryGetProperty("error", out var error) || error.ValueKind == JsonValueKind.Null))
                SuccessfulSources.Add(item.GetProperty("server_label").GetString()!);
        return response;
    }
}
