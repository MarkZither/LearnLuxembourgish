using LearnLuxembourgish.Data.Shared;
using LearnLuxembourgish.Data.Shared.Entities;
using LearnLuxembourgish.Shared.Models.Authentication;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace LearnLuxembourgish.Api.Services.Authentication;

public interface IAuthenticationService
{
    Task<AuthenticationResponse?> AuthenticateExternalAsync(string provider, string idToken, string? deviceId = null);
    Task<AuthenticationResponse?> RefreshTokenAsync(string refreshToken);
    Task<bool> RevokeTokenAsync(string refreshToken);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly LearnLuxembourgishDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IIdTokenValidationService _idTokenValidationService;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly AuthenticationConfig _authConfig;

    public AuthenticationService(
        LearnLuxembourgishDbContext context,
        IJwtTokenService jwtTokenService,
        IIdTokenValidationService idTokenValidationService,
        ILogger<AuthenticationService> logger,
        AuthenticationConfig authConfig)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _idTokenValidationService = idTokenValidationService;
        _logger = logger;
        _authConfig = authConfig;
    }

    public async Task<AuthenticationResponse?> AuthenticateExternalAsync(string provider, string idToken, string? deviceId = null)
    {
        try
        {
            // Validate the ID token from Azure AD or Google
            var principal = provider.ToLower() switch
            {
                "azuread" => await _idTokenValidationService.ValidateAzureAdTokenAsync(idToken),
                "google" => await _idTokenValidationService.ValidateGoogleTokenAsync(idToken),
                _ => null
            };

            if (principal == null)
            {
                _logger.LogWarning("ID token validation failed for provider {Provider}", provider);
                return null;
            }

            // Extract user info from claims - try multiple claim types
            var email = principal.FindFirst("email")?.Value 
                ?? principal.FindFirst("preferred_username")?.Value 
                ?? principal.FindFirst("upn")?.Value
                ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Upn)?.Value;

            var name = principal.FindFirst("name")?.Value 
                ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value 
                ?? email;

            var externalUserId = principal.FindFirst("sub")?.Value 
                ?? principal.FindFirst("oid")?.Value
                ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            _logger.LogInformation("Extracted claims - Email: {Email}, Name: {Name}, ExternalUserId: {ExternalUserId}", 
                email, name, externalUserId);

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(externalUserId))
            {
                _logger.LogWarning("Missing required claims in ID token. Email: {Email}, ExternalUserId: {ExternalUserId}", 
                    email ?? "NULL", externalUserId ?? "NULL");

                // Log all available claims for debugging
                var allClaims = string.Join(", ", principal.Claims.Select(c => $"{c.Type}={c.Value}"));
                _logger.LogDebug("All claims in token: {Claims}", allClaims);

                return null;
            }

            _logger.LogInformation("External authentication for {Email} via {Provider}", email, provider);

            // Find or create user
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ExternalUserId == externalUserId && u.AuthProvider == provider);

            bool isNewUser = false;
            if (user == null)
            {
                user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
                
                if (user != null)
                {
                    user.AuthProvider = provider;
                    user.ExternalUserId = externalUserId;
                }
                else
                {
                    user = new User
                    {
                        PublicId = Guid.NewGuid(),
                        Email = email,
                        Name = name,
                        AuthProvider = provider,
                        ExternalUserId = externalUserId,
                        Role = "User",
                        TimeZone = "UTC",
                        LastLoginAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        IsActive = true,
                        IsDeleted = false
                    };
                    _context.Users.Add(user);
                    isNewUser = true;
                }
            }
            else
            {
                user.LastLoginAt = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            // Generate tokens
            var userInfo = new UserInfo
            {
                PublicId = user.PublicId,
                Email = user.Email,
                Name = user.Name,
                Role = user.Role,
                Provider = user.AuthProvider,
                TimeZone = user.TimeZone,
                LastLogin = user.LastLoginAt ?? DateTime.UtcNow
            };

            var permissions = GetUserPermissions(user.Role);
            var accessToken = _jwtTokenService.GenerateAccessToken(userInfo, permissions);
            var refreshToken = _jwtTokenService.GenerateRefreshToken();

            await StoreRefreshTokenAsync(user.PublicId, refreshToken, deviceId);

            _logger.LogInformation("User {Email} authenticated successfully (New: {IsNew})", email, isNewUser);

            return new AuthenticationResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                TokenType = "Bearer",
                ExpiresIn = _authConfig.Jwt.AccessTokenExpirationMinutes * 60,
                User = userInfo,
                Permissions = permissions,
                RequiresMfa = _authConfig.MedicalSecurity.RequireMfa
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "External authentication failed");
            return null;
        }
    }

    public async Task<AuthenticationResponse?> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            var tokenHash = HashToken(refreshToken);
            var storedToken = await _context.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

            if (storedToken == null || !storedToken.IsActive)
            {
                _logger.LogWarning("Invalid or expired refresh token");
                return null;
            }

            var user = storedToken.User;
            if (user == null || !user.IsActive || user.IsDeleted)
            {
                _logger.LogWarning("User is inactive or deleted");
                return null;
            }

            user.LastLoginAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;

            var userInfo = new UserInfo
            {
                PublicId = user.PublicId,
                Email = user.Email,
                Name = user.Name,
                Role = user.Role,
                Provider = user.AuthProvider,
                TimeZone = user.TimeZone,
                LastLogin = user.LastLoginAt ?? DateTime.UtcNow
            };

            var permissions = GetUserPermissions(user.Role);
            var newAccessToken = _jwtTokenService.GenerateAccessToken(userInfo, permissions);
            var newRefreshToken = _jwtTokenService.GenerateRefreshToken();

            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.RevocationReason = "Replaced";
            await StoreRefreshTokenAsync(user.PublicId, newRefreshToken, storedToken.DeviceId);
            await _context.SaveChangesAsync();

            return new AuthenticationResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                TokenType = "Bearer",
                ExpiresIn = _authConfig.Jwt.AccessTokenExpirationMinutes * 60,
                User = userInfo,
                Permissions = permissions,
                RequiresMfa = _authConfig.MedicalSecurity.RequireMfa
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed");
            return null;
        }
    }

    public async Task<bool> RevokeTokenAsync(string refreshToken)
    {
        try
        {
            var tokenHash = HashToken(refreshToken);
            var storedToken = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

            if (storedToken == null) return false;

            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.RevocationReason = "User logout";
            await _context.SaveChangesAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token revocation failed");
            return false;
        }
    }

    private async Task StoreRefreshTokenAsync(Guid userPublicId, string refreshToken, string? deviceId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.PublicId == userPublicId);
        if (user == null) return;

        var tokenHash = HashToken(refreshToken);
        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            TokenHash = tokenHash,
            DeviceId = deviceId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(_authConfig.Jwt.RefreshTokenExpirationDays)
        };

        _context.RefreshTokens.Add(refreshTokenEntity);
    }

    private string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }

    private List<string> GetUserPermissions(string role)
    {
        return new List<string>
        {
            "translations:read",
            "translations:write",
            "flashcards:read",
            "flashcards:write"
        };
    }
}
