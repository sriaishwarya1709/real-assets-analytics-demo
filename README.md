# Portfolio Lens

A .NET 10 real assets analytics demo: ask a question, retrieve governed data through a Microsoft Foundry prompt agent connected to **multiple Fabric IQ data agents**, render a bar/pie chart, and download a vector PDF report. Generic branding only.

## What is implemented

- ASP.NET Core Razor Pages frontend and C# backend; responsive SVG charts, accessible data tables, chart switching, raw JSON and PDF downloads. No chart CDN, browser automation, paid PDF license or JavaScript application framework.
- Explicit **Sample** and **Live** modes. Sample mode uses five synthetic metrics across senior housing, student housing, healthcare, storage and digital infrastructure. It does not call Azure or support arbitrary questions.
- Live mode uses delegated Microsoft Entra sign-in and the Foundry Responses REST API, with a strict JSON schema and server-side validation. Errors never fall back to fabricated/sample results.
- C# prompt-agent provisioning with any configured number of Fabric IQ tools.
- Bicep for App Service, Key Vault, optional Foundry project/model and Fabric IQ connections.
- Focused executable C# checks, GitHub validation and manually triggered OIDC deployment workflows.

**Status:** The synthetic Sample-mode demo is deployed to Azure. Source is published in a private [GitHub repository](https://github.com/sriaishwarya1709/real-assets-analytics-demo). Live Fabric integration still requires the intended real estate data agents.

## Deployed demo

- **URL:** https://portfolio-lens-t5wn44xnzyq4w.azurewebsites.net
- **Access:** Microsoft Entra sign-in in the deployment tenant.
- **Mode:** Sample; all numerical results are synthetic, with no live Foundry/Fabric queries.
- **Resource group:** `rg-portfolio-lens-demo`.
- **Hosting:** App Service B1 in East US 2; Key Vault in Sweden Central, reached through private networking. Sweden Central App Service capacity was unavailable.
- **Verified:** successful code deployment, healthy `/health` endpoint, authorization-code/PKCE sign-in, resolved private Key Vault credential reference, and authenticated browser chart generation/switching, JSON export and PDF download.
- **Credential maintenance:** the dedicated web application credential expires 90 days after creation on November 16, 2026 UTC. Rotate it and update the Key Vault reference before expiry.

The resources incur Azure charges. This deployment does not establish a live connection to the organization's Fabric data.

## Architecture

```text
Browser -> .NET Razor Pages -> Foundry prompt agent -> Fabric IQ data agents
                                  |                    |- Portfolio & valuations
                                  |                    |- Property operations
                                  |                    `- Sustainability
                                  v
                         validated chart JSON
                                  |
                      SVG chart + table + PDF
```

In live mode the web app acquires a **user-delegated Foundry token**, not an application-only token. Foundry's `UserEntraToken` connections propagate the user context to Fabric. The web app's managed identity is used for Key Vault, not to bypass Fabric user permissions.

## Run locally

Prerequisite: .NET 10 SDK. If the workspace-local SDK is available, add it to this PowerShell session's PATH:

```powershell
$env:PATH = "$PWD\.tools\dotnet;$env:PATH"
dotnet run --project src\PortfolioLens --urls http://localhost:5180 --environment Development
```

Open `http://localhost:5180`. Try allocation, occupancy, NOI, energy or emissions; switch chart type and download a PDF. F5 uses the included Windows VS Code configuration and workspace-local SDK build task. If you have a system-wide SDK instead, change task `command` to `dotnet`. For live local sign-in, use an HTTPS development certificate/URL and register its `/signin-oidc` redirect.

Validation:

```powershell
dotnet build src\PortfolioLens\PortfolioLens.csproj -c Release
dotnet run --project tests\PortfolioLens.Tests -c Release
```

## Sample queries and demo walkthrough

### Ready to run in Sample mode

These queries work without Azure configuration. All values are synthetic, use a fixed sample reporting date, and cover the same five property sectors.

| Copy/paste query | Data domain | Auto chart | Unit |
| --- | --- | --- | --- |
| Show portfolio allocation by sector | Portfolio & valuations | Pie | USD millions |
| Compare occupancy across property sectors | Property operations | Bar | % |
| Show annual NOI by sector | Property operations | Bar | USD millions |
| Compare energy intensity across sectors | Sustainability | Bar | kWh / sq ft |
| Show annual operational emissions by sector | Sustainability | Bar | tCO2e |

Suggested walkthrough:

1. Run **Show portfolio allocation by sector** with **Auto visualization** selected.
2. Switch between **Pie** and **Bar** without another agent request.
3. Expand **View underlying data** and open **View JSON** to inspect the chart contract.
4. Select **Download PDF** to export the current chart, exact values, question, summary and source labels.
5. Run the occupancy and energy queries to show the other data domains.

Sample mode selects a fixed dataset using metric keywords. It does **not** apply arbitrary date filters, rank properties, join domains, or query Fabric. For example, adding "last quarter" does not change the sample reporting period. Use the exact examples above when presenting the synthetic demo.

### Live-mode examples

Use these only after connecting the relevant Fabric data agents and confirming that their underlying data includes the requested dimensions, metrics and reporting periods. Results depend on source data and the signed-in user's permissions.

- **Portfolio:** "Show portfolio valuation by property sector for the latest available reporting quarter. Return values in USD millions and include the reporting date."
- **Operations:** "Compare occupancy by property sector for the latest available month. Use a bar chart and percentages."
- **Operations:** "Show annual net operating income by property sector for the most recent complete fiscal year. Use a bar chart in USD millions."
- **Sustainability:** "Compare annual operational emissions by property sector for the latest complete reporting year. Use a bar chart in tCO2e."
- **Cross-agent:** "Using the portfolio and operations data agents, compare annual NOI yield by property sector for the same reporting period. Calculate NOI divided by valuation only where sector, period and currency match. Return a bar chart in percent and identify missing or incompatible data."
- **Cross-agent:** "Using the operations and sustainability data agents, compare energy intensity for sectors with occupancy below 95% in a compatible reporting period. Use a bar chart and report the source dates. Do not join incompatible records."

Replace domain names with your configured agent names if different. Cross-agent joins and calculated metrics must be validated against source data; a valid JSON response alone does not establish numerical correctness.

## Connect real Foundry and Fabric agents

The Fabric IQ integration is **preview**. The older `fabric_dataagent_preview` connector was scheduled for deprecation November 6, 2026; this demo uses `fabric_iq_preview`.

### 1. Prepare identity and data

1. Use a Foundry project with a deployed model supporting tool calling and structured outputs. Choose an available model/version and capacity for your region rather than assuming a deployment name exists.
2. Publish your Fabric data agents. Confirm each works directly in Fabric. Fabric capacity, licenses and tenant AI/cross-geo settings must meet the service prerequisites.
3. Keep Foundry and Fabric in the same tenant. Give each permitted user **Foundry User** on the project and read/execute access to the Fabric agents and their underlying data. Give the developer **Foundry Project Manager** to create connections.
4. Register a **single-tenant Entra web application**. Add the web redirect `https://<web-app>.azurewebsites.net/signin-oidc` (and your local HTTPS redirect when developing). Configure delegated Azure AI/Foundry `user_impersonation` permission for resource `https://ai.azure.com`, and obtain tenant admin consent as required by policy. Create a client credential and keep it out of source control.
5. Configure the Foundry `UserEntraToken` connection consent path for Fabric data agents. The downstream audience is `https://analysis.windows.net/powerbi/api`; the data-agent scope is `https://analysis.windows.net/powerbi/api/DataAgent.Execute.All`. The template relies on the documented default scope mapping for that target. Tenant policy may require an administrator to consent to Foundry's downstream delegated access as well as the web application's Foundry permission. These are separate consent boundaries.

This client does not implement in-conversation OAuth or approval-card flows. Use `UserEntraToken` connections with consent completed, not an `OAuth2` connection that requires a separate conversation sign-in.

### 2. Create/reuse Foundry infrastructure

Reuse your existing project if possible. To create a new project/model, deploy `infra/foundry.bicep` into the intended resource group, supplying `accountName`, `modelName`, `modelVersion`, `modelDeploymentName`, region/SKU/capacity and `foundryUserPrincipalIds`. This creates billable resources and requires model availability/quota. The template outputs the project endpoint.

Create one Fabric IQ connection per published data agent, either using the Foundry portal's **Fabric IQ (OneLake Catalog)** tool or by deploying `infra/fabric-connections.bicep` into the Foundry resource group:

```powershell
az login
az deployment group create --resource-group <foundry-resource-group> `
  --template-file infra\fabric-connections.bicep `
  --parameters accountName=<account> projectName=<project> fabricAgents=@<agents-json-file>
```

The external agents file is a JSON array, for example:

```json
[
  {
    "label": "portfolio",
    "serverUrl": "https://api.fabric.microsoft.com/v1/mcp/workspaces/<workspace-guid>/dataagents/<agent-guid>/agent"
  }
]
```

Repeat entries for operations, sustainability or your actual domains. For private-linked Fabric workspaces, use the workspace-specific Fabric host and compatible Foundry networking; the supplied templates use public endpoints and do not provision private networking.

### 3. Configure and provision the prompt agent

Create the ignored file `src/PortfolioLens/appsettings.Local.json`:

```json
{
  "Authentication": { "Enabled": true },
  "AzureAd": {
    "TenantId": "<tenant-guid>",
    "ClientId": "<web-app-registration-client-guid>"
  },
  "Analytics": {
    "Mode": "Live",
    "ProjectEndpoint": "https://<account>.services.ai.azure.com/api/projects/<project>",
    "ModelDeployment": "<existing-model-deployment-name>",
    "AgentName": "real-assets-analyst",
    "AgentVersion": "",
    "Background": false,
    "FabricAgents": [
      {
        "Label": "portfolio",
        "Name": "Portfolio & valuations",
        "Description": "Use for asset allocation, valuations and sector exposure.",
        "ServerUrl": "https://api.fabric.microsoft.com/v1/mcp/workspaces/<workspace-guid>/dataagents/<agent-guid>/agent",
        "ConnectionId": "/subscriptions/<subscription>/resourceGroups/<group>/providers/Microsoft.CognitiveServices/accounts/<account>/projects/<project>/connections/portfolio"
      }
    ]
  }
}
```

Add **all** desired agent entries, with unique lowercase labels and precise descriptions. The provisioner incorporates those descriptions into routing instructions and creates one Fabric IQ tool per entry. A cross-domain question can invoke multiple tools; join correctness remains dependent on your semantic models and instructions.

```powershell
# Provisioning uses your developer Azure credentials, not the web client's secret.
az login
dotnet run --project src\PortfolioLens -- --provision-agent
# Pin Analytics:AgentVersion to the printed version.

# Runtime uses the signed-in user and web application credential.
$env:AzureAd__ClientSecret = '<set locally from your approved secret store>'
dotnet run --project src\PortfolioLens --urls https://localhost:7180 --environment Development
```

Keep the prompt and chart schema in `src/PortfolioLens/Agent` under version control. Re-provision and pin a new agent version after changing them. You can also configure an existing portal-created prompt agent with the same prompt/tools/schema.

For long-running Fabric queries, set `Analytics:Background` to `true` **only with a compatible deployed model**. The client polls for up to 150 seconds. Background requests require stored responses, so `store` is enabled in that mode; otherwise it is disabled. A timed-out/disconnected client does not cancel the remote background run. Review Foundry retention, regional processing and costs before enabling it.

### Chart JSON contract

```json
{
  "title": "Portfolio allocation by sector",
  "summary": "Allocation across the source reporting period.",
  "chartType": "pie",
  "unit": "USD millions",
  "points": [{ "label": "Senior housing", "value": 1250 }],
  "sources": ["portfolio"],
  "asOf": "2026-09-30",
  "warning": ""
}
```

The server rejects malformed JSON, unknown source labels, duplicate labels, more than 20 points, extreme values and invalid pie values. Sources are **model-reported labels**, not independently verified citations. Rates should normally be bars, not parts-of-whole pies. Unanswerable/ungrounded queries produce an error rather than a chart.

## Deploy the .NET app to Azure

Prerequisites: authenticated Azure CLI, .NET SDK, a subscription/region/resource group you are authorized to use, an Entra app registration and permission to create role assignments. Tenant identity resources/admin consent are intentionally not provisioned by ARM Bicep.

The web template creates Linux App Service B1 (one instance), managed identity, RBAC-enabled Key Vault and a Key Vault reference for the web application's client secret. Key Vault public access is disabled; App Service reaches it through VNet integration, a private endpoint and private DNS. These networking resources incur additional charges. Authentication is enabled on Azure even in Sample mode. No Fabric credentials are stored in the browser or repository.

Sample-mode sign-in requests identity access only; it does not request Foundry API consent. Live mode additionally requests the delegated Foundry scope.

Forwarded headers are enabled for the Azure App Service managed reverse proxy so HTTPS sign-in redirects work. Do not copy that setting to an arbitrary host with an untrusted ingress. Follow your organization's execution policy when running PowerShell scripts; the underlying .NET and Azure CLI commands can also be run individually.

If App Service has no capacity in the selected region, set `AZURE_APP_SERVICE_LOCATION` to an approved alternative before deployment. This moves the App Service plan and web app only; Key Vault remains in `AZURE_LOCATION`. Review cross-region access requirements before using this option with live data.

Set these variables from your approved local secret manager, **not in a committed file**:

```powershell
$env:AZURE_TENANT_ID = '<tenant-guid>'
$env:AZURE_AD_CLIENT_ID = '<web-app-registration-client-guid>'
$env:AZURE_AD_CLIENT_SECRET = '<web-app-registration-secret>'
$env:ANALYTICS_MODE = 'Live' # or Sample for the synthetic demo
$env:FOUNDRY_PROJECT_ENDPOINT = 'https://<account>.services.ai.azure.com/api/projects/<project>'
$env:FOUNDRY_AGENT_VERSION = '<provisioned-version>'
# For Live mode: array of the SAME configured labels/names/descriptions used when provisioning.
$env:FABRIC_AGENTS_JSON = '[{"label":"portfolio","name":"Portfolio","description":"Asset allocation and valuations"}]'
.\scripts\deploy.ps1 -SubscriptionId '<subscription>' -ResourceGroup '<group>' -Location '<region>'
```

The script previews infrastructure, requests confirmation before provisioning, publishes the .NET application, uploads it and verifies health. Add the printed redirect URI to your Entra application before signing in. Key Vault RBAC propagation can take time; if a reference remains unresolved, verify the managed identity role and restart the app. Register the required resource providers if your subscription has not used App Service, Cognitive Services or Key Vault before.

**Required live acceptance:** sign in as an allowed user; query each Fabric agent and a cross-agent question; compare chart JSON with the original source; download/open the PDF; verify a user without the required Fabric permissions cannot retrieve results. Health checks verify web startup only, not agent/data correctness.

## Publish to GitHub

Install Git and GitHub CLI, run `gh auth login`, and choose your repository owner/name:

```powershell
.\scripts\publish-github.ps1 -Repository '<owner>/real-assets-analytics-demo'
```

The script stages only project paths, displays the staged summary, asks for confirmation, creates a **private** repository and pushes. Review for secrets before confirming. It refuses to modify an existing repository.

CI builds and runs checks and compiles Bicep. For subsequent code deployments, configure the GitHub **demo** environment with required reviewers, an Azure deployment identity federated to `repo:<owner>/<repo>:environment:demo`, and variables:

- `AZURE_DEPLOY_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
- `AZURE_WEBAPP_NAME`

Grant that deployment identity only the required web-app deployment scope. Run **Deploy existing Azure web app** manually after the initial Bicep deployment. This workflow deploys application code, not identity consent or the Fabric agents.

The published demo repository already has the **demo** environment, the four variables above, and a dedicated secretless Azure OIDC identity with **Website Contributor** scoped only to this web app. Environment deployments are restricted to `main`. An environment approval rule is not configured; add required reviewers if your GitHub plan supports them.

## Demo limitations and operations

- This is a demo, not a production-ready investment platform. Review privacy, regional processing, generated-query quality and investment disclaimers.
- Sessions and delegated token caches are in-memory. Use one App Service instance with affinity; restarts expire results/token caches and can require signing in again. Production needs distributed caches, shared Data Protection keys and scale-aware rate limiting.
- Queries are limited to 12 requests/minute per user/IP (including page/report requests). POSTs use ASP.NET antiforgery protection. Authentication/authorization are mandatory in Live mode.
- The PDF includes the chart, exact values, question, summary, source labels, dates and warnings. Its standard Helvetica font replaces non-ASCII characters with `?` and says so in the report. It is not a multilingual typesetting engine.
- No question text or result body is deliberately logged by application code. Foundry/Fabric have their own storage and processing policies. Background responses can outlive a browser request.
- The web frontend and Foundry/Fabric integrations use public service endpoints protected by identity. Key Vault uses a private endpoint and VNet integration. This is not a fully network-isolated production topology. Application deployment uses Azure Entra/OIDC; FTP is disabled.
- Azure resources remain billable until you deliberately remove the demo resources. Never delete a shared resource group to clean up this demo.

## Microsoft references

- [Fabric data agents through Fabric IQ, including multiple agents](https://learn.microsoft.com/en-us/fabric/data-science/data-agent-foundry-fabric-iq)
- [Foundry Fabric IQ tool, .NET and REST examples](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/tools/fabric-iq)
- [User delegation and delegated scopes](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/configure-user-delegation)
