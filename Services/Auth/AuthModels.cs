using System.Text.Json.Serialization;

namespace TaskMate.Services.Auth
{
    // === Requests ===
    public sealed class PasswordSignInRequest
    {
        [JsonPropertyName("email")] public string Email { get; init; } = "";
        [JsonPropertyName("password")] public string Password { get; init; } = "";
        [JsonPropertyName("returnSecureToken")] public bool ReturnSecureToken { get; init; } = true;
    }

    // === Responses (Identity Toolkit) ===
    public sealed class PasswordSignInResponse
    {
        // Firebase ID token (JWT) — short lived (~1h)
        [JsonPropertyName("idToken")] public string IdToken { get; init; } = "";
        // Firebase Refresh token — long lived
        [JsonPropertyName("refreshToken")] public string RefreshToken { get; init; } = "";
        // Firebase UID
        [JsonPropertyName("localId")] public string LocalId { get; init; } = "";
        [JsonPropertyName("email")] public string Email { get; init; } = "";
        // Seconds until expiry (optional)
        [JsonPropertyName("expiresIn")] public string? ExpiresIn { get; init; }
    }

    // === Responses (Secure Token: refresh) ===
    public sealed class RefreshTokenResponse
    {
        // New Firebase ID token (JWT)
        [JsonPropertyName("id_token")] public string IdToken { get; init; } = "";
        // Usually same refresh token, but treat as new
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; init; } = "";
        // Firebase UID
        [JsonPropertyName("user_id")] public string UserId { get; init; } = "";
        [JsonPropertyName("expires_in")] public string? ExpiresIn { get; init; }
        [JsonPropertyName("token_type")] public string? TokenType { get; init; }
        [JsonPropertyName("project_id")] public string? ProjectId { get; init; }
    }
}
