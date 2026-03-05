var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database server
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

// Add SQLite database for API (local file-based)
var sqliteDb = builder.AddConnectionString("DefaultConnection", "Data Source=learnluxembourgish.db");

// Optional: Add Ollama for local AI (if running)
var ollamaEndpoint = builder.AddConnectionString("OllamaEndpoint", "http://localhost:11434/v1");

// Add parameters for sensitive configuration (prompts during 'azd up' deployment)
// For local development, these use user secrets in the API/Web projects
var deepLApiKey = builder.AddParameter("deepl-api-key", secret: true);
var mistralApiKey = builder.AddParameter("mistral-api-key", secret: true);
var azureAdTenantId = builder.AddParameter("azure-ad-tenant-id", secret: true);
var azureAdApiClientId = builder.AddParameter("azure-ad-api-client-id", secret: true);
var azureAdWebClientId = builder.AddParameter("azure-ad-web-client-id", secret: true);

// Add API project with database reference and configuration injection
var api = builder.AddProject<Projects.LearnLuxembourgish_Api>("api")
    .WithReference(sqliteDb)
    .WithEnvironment("Translation__DeepL__ApiKey", deepLApiKey)
    .WithEnvironment("Translation__Mistral__ApiKey", mistralApiKey)
    .WithEnvironment("Grammar__Mistral__ApiKey", mistralApiKey)
    .WithEnvironment("Translation__Ollama__Endpoint", ollamaEndpoint)
    .WithEnvironment("Grammar__Ollama__Endpoint", ollamaEndpoint)
    .WithEnvironment("AzureAd__TenantId", azureAdTenantId)
    .WithEnvironment("AzureAd__ClientId", azureAdApiClientId);

// Add Web frontend with reference to API and configuration injection
var web = builder.AddProject<Projects.LearnLuxembourgish_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WithEnvironment("AzureAd__TenantId", azureAdTenantId)
    .WithEnvironment("AzureAd__ClientId", azureAdWebClientId);

// Configure CORS AllowedOrigins dynamically based on Web's endpoint
api.WithEnvironment(context =>
{
    var webHttpEndpoint = web.GetEndpoint("http");
    var webHttpsEndpoint = web.GetEndpoint("https");

    context.EnvironmentVariables["AllowedOrigins__0"] = webHttpEndpoint.Property(EndpointProperty.Url);
    context.EnvironmentVariables["AllowedOrigins__1"] = webHttpsEndpoint.Property(EndpointProperty.Url);
});

builder.Build().Run();
