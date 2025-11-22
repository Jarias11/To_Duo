using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using TaskMate.Services.Auth;

namespace TaskMate.Services.Auth {
    public interface IAuthService {
        string? IdToken { get; }
        string? RefreshToken { get; }
        string? Uid { get; }
        bool IsSignedIn { get; }

        Task<bool> TrySilentSignInAsync();
        Task SignInWithEmailAsync(string email, string password, string webApiKey, CancellationToken ct = default);
        Task SignUpWithEmailAsync(string email, string password, string webApiKey, CancellationToken ct = default);
        Task EnsureFreshIdTokenAsync(string webApiKey, CancellationToken ct = default);
        void SignOut();
    }

    /// <summary>
    /// Minimal Firebase Auth client (Email/Password + Refresh) for desktop apps.
    /// </summary>
    public sealed class AuthService : IAuthService {
        private readonly HttpClient _http;
        private static readonly JsonSerializerOptions _json = new() {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // ---- Concurrency gates for refresh coalescing ----
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private Task? _inflightRefresh;

        public string? IdToken { get; private set; }
        public string? RefreshToken { get; private set; }
        public string? Uid { get; private set; }
        public bool IsSignedIn => !string.IsNullOrEmpty(IdToken) && !string.IsNullOrEmpty(Uid);

        public AuthService(HttpClient? http = null) => _http = http ?? new HttpClient();

        public Task<bool> TrySilentSignInAsync() {
            var saved = TokenStore.Load();
            if(saved is null) return Task.FromResult(false);
            Uid = saved.Value.uid;
            RefreshToken = saved.Value.refreshToken;
            IdToken = saved.Value.idToken;
            return Task.FromResult(true);
        }

        public async Task SignInWithEmailAsync(string email, string password, string webApiKey, CancellationToken ct = default) {
            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={webApiKey}";
            var req = new PasswordSignInRequest { Email = email, Password = password, ReturnSecureToken = true };

            using var content = new StringContent(JsonSerializer.Serialize(req, _json), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
            var payload = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if(!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(ParseFirebaseError(payload));

            var auth = JsonSerializer.Deserialize<PasswordSignInResponse>(payload, _json)!;
            IdToken = auth.IdToken;
            RefreshToken = auth.RefreshToken;
            Uid = auth.LocalId;

            // Atomic save
            TokenStore.Save(Uid!, RefreshToken!, IdToken!);
        }

        public async Task SignUpWithEmailAsync(string email, string password, string webApiKey, CancellationToken ct = default) {
            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={webApiKey}";
            var req = new PasswordSignInRequest { Email = email, Password = password, ReturnSecureToken = true };

            using var content = new StringContent(JsonSerializer.Serialize(req, _json), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
            var payload = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if(!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(ParseFirebaseError(payload));

            var auth = JsonSerializer.Deserialize<PasswordSignInResponse>(payload, _json)!;
            IdToken = auth.IdToken;
            RefreshToken = auth.RefreshToken;
            Uid = auth.LocalId;

            // Atomic save
            TokenStore.Save(Uid!, RefreshToken!, IdToken!);
        }

        private static string ParseFirebaseError(string json) {
            try {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if(root.TryGetProperty("error", out var err)) {
                    var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                    return string.IsNullOrWhiteSpace(msg) ? json : msg!;
                }
                return json;
            }
            catch { return json; }
        }

        /// <summary>
        /// Renew the ID token using the refresh token. Coalesces concurrent callers.
        /// </summary>
        public async Task EnsureFreshIdTokenAsync(string webApiKey, CancellationToken ct = default) {
            if(string.IsNullOrEmpty(RefreshToken))
                throw new InvalidOperationException("No refresh token available.");

            // Coalesce concurrent refreshes
            await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
            try {
                if(_inflightRefresh == null) {
                    _inflightRefresh = RefreshAsync(webApiKey, ct);
                }
            }
            finally {
                _refreshGate.Release();
            }

            // Await the shared task
            try {
                await _inflightRefresh.ConfigureAwait(false);
            }
            finally {
                // Clear task so future refreshes can run
                await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
                try { _inflightRefresh = null; }
                finally { _refreshGate.Release(); }
            }
        }

        private async Task RefreshAsync(string webApiKey, CancellationToken ct) {
            var url = $"https://securetoken.googleapis.com/v1/token?key={webApiKey}";
            var body = $"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(RefreshToken!)}";

            using var content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
            using var resp = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
            var payload = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var tokens = JsonSerializer.Deserialize<RefreshTokenResponse>(payload, _json)!;
            IdToken = tokens.IdToken;
            RefreshToken = tokens.RefreshToken;
            Uid ??= tokens.UserId;

            // Atomic save
            TokenStore.Save(Uid!, RefreshToken!, IdToken!);
        }

        public void SignOut() {
            IdToken = null;
            RefreshToken = null;
            Uid = null;
            TokenStore.Clear();
        }
    }
}
