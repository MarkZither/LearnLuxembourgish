using LearnLuxembourgish.Web;
using LearnLuxembourgish.Web.Components;
using LearnLuxembourgish.Web.Handlers;
using LearnLuxembourgish.Shared.Services;
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

// Register server-side token storage (scoped = per Blazor circuit)
builder.Services.AddScoped<TokenProvider>();

// Register the auth header handler (scoped to access TokenProvider)
builder.Services.AddScoped<AuthHeaderHandler>();

// HttpClient for API calls with service discovery and auth header injection
builder.Services.AddHttpClient("api", client =>
{
    // BaseAddress will be resolved by Aspire service discovery to http://api
    client.BaseAddress = new Uri("http://api");
})
.AddServiceDiscovery() // Aspire service discovery
.AddHttpMessageHandler<AuthHeaderHandler>(); // Add auth header to all requests

// Default HttpClient uses the "api" named client
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("api"));

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
