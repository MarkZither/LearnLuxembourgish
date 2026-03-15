using LearnLuxembourgish.Api.Services.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace LearnLuxembourgish.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthenticationService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("azure-ad")]
    public async Task<IActionResult> AzureAdCallback([FromBody] TokenRequest request)
    {
        if (string.IsNullOrEmpty(request.IdToken))
        {
            return BadRequest(new { error = "id_token is required" });
        }

        var result = await _authService.AuthenticateExternalAsync("AzureAD", request.IdToken, request.DeviceId);
        
        if (result == null)
        {
            return Unauthorized(new { error = "Authentication failed" });
        }

        return Ok(result);
    }

    [HttpPost("google")]
    public async Task<IActionResult> GoogleCallback([FromBody] TokenRequest request)
    {
        if (string.IsNullOrEmpty(request.IdToken))
        {
            return BadRequest(new { error = "id_token is required" });
        }

        var result = await _authService.AuthenticateExternalAsync("Google", request.IdToken, request.DeviceId);
        
        if (result == null)
        {
            return Unauthorized(new { error = "Authentication failed" });
        }

        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return BadRequest(new { error = "refresh_token is required" });
        }

        var result = await _authService.RefreshTokenAsync(request.RefreshToken);
        
        if (result == null)
        {
            return Unauthorized(new { error = "Token refresh failed" });
        }

        return Ok(result);
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] RefreshRequest request)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return BadRequest(new { error = "refresh_token is required" });
        }

        var success = await _authService.RevokeTokenAsync(request.RefreshToken);
        
        if (!success)
        {
            return BadRequest(new { error = "Token revocation failed" });
        }

        return Ok(new { message = "Token revoked successfully" });
    }
}

public record TokenRequest(string IdToken, string? DeviceId);
public record RefreshRequest(string RefreshToken);
