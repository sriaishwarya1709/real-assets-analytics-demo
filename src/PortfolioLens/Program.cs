using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using PortfolioLens.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
var options = builder.Configuration.GetSection("Analytics").Get<AnalyticsOptions>() ?? new();
var agentsJson = builder.Configuration["Analytics:FabricAgentsJson"];
if (!string.IsNullOrWhiteSpace(agentsJson))
    options.FabricAgents = System.Text.Json.JsonSerializer.Deserialize<FabricAgent[]>(agentsJson,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("Analytics:FabricAgentsJson must be an array.");
if (options.Mode == "Sample" && options.FabricAgents.Length == 0)
    options.FabricAgents = SampleAnalytics.DefaultAgents;
options.Validate();
if (args.Contains("--provision-agent"))
{
    await AgentProvisioner.RunAsync(options, builder.Environment.ContentRootPath);
    return;
}

var authEnabled = builder.Configuration.GetValue<bool>("Authentication:Enabled");
if (options.Mode == "Live" && !authEnabled)
    throw new InvalidOperationException("Live mode requires Microsoft Entra authentication.");
builder.Services.AddSingleton(options);
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.Cookie.Name = "__PortfolioLens";
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.IdleTimeout = TimeSpan.FromMinutes(20);
});
if (authEnabled)
{
    var identity = builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
    builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme,
        o => o.ResponseType = "code");
    if (options.Mode == "Live")
        identity.EnableTokenAcquisitionToCallDownstreamApi([FoundryAnalytics.Scope])
            .AddInMemoryTokenCaches();
    builder.Services.AddAuthorization(o => o.FallbackPolicy =
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
}
else
{
    builder.Services.AddAuthentication();
    builder.Services.AddAuthorization();
}
builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("queries", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue("oid") ?? context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});
builder.Services.AddHttpClient("Foundry", client => client.Timeout = TimeSpan.FromSeconds(155));
builder.Services.AddScoped<IAnalyticsService>(services => options.Mode == "Sample"
    ? new SampleAnalytics(options)
    : new FoundryAnalytics(services.GetRequiredService<IHttpClientFactory>().CreateClient("Foundry"),
        () => services.GetRequiredService<ITokenAcquisition>().GetAccessTokenForUserAsync([FoundryAnalytics.Scope]), options));

var app = builder.Build();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'" +
        (authEnabled ? " https://login.microsoftonline.com" : "");
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseSession();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", mode = options.Mode })).AllowAnonymous();
app.MapRazorPages().RequireRateLimiting("queries");
app.Run();

public partial class Program;
