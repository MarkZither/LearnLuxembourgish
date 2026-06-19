using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace LearnLuxembourgish.Api.Services.Authentication;

public class IdTokenValidationService : IIdTokenValidationService
{
    private readonly ILogger<IdTokenValidationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    public IdTokenValidationService(ILogger<IdTokenValidationService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _tokenHandler = new JwtSecurityTokenHandler();
    }

    public async Task<ClaimsPrincipal?> ValidateAzureAdTokenAsync(string idToken)
    {
        try
        {
            var tenantId = _configuration["AzureAd:TenantId"];
            var clientId = _configuration["AzureAd:ClientId"];
            
            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId))
            {
                _logger.LogError("Azure AD configuration missing");
                return null;
            }

            var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());

            var discoveryDocument = await configurationManager.GetConfigurationAsync();
            var signingKeys = discoveryDocument.SigningKeys;

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = $"https://login.microsoftonline.com/{tenantId}/v2.0",
                ValidateAudience = true,
                ValidAudience = clientId,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var principal = _tokenHandler.ValidateToken(idToken, validationParameters, out var validatedToken);
            _logger.LogInformation("Azure AD ID token validated successfully");
            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure AD ID token validation failed");
            return null;
        }
    }

    public async Task<ClaimsPrincipal?> ValidateGoogleTokenAsync(string idToken)
    {
        try
        {
            var clientId = _configuration["Authentication:Google:ClientId"];
            
            if (string.IsNullOrEmpty(clientId))
            {
                _logger.LogError("Google configuration missing");
                return null;
            }

            var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                "https://accounts.google.com/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());

            var discoveryDocument = await configurationManager.GetConfigurationAsync();
            var signingKeys = discoveryDocument.SigningKeys;

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
                ValidateAudience = true,
                ValidAudience = clientId,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var principal = _tokenHandler.ValidateToken(idToken, validationParameters, out var validatedToken);
            _logger.LogInformation("Google ID token validated successfully");
            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google ID token validation failed");
            return null;
        }
    }
}
