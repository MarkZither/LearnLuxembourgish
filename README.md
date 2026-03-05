# LearnLuxembourgish

Tools to help learn Luxembourgish — accepting English or Polish sentences and translating them idiomatically to Lëtzebuergesch (Luxembourgish).

## Features

- **Idiomatic Translation**: DeepL → Mistral → Ollama/OpenAI-compat fallback chain
- **Audio Pronunciation**: Generated via [sproochmaschinn.lu](https://sproochmaschinn.lu) TTS API
- **Grammar Explanation**: Mistral AI with Ollama fallback breaks down grammar for learners
- **Flash Cards**: Save translations with audio and grammar explanations; group into decks for review
- **Authentication**: EntraID (Microsoft Entra) with flexibility for other providers
- **Multi-Frontend**: Blazor Web + MAUI Blazor Hybrid (code reuse via shared component library)
- **Multi-Database**: SQLite (default/dev), SQL Server, PostgreSQL — one base project, one per provider

## Architecture

```
LearnLuxembourgish/
├── src/
│   ├── LearnLuxembourgish.Api            # ASP.NET Core WebAPI with Semantic Kernel
│   ├── LearnLuxembourgish.Web            # Blazor Web App
│   ├── LearnLuxembourgish.Mobile         # .NET MAUI Blazor Hybrid
│   ├── LearnLuxembourgish.Shared         # Shared Blazor components & models
│   ├── LearnLuxembourgish.ServiceDefaults # OpenTelemetry, health checks
│   ├── LearnLuxembourgish.Data.Shared    # EF Core entities, DbContext, interfaces
│   ├── LearnLuxembourgish.Data.SQLite    # SQLite EF Core provider
│   ├── LearnLuxembourgish.Data.SqlServer # SQL Server EF Core provider
│   └── LearnLuxembourgish.Data.PostgreSQL # PostgreSQL EF Core provider
└── tests/
    └── LearnLuxembourgish.Tests          # xUnit tests
```

## Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- One of: SQLite (default), SQL Server, or PostgreSQL
- Optional: DeepL API key, Mistral API key, or Ollama running locally

### Development Setup

1. **Clone the repository**

2. **Configure User Secrets**

   > 📚 **For detailed instructions**, see [User Secrets Setup Guide](docs/USER_SECRETS_SETUP.md)

   **Option A: Using Aspire AppHost (Recommended)**

   When running with Aspire, configure secrets in the AppHost project. Aspire will inject these into the API and Web projects automatically:

   ```bash
   # AppHost Parameters (Aspire injects these into child projects)
   dotnet user-secrets set "Parameters:azure-ad-tenant-id" "your-tenant-id" --project src/LearnLuxembourgish.AppHost
   dotnet user-secrets set "Parameters:azure-ad-api-client-id" "your-api-client-id" --project src/LearnLuxembourgish.AppHost
   dotnet user-secrets set "Parameters:azure-ad-web-client-id" "your-web-client-id" --project src/LearnLuxembourgish.AppHost

   # Optional: API keys (leave empty to use Ollama)
   dotnet user-secrets set "Parameters:deepl-api-key" "your-deepl-key" --project src/LearnLuxembourgish.AppHost
   dotnet user-secrets set "Parameters:mistral-api-key" "your-mistral-key" --project src/LearnLuxembourgish.AppHost
   ```

   **Option B: Running Projects Individually**

   If running API and Web separately (without Aspire), configure secrets directly in each project:

   <details>
   <summary>Click to expand individual project configuration</summary>

   #### API Project Secrets
   ```bash
   # Set Azure AD authentication (required for auth)
   dotnet user-secrets set "AzureAd:TenantId" "your-tenant-id" --project src/LearnLuxembourgish.Api
   dotnet user-secrets set "AzureAd:ClientId" "your-api-client-id" --project src/LearnLuxembourgish.Api
   dotnet user-secrets set "AzureAd:Domain" "your-tenant.onmicrosoft.com" --project src/LearnLuxembourgish.Api

   # Optional: Translation API keys
   dotnet user-secrets set "Translation:DeepL:ApiKey" "your-deepl-key" --project src/LearnLuxembourgish.Api
   dotnet user-secrets set "Translation:Mistral:ApiKey" "your-mistral-key" --project src/LearnLuxembourgish.Api
   dotnet user-secrets set "Grammar:Mistral:ApiKey" "your-mistral-key" --project src/LearnLuxembourgish.Api
   ```

   #### Web Project Secrets
   ```bash
   # Set Azure AD authentication
   dotnet user-secrets set "AzureAd:TenantId" "your-tenant-id" --project src/LearnLuxembourgish.Web
   dotnet user-secrets set "AzureAd:ClientId" "your-web-client-id" --project src/LearnLuxembourgish.Web
   dotnet user-secrets set "AzureAd:Domain" "your-tenant.onmicrosoft.com" --project src/LearnLuxembourgish.Web
   ```
   </details>

   > **Note**: User secrets are stored locally and never checked into source control. Aspire automatically injects AppHost parameters into child projects via environment variables.

3. **Run with Aspire (Recommended)**
   ```bash
   dotnet run --project src/LearnLuxembourgish.AppHost
   ```

   **What Aspire provides:**
   - ✅ Automatic service discovery (Web → API communication)
   - ✅ Configuration injection from AppHost to child projects
   - ✅ Dynamic CORS configuration based on Web's endpoint
   - ✅ PostgreSQL with pgAdmin UI
   - ✅ Integrated Aspire Dashboard for telemetry and logs
   - ✅ Simplified deployment with `azd` (Azure Developer CLI)

4. **Or run projects individually** (without Aspire)
   ```bash
   # Terminal 1 - API
   dotnet run --project src/LearnLuxembourgish.Api

   # Terminal 2 - Web
   dotnet run --project src/LearnLuxembourgish.Web
   ```

   > ⚠️ When running individually, you must manually configure each project's secrets and ensure the Web project's `ApiBaseUrl` points to the correct API endpoint.

### Translation Fallback Chain

1. **DeepL** (`Translation:DeepL:ApiKey`) — highest quality, if key configured
2. **Mistral** (`Translation:Mistral:ApiKey`) — if DeepL unavailable
3. **Ollama** (`Translation:Ollama:Endpoint`, default `http://localhost:11434/v1`) — local fallback

### Required vs Optional Secrets

| Secret | Required? | Purpose | Default/Fallback |
|--------|-----------|---------|------------------|
| `AzureAd:TenantId` | ⚠️ If using auth | Microsoft Entra authentication | Anonymous access |
| `AzureAd:ClientId` | ⚠️ If using auth | Microsoft Entra authentication | Anonymous access |
| `AzureAd:Domain` | ⚠️ If using auth | Microsoft Entra authentication | Anonymous access |
| `Translation:DeepL:ApiKey` | ❌ Optional | Highest quality translation | Falls back to Mistral/Ollama |
| `Translation:Mistral:ApiKey` | ❌ Optional | Good quality translation | Falls back to Ollama |
| `Grammar:Mistral:ApiKey` | ❌ Optional | Grammar explanations | Falls back to Ollama |
| `ConnectionStrings:DefaultConnection` | ❌ Optional | Database connection | SQLite (`learnluxembourgish.db`) |

> **💡 Tip**: For local development without API keys, just run [Ollama](https://ollama.ai) locally with `llama3` model. The app will work fully offline!

### Database Configuration

Default: SQLite (`learnluxembourgish.db` in working directory)

For PostgreSQL:
```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=learnluxembourgish;Username=postgres;Password=yourpassword" --project src/LearnLuxembourgish.Api
```

And register `AddPostgreSQLDataStore` in `Program.cs` instead of `AddSQLiteDataStore`.

### Running Tests

```bash
dotnet test tests/LearnLuxembourgish.Tests
```

## Stretch Goal: Pronunciation Scoring

Future feature to compare user speech waveform to generated TTS audio, potentially using:
- Azure Cognitive Services Speech
- Mistral audio capabilities
- Android-native audio features in the mobile app

## WSL + Podman Development

This project is developed in WSL with Podman. A `podman-compose` setup for local development services (PostgreSQL, etc.) will be added.
