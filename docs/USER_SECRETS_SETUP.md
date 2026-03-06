# User Secrets Setup Guide

This guide explains how to configure user secrets for the LearnLuxembourgish application.

## What are User Secrets?

User secrets are a secure way to store sensitive configuration data during development. They are stored **outside of your project directory** and are never checked into source control.

### Storage Location

- **Windows**: `%APPDATA%\Microsoft\UserSecrets\<user_secrets_id>\secrets.json`
- **Linux/macOS**: `~/.microsoft/usersecrets/<user_secrets_id>/secrets.json`

## Recommended Approach: Using Aspire AppHost

When using .NET Aspire (recommended), you configure secrets **once** in the AppHost project, and Aspire automatically injects them into the API and Web projects via environment variables.

### AppHost Project (LearnLuxembourgish.AppHost)

```bash
# Set Azure AD authentication (required if using auth)
dotnet user-secrets set "Parameters:azure-ad-tenant-id" "your-tenant-id" --project src/LearnLuxembourgish.AppHost
dotnet user-secrets set "Parameters:azure-ad-client-id" "your-client-id" --project src/LearnLuxembourgish.AppHost

# Set translation API keys (optional - falls back to Ollama if not set)
dotnet user-secrets set "Parameters:deepl-api-key" "your-deepl-key" --project src/LearnLuxembourgish.AppHost
dotnet user-secrets set "Parameters:mistral-api-key" "your-mistral-key" --project src/LearnLuxembourgish.AppHost
```

**Benefits of the Aspire approach:**
- ✅ Configure once, use everywhere
- ✅ Automatic service discovery (Web knows how to reach API)
- ✅ Dynamic CORS configuration
- ✅ Consistent configuration for local dev and cloud deployment
- ✅ Integrated dashboard for logs and telemetry

## Alternative: Individual Project Configuration

If you prefer to run projects individually without Aspire, configure secrets directly in each project:

### API Project (LearnLuxembourgish.Api)

```bash
# Set Azure AD authentication
dotnet user-secrets set "AzureAd:TenantId" "your-tenant-id" --project src/LearnLuxembourgish.Api
dotnet user-secrets set "AzureAd:ClientId" "your-api-client-id" --project src/LearnLuxembourgish.Api
dotnet user-secrets set "AzureAd:Domain" "your-tenant.onmicrosoft.com" --project src/LearnLuxembourgish.Api

# Set translation API keys (optional)
dotnet user-secrets set "Translation:DeepL:ApiKey" "your-deepl-key" --project src/LearnLuxembourgish.Api
dotnet user-secrets set "Translation:Mistral:ApiKey" "your-mistral-key" --project src/LearnLuxembourgish.Api

# Set grammar API key (optional - can reuse Mistral key)
dotnet user-secrets set "Grammar:Mistral:ApiKey" "your-mistral-key" --project src/LearnLuxembourgish.Api

# Set custom database connection (optional)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=learnluxembourgish;Username=postgres;Password=yourpassword" --project src/LearnLuxembourgish.Api
```

### Web Project (LearnLuxembourgish.Web)

```bash
# Set Azure AD authentication
dotnet user-secrets set "AzureAd:TenantId" "your-tenant-id" --project src/LearnLuxembourgish.Web
dotnet user-secrets set "AzureAd:ClientId" "your-web-client-id" --project src/LearnLuxembourgish.Web
dotnet user-secrets set "AzureAd:Domain" "your-tenant.onmicrosoft.com" --project src/LearnLuxembourgish.Web
```

## Managing Secrets

### List all secrets for a project
```bash
dotnet user-secrets list --project src/LearnLuxembourgish.Api
```

### Remove a specific secret
```bash
dotnet user-secrets remove "Translation:DeepL:ApiKey" --project src/LearnLuxembourgish.Api
```

### Clear all secrets for a project
```bash
dotnet user-secrets clear --project src/LearnLuxembourgish.Api
```

## Minimal Setup for Development

If you just want to get started quickly without external API keys:

1. **Install Ollama** (for local AI): https://ollama.ai
2. **Pull the llama3 model**: `ollama pull llama3`
3. **Skip all API keys** - the app will automatically fall back to Ollama

You can add authentication and API keys later as needed.

## Getting API Keys

### DeepL (Highest quality translation)
1. Sign up at https://www.deepl.com/pro-api
2. Free tier: 500,000 characters/month
3. Get your API key from the account page

### Mistral AI (Good quality translation + grammar)
1. Sign up at https://console.mistral.ai/
2. Free tier available with rate limits
3. Create an API key in the console

### Azure AD / Microsoft Entra (Authentication)
1. Go to https://portal.azure.com
2. Navigate to "Microsoft Entra ID" (formerly Azure AD)
3. Register a new application:
   - **Name**: LearnLuxembourgish
   - **Redirect URIs**: Add both:
     - `https://localhost:5001/signin-oidc` (Web)
     - `https://localhost:5050/signin-oidc` (API)
   - **API Permissions**: Add Microsoft Graph permissions if needed
   - Copy the **Application (client) ID** - this is your `azure-ad-client-id`
   - Copy the **Directory (tenant) ID** - this is your `azure-ad-tenant-id`

**Note**: This uses a single Azure AD app registration for both the API and Web projects. For production environments with stricter security requirements, consider using separate registrations.

## Security Best Practices

✅ **DO**:
- Use user secrets for all sensitive data (API keys, connection strings, client secrets)
- Keep different secrets for development, staging, and production environments
- Rotate API keys regularly
- Use Azure Key Vault for production secrets

❌ **DON'T**:
- Commit secrets to source control
- Share secrets via email or messaging apps
- Use production secrets in development
- Store secrets in appsettings.json or appsettings.Development.json files

## Troubleshooting

### "User secrets ID not found"
Make sure the project file has a `<UserSecretsId>` element:
```xml
<PropertyGroup>
  <UserSecretsId>learnluxembourgish-api-secrets</UserSecretsId>
</PropertyGroup>
```

### Secrets not loading
1. Check that you're setting secrets for the correct project
2. Verify the UserSecretsId matches between project file and secrets storage
3. Ensure you're using `--project` flag with the correct path
4. Restart your IDE/terminal after setting secrets

### Need to share configuration with team
Don't share actual secrets. Instead, share:
1. This documentation
2. The appsettings.json files (which show structure but not values)
3. Instructions on which secrets are required for which features

## More Information

- [Microsoft Docs: Safe storage of app secrets in development](https://learn.microsoft.com/aspnet/core/security/app-secrets)
- [Azure Key Vault for production](https://learn.microsoft.com/azure/key-vault/)
