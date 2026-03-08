using LearnLuxembourgish.Web.Components;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Authentication with EntraID (optional - only if configured)
var azureAdClientId = builder.Configuration["AzureAd:ClientId"];
var azureAdTenantId = builder.Configuration["AzureAd:TenantId"];

// Check if Azure AD is properly configured (not empty or placeholder values)
var isAzureAdConfigured = !string.IsNullOrEmpty(azureAdClientId) 
    && !string.IsNullOrEmpty(azureAdTenantId)
    && !azureAdClientId.Contains("<")
    && !azureAdTenantId.Contains("<");

if (isAzureAdConfigured)
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

    builder.Services.AddControllersWithViews()
        .AddMicrosoftIdentityUI();

    builder.Services.AddAuthorization();
}

// HTTP client for calling the API
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5050/");
});

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("api"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.MapDefaultEndpoints();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Only use authentication if it was configured
if (isAzureAdConfigured)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Only map MVC controllers if authentication is configured (needed for Microsoft.Identity.UI)
if (isAzureAdConfigured)
{
    app.MapControllers();
}

app.Run();
