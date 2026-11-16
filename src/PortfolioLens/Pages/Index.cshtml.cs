using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Identity.Web;
using PortfolioLens.Services;

namespace PortfolioLens.Pages;

[AuthorizeForScopes(Scopes = [FoundryAnalytics.Scope])]
public sealed class IndexModel(IAnalyticsService analytics, AnalyticsOptions options, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty, Required, StringLength(1500, MinimumLength = 5)]
    public string Question { get; set; } = "";
    [BindProperty, RegularExpression("^(auto|bar|pie)$")]
    public string RequestedChart { get; set; } = "auto";
    public ChartResult? Chart { get; private set; }
    public AnalyticsOptions Options => options;
    public string? Error { get; private set; }
    public static string[] Suggestions =>
    [
        "Show portfolio allocation by sector",
        "Compare occupancy across property sectors",
        "Show annual NOI by sector",
        "Compare energy intensity across sectors"
    ];

    private string Owner => User.FindFirst("oid")?.Value ?? "local-sample";
    private void Save(ChartResult chart)
    {
        HttpContext.Session.SetString("chart", JsonSerializer.Serialize(chart, ChartResult.JsonOptions));
        HttpContext.Session.SetString("question", Question);
        HttpContext.Session.SetString("owner", Owner);
    }
    private void Load()
    {
        if (HttpContext.Session.GetString("owner") != Owner) return;
        var json = HttpContext.Session.GetString("chart");
        if (json is null) return;
        Chart = ChartResult.Parse(json, options.FabricAgents.Select(a => a.Label));
        Question = HttpContext.Session.GetString("question") ?? "";
    }
    public void OnGet() => Load();
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();
        try
        {
            Chart = await analytics.QueryAsync(Question.Trim(), RequestedChart, cancellationToken);
            Save(Chart);
        }
        catch (ChartException ex)
        {
            logger.LogWarning(ex, "Chart query was rejected");
            Error = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Foundry request failed with status {Status}", ex.StatusCode);
            Error = "The live data request failed. Check Foundry/Fabric permissions and connection configuration. No sample data has been substituted.";
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Foundry query timed out");
            Error = "The query exceeded 150 seconds. Narrow your question and try again. A background Foundry run may still be completing.";
        }
        return Page();
    }
    public IActionResult OnPostChart(string type)
    {
        Load();
        if (Chart is null) return RedirectToPage();
        if (type is not ("bar" or "pie") || (type == "pie" && !Chart.CanUsePie))
        {
            Error = "Pie charts require non-negative values and a positive total.";
            return Page();
        }
        Chart = Chart with { ChartType = type };
        Save(Chart);
        return RedirectToPage();
    }
    public IActionResult OnGetPdf()
    {
        Load();
        return Chart is null
            ? NotFound("Run a query before downloading a report.")
            : File(PdfReport.Create(Chart, Question, options.Mode), "application/pdf", "portfolio-analysis.pdf");
    }
    public IActionResult OnGetJson()
    {
        Load();
        return Chart is null ? NotFound() : new JsonResult(Chart, ChartResult.JsonOptions);
    }
}
