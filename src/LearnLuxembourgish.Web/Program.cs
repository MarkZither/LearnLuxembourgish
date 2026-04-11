using LearnLuxembourgish.Web;
using LearnLuxembourgish.Web.Components;
using LearnLuxembourgish.Shared.Services;
using LearnLuxembourgish.Shared.Http;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add cascading authentication state for interactive components
builder.Services.AddCascadingAuthenticationState();

// Register server-side token storage (scoped = per Blazor circuit/SignalR connection)
builder.Services.AddScoped<TokenProvider>();

// Per-circuit user settings (grammar provider, Mistral API key from UI)
builder.Services.AddScoped<LearnLuxembourgish.Shared.Services.UserSettingsService>();

// HttpClient for API calls with Aspire service discovery (no auth handler - handled by ApiClient)
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri("http://api");
})
.AddServiceDiscovery();

// ApiClient is scoped and shares the same DI scope as TokenProvider (same Blazor circuit)
// This avoids the IHttpClientFactory handler scope isolation problem
builder.Services.AddScoped<ApiClient>(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("api");
    var tokenProvider = sp.GetRequiredService<TokenProvider>();
    var logger = sp.GetRequiredService<ILogger<ApiClient>>();
    return new ApiClient(httpClient, tokenProvider, logger);
});

// sproochmaschinn.lu TTS client (called from the web tier, not the API server)
builder.Services.AddHttpClient<ISproochmaschinnService, SproochmaschinnService>();

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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(LearnLuxembourgish.Shared.Components.Pages.Translate).Assembly);

app.Run();
