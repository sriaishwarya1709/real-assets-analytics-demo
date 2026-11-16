using System.Net;
using System.Text.Json;
using PortfolioLens.Services;
using UglyToad.PdfPig;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (ChartException) { Check(true, name); return; }
    throw new Exception("FAIL: accepted " + name);
}
var options = new AnalyticsOptions { FabricAgents = SampleAnalytics.DefaultAgents };
var service = new SampleAnalytics(options);
var allocation = await service.QueryAsync("Show portfolio allocation by sector", "auto", default);
Check(allocation.ChartType == "pie" && allocation.Points.Sum(p => p.Value) == 4800, "Sample allocation has consistent additive values");
var occupancy = await service.QueryAsync("Compare occupancy by sector", "auto", default);
Check(occupancy.ChartType == "bar" && occupancy.Sources.SequenceEqual(["operations"]), "Occupancy routes to operations and defaults to bar");
var energy = await service.QueryAsync("Compare energy use", "bar", default);
Check(energy.Sources.SequenceEqual(["sustainability"]), "Energy routes to sustainability");
try
{
    await service.QueryAsync("What is the weather?", "auto", default);
    throw new Exception("Unsupported sample question unexpectedly succeeded");
}
catch (ChartException) { Check(true, "Unsupported sample question fails explicitly"); }

var json = JsonSerializer.Serialize(allocation, ChartResult.JsonOptions);
Check(ChartResult.Parse(json, ["portfolio"]).Points.Length == 5, "Chart JSON round trips");
Reject(() => ChartResult.Parse(json.Replace("\"pie\"", "\"scatter\""), ["portfolio"]), "Unsupported chart type");
Reject(() => ChartResult.Parse(json, ["operations"]), "Unknown source");
Reject(() => ChartResult.Parse("{}", ["portfolio"]), "Missing required JSON fields");
Reject(() => ChartResult.Parse(json.Replace("\"title\":", "\"unexpected\":1,\"title\":"), ["portfolio"]), "Unexpected JSON fields");
Reject(() => (allocation with { Points = [] }).Validate(["portfolio"]), "Empty chart");
Reject(() => (allocation with { Points = [new("A", -1)] }).Validate(["portfolio"]), "Negative pie value");
Reject(() => (allocation with { Points = [new("A", 0)] }).Validate(["portfolio"]), "Zero-total pie");
Reject(() => (allocation with { Points = [new("A", 1), new("a", 2)] }).Validate(["portfolio"]), "Duplicate category labels");
Reject(() => (allocation with { Points = [new("A", decimal.MaxValue)] }).Validate(["portfolio"]), "Out-of-range value");
Reject(() => (allocation with { ChartType = "bar", Points = [new("A", decimal.MinValue)] }).Validate(["portfolio"]), "Minimum decimal is rejected without arithmetic overflow");
Reject(() => (allocation with { Sources = null! }).Validate(["portfolio"]), "Null source list");
var negative = allocation with { ChartType = "bar", Points = [new("Loss", -12), new("Gain", 8)] };
negative.Validate(["portfolio"]);
var (min, span) = ChartDrawing.Range(negative);
Check(min == -12 && span == 20 && !negative.CanUsePie, "Signed bar range preserves losses and gains");
Check(!ChartDrawing.PiePath(0, 1).Contains("NaN"), "A full-circle pie has finite arc geometry");
var zero = negative with { Points = [new("Zero", 0)] };
Check(ChartDrawing.Range(zero).Span == 1, "All-zero bars avoid division by zero");

var live = new AnalyticsOptions
{
    Mode = "Live", ProjectEndpoint = "https://example.services.ai.azure.com/api/projects/real-assets",
    ModelDeployment = "my-deployment",
    FabricAgents = SampleAnalytics.DefaultAgents
};
foreach (var agent in live.FabricAgents)
{
    agent.ServerUrl = $"https://api.fabric.microsoft.com/v1/mcp/workspaces/00000000-0000-0000-0000-000000000001/dataagents/00000000-0000-0000-0000-000000000002/agent";
    agent.ConnectionId = $"/subscriptions/example/resourceGroups/demo/providers/Microsoft.CognitiveServices/accounts/example/projects/real-assets/connections/{agent.Label}";
}
live.Validate();
var definition = JsonSerializer.SerializeToElement(AgentProvisioner.CreateDefinition(live, AppContext.BaseDirectory));
Check(definition.GetProperty("definition").GetProperty("tools").GetArrayLength() == 3, "Prompt agent provisions every configured Fabric IQ tool");
Check(definition.GetProperty("definition").GetProperty("tools")[0].GetProperty("type").GetString() == "fabric_iq_preview", "Uses Fabric IQ, not the deprecated connector");
Check(definition.GetProperty("definition").GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean(), "Strict chart JSON is configured on the prompt agent");
Check(definition.GetProperty("definition").GetProperty("text").GetProperty("format").GetProperty("schema")
    .GetProperty("properties").GetProperty("sources").GetProperty("items").GetProperty("enum")
    .EnumerateArray().Select(e => e.GetString()).SequenceEqual(live.FabricAgents.Select(a => a.Label)),
    "Prompt schema constrains sources to exact configured labels, not MCP-prefixed tool names");

