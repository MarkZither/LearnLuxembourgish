var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database server
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

// Add SQLite database for API (local file-based)
// Using AddParameter with default value so it doesn't require user secrets
var sqliteDb = builder.AddParameter("default-connection", "Data Source=learnluxembourgish.db", secret: false);

// Optional: Add Ollama for local AI (if running)
// Using AddParameter with default value so it doesn't require user secrets
var ollamaEndpoint = builder.AddParameter("ollama-endpoint", "http://localhost:11434/v1", secret: false);

// Add parameters for sensitive configuration (prompts during 'azd up' deployment)
// For local development, these use user secrets in the API/Web projects
var deepLApiKey = builder.AddParameter("deepl-api-key", secret: true);
var mistralApiKey = builder.AddParameter("mistral-api-key", secret: true);
var groqApiKey = builder.AddParameter("groq-api-key", secret: true);

// Azure AD parameters are REQUIRED - app will not start without proper authentication
var azureAdTenantId = builder.AddParameter("azure-ad-tenant-id", secret: true);
var azureAdClientId = builder.AddParameter("azure-ad-client-id", secret: true);
var azureAdInstance = builder.AddParameter("azure-ad-instance", "https://login.microsoftonline.com/", secret: false);

// Add API project with database reference and configuration injection
var api = builder.AddProject<Projects.LearnLuxembourgish_Api>("api")
    .WithEnvironment("ConnectionStrings__DefaultConnection", sqliteDb)
    .WithEnvironment("Translation__DeepL__ApiKey", deepLApiKey)
    .WithEnvironment("Translation__Mistral__ApiKey", mistralApiKey)
    .WithEnvironment("Grammar__Mistral__ApiKey", mistralApiKey)
    .WithEnvironment("Grammar__Groq__ApiKey", groqApiKey)
    .WithEnvironment("Translation__Ollama__Endpoint", ollamaEndpoint)
    .WithEnvironment("Grammar__Ollama__Endpoint", ollamaEndpoint)
    .WithEnvironment("AzureAd__Instance", azureAdInstance)
    .WithEnvironment("AzureAd__TenantId", azureAdTenantId)
    .WithEnvironment("AzureAd__ClientId", azureAdClientId);

// Add Web frontend with reference to API and configuration injection
var web = builder.AddProject<Projects.LearnLuxembourgish_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WithEnvironment("AzureAd__Instance", azureAdInstance)
    .WithEnvironment("AzureAd__TenantId", azureAdTenantId)
    .WithEnvironment("AzureAd__ClientId", azureAdClientId);

// Configure CORS AllowedOrigins dynamically based on Web's endpoints.
// The https entry is only added when the web project actually exposes one
// (WithExternalHttpEndpoints reads launchSettings; some profiles are http-only).
api.WithEnvironment(context =>
{
    context.EnvironmentVariables["AllowedOrigins__0"] = web.GetEndpoint("http").Property(EndpointProperty.Url);

    var hasHttps = web.Resource.Annotations.OfType<EndpointAnnotation>().Any(a =>
        string.Equals(a.Name, "https", StringComparison.OrdinalIgnoreCase));

    if (hasHttps)
        context.EnvironmentVariables["AllowedOrigins__1"] = web.GetEndpoint("https").Property(EndpointProperty.Url);
});

builder.Build().Run();
