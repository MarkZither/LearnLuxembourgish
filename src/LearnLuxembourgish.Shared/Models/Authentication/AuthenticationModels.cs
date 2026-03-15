using System.Security.Cryptography;

namespace LearnLuxembourgish.Shared.Models.Authentication;

public class AuthenticationResponse
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public required string TokenType { get; set; }
    public required int ExpiresIn { get; set; }
    public required UserInfo User { get; set; }
    public List<string> Permissions { get; set; } = new();
    public bool RequiresMfa { get; set; }
}

public class UserInfo
{
    public required Guid PublicId { get; set; }
    public required string Email { get; set; }
    public required string Name { get; set; }
    public required string Role { get; set; }
    public required string Provider { get; set; }
    public string TimeZone { get; set; } = "UTC";
    public DateTime LastLogin { get; set; }
}

public class AuthenticationConfig
{
    public JwtConfig Jwt { get; set; } = null!;
    public MedicalSecurityConfig MedicalSecurity { get; set; } = new();
}

public class JwtConfig
{
    public required string SecretKey { get; set; }
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}

public class MedicalSecurityConfig
{
    public bool RequireMfa { get; set; }
}
