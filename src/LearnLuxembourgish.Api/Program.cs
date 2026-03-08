using LearnLuxembourgish.Api.Services;
using LearnLuxembourgish.Data.SQLite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add controllers
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Database - default to SQLite for development
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=learnluxembourgish.db";
builder.Services.AddSQLiteDataStore(connectionString);

// Authentication with EntraID (Microsoft Identity) - optional
var azureAdClientId = builder.Configuration["AzureAd:ClientId"];
var azureAdTenantId = builder.Configuration["AzureAd:TenantId"];

// Check if Azure AD is properly configured (not empty or placeholder values)
var isAzureAdConfigured = !string.IsNullOrEmpty(azureAdClientId) 
    && !string.IsNullOrEmpty(azureAdTenantId)
    && !azureAdClientId.Contains("<")
    && !azureAdTenantId.Contains("<");

if (isAzureAdConfigured)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

    builder.Services.AddAuthorization();
}

// HTTP clients
builder.Services.AddHttpClient("translation");
builder.Services.AddHttpClient("audio");

// Application services
builder.Services.AddScoped<ITranslationService, TranslationService>();
builder.Services.AddScoped<IAudioService, AudioService>();
builder.Services.AddScoped<IGrammarService, GrammarService>();

// CORS for Blazor frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["https://localhost:5001"])
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Apply EF migrations on startup in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<LearnLuxembourgish.Data.Shared.LearnLuxembourgishDbContext>();
    db.Database.EnsureCreated();
}

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();

// Only use authentication if it was configured
if (isAzureAdConfigured)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapControllers();

app.Run();
