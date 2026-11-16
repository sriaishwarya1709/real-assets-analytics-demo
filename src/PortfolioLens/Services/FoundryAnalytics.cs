using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PortfolioLens.Services;

public sealed class FoundryAnalytics(HttpClient http, Func<Task<string>> getToken, AnalyticsOptions options) : IAnalyticsService
{
    public const string Scope = "https://ai.azure.com/.default";

    public async Task<ChartResult> QueryAsync(string question, string chartType, CancellationToken cancellationToken)
    {
        var token = await getToken();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(150));
        var reference = new Dictionary<string, object>
        {
            ["type"] = "agent_reference", ["name"] = options.AgentName
        };
        if (!string.IsNullOrWhiteSpace(options.AgentVersion))
            reference["version"] = options.AgentVersion;
        var payload = new
        {
            agent_reference = reference,
            input = $"Question: {question}\nRequested chart type: {chartType}. Return only the chart JSON contract.",
            tool_choice = "required",
            background = options.Background,
            store = options.Background
        };
        using var initial = await SendAsync(HttpMethod.Post, "openai/v1/responses", payload, token, deadline.Token);
        var root = initial.RootElement.Clone();
        while (root.TryGetProperty("status", out var state) && state.GetString() is "queued" or "in_progress")
        {
            var id = root.GetProperty("id").GetString()
                ?? throw new ChartException("Foundry did not return a response ID.");
            await Task.Delay(TimeSpan.FromSeconds(2), deadline.Token);
            using var next = await SendAsync(HttpMethod.Get, $"openai/v1/responses/{Uri.EscapeDataString(id)}",
                null, token, deadline.Token);
            root = next.RootElement.Clone();
        }
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed")
            throw new ChartException("Foundry could not complete the query. Check Fabric permissions, connection consent, model compatibility and agent configuration.");
        if (!root.TryGetProperty("output", out var output))
            throw new ChartException("Foundry returned no chart output.");
        var text = string.Concat(output.EnumerateArray()
            .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "message")
            .TakeLast(1)
            .SelectMany(item => item.GetProperty("content").EnumerateArray())
            .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "output_text")
            .Select(item => item.GetProperty("text").GetString()));
        var result = ChartResult.Parse(text, options.FabricAgents.Select(a => a.Label));
        if (options.SyntheticData && !result.Warning.Contains("synthetic", StringComparison.OrdinalIgnoreCase))
        {
            result = result with { Warning = "SYNTHETIC DEMO DATA. " + result.Warning };
            result.Validate(options.FabricAgents.Select(a => a.Label));
        }
        return result;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{options.ProjectEndpoint.TrimEnd('/')}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Foundry returned HTTP {(int)response.StatusCode}.",
                null, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try { return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken); }
        catch (JsonException ex) { throw new ChartException("Foundry returned an invalid service response.", ex); }
    }
}
