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

1. Clone the repository
2. Configure API keys in `src/LearnLuxembourgish.Api/appsettings.Development.json`:
   ```json
   {
     "Translation": {
       "DeepL": { "ApiKey": "your-deepl-key" },
       "Mistral": { "ApiKey": "your-mistral-key" }
     },
     "Grammar": {
       "Mistral": { "ApiKey": "your-mistral-key" }
     }
   }
   ```
3. Run the API: `dotnet run --project src/LearnLuxembourgish.Api`
4. Run the Web app: `dotnet run --project src/LearnLuxembourgish.Web`

### Translation Fallback Chain

1. **DeepL** (`Translation:DeepL:ApiKey`) — highest quality, if key configured
2. **Mistral** (`Translation:Mistral:ApiKey`) — if DeepL unavailable
3. **Ollama** (`Translation:Ollama:Endpoint`, default `http://localhost:11434/v1`) — local fallback

### Database Configuration

Default: SQLite (`learnluxembourgish.db` in working directory)

For PostgreSQL:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=learnluxembourgish;Username=postgres;Password=..."
  }
}
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
