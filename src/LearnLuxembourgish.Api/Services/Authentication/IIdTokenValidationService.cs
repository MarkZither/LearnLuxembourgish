using System.Security.Claims;

namespace LearnLuxembourgish.Api.Services.Authentication;

public interface IIdTokenValidationService
{
    Task<ClaimsPrincipal?> ValidateAzureAdTokenAsync(string idToken);
    Task<ClaimsPrincipal?> ValidateGoogleTokenAsync(string idToken);
}
