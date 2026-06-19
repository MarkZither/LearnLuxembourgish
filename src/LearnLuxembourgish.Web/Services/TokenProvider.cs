namespace LearnLuxembourgish.Web.Services;

/// <summary>
/// Stores the JWT token in server-side memory (scoped per Blazor circuit)
/// </summary>
public class TokenProvider
{
    private string? _token;
    
    public string? Token 
    { 
        get => _token;
        set => _token = value;
    }
    
    public bool HasToken => !string.IsNullOrEmpty(_token);
    
    public void ClearToken() => _token = null;
}