foreach (var chart in new[] { allocation, negative, zero, allocation with { Summary = new string('s', 2000), Warning = new string('w', 1000), Points = Enumerable.Range(1, 20).Select(i => new ChartPoint(new string('x', 75) + i, i)).ToArray() } })
{
    var pdf = PdfReport.Create(chart, "Allocation (USD) \\ report", "Sample");
    using var document = PdfDocument.Open(pdf);
    Check(document.NumberOfPages >= 2, "Independent parser reads the chart and data pages");
    var text = string.Concat(document.GetPages().Select(p => p.Text));
    Check(text.Contains("Allocation (USD) \\ report"), "PDF preserves question punctuation");
    Check(text.Contains("SYNTHETIC SAMPLE DATA"), "PDF preserves sample-data disclosure");
    Check(text.Contains(chart.Points[^1].Label), "PDF preserves the last full category label, including long reports");
}

string Response(string text, string? intermediate = null) => JsonSerializer.Serialize(new
{
    id = "resp_test", status = "completed",
    output = new[] { intermediate, text }.Where(t => t is not null)
        .Select(t => new { type = "message", content = new[] { new { type = "output_text", text = t } } })
});
var calls = 0;
using var handler = new StubHandler(async request =>
{
    calls++;
    Check(request.Headers.Authorization?.Parameter == "delegated-user-token", "Foundry request uses the user token");
    Check(request.RequestUri!.AbsolutePath.EndsWith("/openai/v1/responses"), "Uses Foundry project Responses endpoint");
    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
    Check(body.RootElement.GetProperty("tool_choice").GetString() == "required", "Live query requires a data tool");
    Check(!body.RootElement.GetProperty("store").GetBoolean(), "Synchronous query disables response storage");
    Check(!body.RootElement.TryGetProperty("text", out _), "Agent-reference requests omit the forbidden text override");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Response(json, "{\"not\":\"the final answer\"}")) };
});
using var client = new HttpClient(handler);
var foundry = new FoundryAnalytics(client, () => Task.FromResult("delegated-user-token"), live);
var answer = await foundry.QueryAsync("What is our allocation?", "pie", default);
Check(calls == 1 && answer.Title == allocation.Title, "Live adapter uses the final message, not concatenated intermediate answers");
live.SyntheticData = true;
using var disclosureHandler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StringContent(Response(JsonSerializer.Serialize(allocation with { Warning = "" }, ChartResult.JsonOptions)))
}));
using var disclosureClient = new HttpClient(disclosureHandler);
var disclosed = await new FoundryAnalytics(disclosureClient, () => Task.FromResult("token"), live)
    .QueryAsync("What is our allocation?", "pie", default);
Check(disclosed.Warning.Contains("SYNTHETIC DEMO DATA"), "Live synthetic data remains disclosed even when the model omits its warning");
live.SyntheticData = false;
using var failureHandler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
using var failureClient = new HttpClient(failureHandler);
try
{
    await new FoundryAnalytics(failureClient, () => Task.FromResult("token"), live).QueryAsync("Question", "bar", default);
    throw new Exception("Foundry authorization failure unexpectedly succeeded");
}
catch (HttpRequestException ex) { Check(ex.StatusCode == HttpStatusCode.Forbidden, "Foundry authorization failure is surfaced, not replaced with sample data"); }

using var malformedHandler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StringContent(Response("{\"not\":\"a chart\"}"))
}));
using var malformedClient = new HttpClient(malformedHandler);
try
{
    await new FoundryAnalytics(malformedClient, () => Task.FromResult("token"), live).QueryAsync("Question", "bar", default);
    throw new Exception("Malformed live chart unexpectedly succeeded");
}
catch (ChartException) { Check(true, "Malformed live chart fails validation without a sample fallback"); }

live.Background = true;
var pollCalls = 0;
using var backgroundHandler = new StubHandler(async request =>
{
    pollCalls++;
    if (request.Method == HttpMethod.Post)
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        Check(body.RootElement.GetProperty("store").GetBoolean() &&
            body.RootElement.GetProperty("background").GetBoolean(), "Background mode explicitly enables stored responses");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"id\":\"resp_background\",\"status\":\"queued\"}")
        };
    }
    Check(request.RequestUri!.AbsolutePath.EndsWith("/responses/resp_background"), "Polls the specific Foundry response");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Response(json)) };
});
using var backgroundClient = new HttpClient(backgroundHandler);
var background = await new FoundryAnalytics(backgroundClient, () => Task.FromResult("token"), live)
    .QueryAsync("Question", "auto", default);
Check(pollCalls == 2 && background.Points.Length == 5, "Background query waits for the completed chart");

Console.WriteLine($"All {passed} checks passed.");

sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
}
