namespace LearnLuxembourgish.Data.Shared.Entities;

public class User
{
    public int Id { get; set; } // Internal database ID
    public Guid PublicId { get; set; } // Public-facing GUID for API
    public required string Email { get; set; }
    public required string Name { get; set; }
    public required string AuthProvider { get; set; } // "AzureAD", "Google"
    public required string ExternalUserId { get; set; } // Provider's user ID
    public string Role { get; set; } = "User";
    public string TimeZone { get; set; } = "UTC";
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;

    // Navigation properties
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

public class RefreshToken
{
    public required string Id { get; set; }
    public int UserId { get; set; } // FK to User.Id
    public required string TokenHash { get; set; } // SHA-256 hash of refresh token
    public string? DeviceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }

    public bool IsActive => RevokedAt == null && DateTime.UtcNow < ExpiresAt;

    // Navigation property
    public User? User { get; set; }
}
