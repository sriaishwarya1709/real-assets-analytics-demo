using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;

namespace PortfolioLens.Services;

public static class AgentProvisioner
{
    public static JsonElement ReadSchema(string root) =>
        JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(root, "Agent", "chart.schema.json")));

    public static object CreateDefinition(AnalyticsOptions options, string root)
    {
        if (string.IsNullOrWhiteSpace(options.ModelDeployment))
            throw new InvalidOperationException("Analytics:ModelDeployment is required to provision the prompt agent.");
        foreach (var agent in options.FabricAgents)
        {
            if (!Uri.TryCreate(agent.ServerUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !(uri.Host == "api.fabric.microsoft.com" || uri.Host.EndsWith(".api.fabric.microsoft.com", StringComparison.OrdinalIgnoreCase)) ||
                !uri.AbsolutePath.StartsWith("/v1/mcp/", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(agent.ConnectionId))
                throw new InvalidOperationException($"Configure an HTTPS Fabric MCP URL and a project connection ID for '{agent.Label}'.");
        }
        var instructions = File.ReadAllText(Path.Combine(root, "Agent", "instructions.txt")) +
            "\nConnected Fabric data agents:\n" + string.Join("\n", options.FabricAgents.Select(a =>
                $"- {a.Label} ({a.Name}): {a.Description}"));
        var schema = JsonNode.Parse(ReadSchema(root).GetRawText())!;
        schema["properties"]!["sources"]!["items"]!["enum"] =
            new JsonArray(options.FabricAgents.Select(a => (JsonNode?)JsonValue.Create(a.Label)).ToArray());
        return new
        {
            definition = new
            {
                kind = "prompt", model = options.ModelDeployment, instructions,
                text = new { format = new { type = "json_schema", name = "chart_result", strict = true,
                    schema } },
                tools = options.FabricAgents.Select(a => new
                {
                    type = "fabric_iq_preview", server_label = a.Label,
                    server_url = a.ServerUrl, project_connection_id = a.ConnectionId,
                    require_approval = "never"
                }).ToArray()
            }
        };
    }

    public static async Task RunAsync(AnalyticsOptions options, string root)
    {
        if (options.Mode != "Live")
            throw new InvalidOperationException("Set Analytics:Mode to Live before provisioning.");
        var definition = CreateDefinition(options, root);
        var credential = new DefaultAzureCredential();
        var token = await credential.GetTokenAsync(new TokenRequestContext([FoundryAnalytics.Scope]));
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{options.ProjectEndpoint.TrimEnd('/')}/agents/{Uri.EscapeDataString(options.AgentName)}/versions?api-version=v1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Content = JsonContent.Create(definition);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Console.WriteLine($"Created agent '{options.AgentName}', version {result.RootElement.GetProperty("version")}. Pin Analytics:AgentVersion to this version.");
    }
}
