using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TaskMate.Services.Auth;

namespace TaskMate.Sync {
    /// <summary>
    /// Very small Firestore REST wrapper for CRUD + runQuery.
    /// Injects Firebase ID token and auto-refreshes on 401.
    /// </summary>
    public sealed class FirestoreRestClient {
        private readonly string _projectId;
        private readonly string _webApiKey;
        private readonly IAuthService _auth;
        private readonly HttpClient _http;
        private static readonly JsonSerializerOptions _json = new() {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,

            WriteIndented = false
        };

        private string BaseUrl =>
            $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)";

        public FirestoreRestClient(string projectId, string webApiKey, IAuthService auth, HttpClient? http = null) {
            _projectId = projectId;
            _webApiKey = webApiKey;
            _auth = auth;
            _http = http ?? new HttpClient();
        }

        // ---- Public helpers --------------------------------------------------

        /// <summary>Creates or replaces a document at a known path (PATCH semantics).</summary>
        public Task SetDocAsync(string docPath, object fields) =>
            SendJsonAsync(HttpMethod.Patch, WithKey($"{BaseUrl}/documents/{docPath}"), new { fields });

        /// <summary>Adds a document with a server-generated id under a collection path.</summary>
        public async Task<string> AddAsync(string collectionPath, object fields) {
            var url = WithKey($"{BaseUrl}/documents/{collectionPath}");
            using var res = await SendAsync(() => Build(HttpMethod.Post, url, new { fields })).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
            // "name" is full resource name: projects/{p}/databases/(default)/documents/{collection}/{id}
            return root.GetProperty("name").GetString()!;
        }

        /// <summary>Gets a document JSON string at a known path (throws if 404).</summary>
        public async Task<string> GetDocRawAsync(string docPath) {
            var url = WithKey($"{BaseUrl}/documents/{docPath}");
            using var res = await SendAsync(() => Build(HttpMethod.Get, url, body: null)).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        /// <summary>Deletes a document.</summary>
        public async Task DeleteDocAsync(string docPath) {
            var url = WithKey($"{BaseUrl}/documents/{docPath}");
            using var res = await SendAsync(() => Build(HttpMethod.Delete, url, body: null)).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
        }

        /// <summary>Run a structured query; returns each runQuery response object as JsonElement (one per line).</summary>
        public async Task<List<JsonElement>> RunQueryAsync(object structuredQuery) {
            var url = WithKey($"{BaseUrl}/documents:runQuery");
            using var res = await SendAsync(() => Build(HttpMethod.Post, url, new { structuredQuery })).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var payload = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

            // runQuery returns "JSON per line"
            var lines = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var result = new List<JsonElement>(lines.Length);
            foreach(var line in lines)
                result.Add(JsonDocument.Parse(line).RootElement);
            return result;
        }
        public async Task<List<JsonElement>> RunQueryAsync(string? parentPath, object structuredQuery) {
            var url = WithKey($"{BaseUrl}/documents:runQuery");

            // ✅ parent must be a RESOURCE NAME, not the HTTPS URL
            string? parentResource = parentPath is { Length: > 0 }
                ? $"projects/{_projectId}/databases/(default)/documents/{parentPath}"
                : null;

            object body = parentResource is not null
                ? new { parent = parentResource, structuredQuery }
                : new { structuredQuery };

            using var res = await SendAsync(() => Build(HttpMethod.Post, url, body)).ConfigureAwait(false);

            if(!res.IsSuccessStatusCode) {
                var err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                System.Diagnostics.Trace.WriteLine($"[FS] runQuery ERROR {(int)res.StatusCode} {res.ReasonPhrase}\nBody: {System.Text.Json.JsonSerializer.Serialize(body)}\nResp: {err}");
                res.EnsureSuccessStatusCode(); // throw after logging
            }

            var payload = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var lines = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var result = new List<JsonElement>(lines.Length);
            foreach(var line in lines)
                result.Add(JsonDocument.Parse(line).RootElement);
            return result;
        }
        public async Task<List<JsonElement>> ListDocsAsync(string collectionPath, string? orderBy = null, int? pageSize = null) {
            var url = WithKey($"{BaseUrl}/documents/{collectionPath}");
            var query = new List<string>();
            if(!string.IsNullOrEmpty(orderBy)) query.Add($"orderBy={Uri.EscapeDataString(orderBy)}");
            if(pageSize is int n) query.Add($"pageSize={n}");
            if(query.Count > 0) url += (url.Contains('?') ? "&" : "?") + string.Join("&", query);

            using var res = await SendAsync(() => Build(HttpMethod.Get, url, null)).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var root = JsonDocument.Parse(json).RootElement;

            var list = new List<JsonElement>();
            if(root.TryGetProperty("documents", out var docs))
                foreach(var d in docs.EnumerateArray()) list.Add(d);
            return list;
        }
        // ---- Low-level send w/ auto-refresh ---------------------------------

        private HttpRequestMessage Build(HttpMethod method, string url, object? body) {
            var req = new HttpRequestMessage(method, url);
            if(body is not null) {
                var json = JsonSerializer.Serialize(body, _json);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            return req;
        }

        private async Task SendJsonAsync(HttpMethod method, string url, object body) {
            using var res = await SendAsync(() => Build(method, url, body)).ConfigureAwait(false);
            if(!res.IsSuccessStatusCode) {
                var payload = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new HttpRequestException($"Firestore error {(int)res.StatusCode}: {res.ReasonPhrase}\n{payload}");
            }
        }

        private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build) {
            if(string.IsNullOrEmpty(_auth.IdToken))
                throw new InvalidOperationException("User is not signed in.");

            var req = build();
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.IdToken);

            var res = await _http.SendAsync(req).ConfigureAwait(false);
            if(res.StatusCode == System.Net.HttpStatusCode.Unauthorized) {
                // refresh ID token once and retry
                res.Dispose();
                await _auth.EnsureFreshIdTokenAsync(_webApiKey).ConfigureAwait(false);

                req = build();
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.IdToken);
                res = await _http.SendAsync(req).ConfigureAwait(false);
            }
            return res;
        }
        private string WithKey(string url)
    => url.Contains('?') ? $"{url}&key={_webApiKey}" : $"{url}?key={_webApiKey}";
    }

    // --- Firestore value helpers ---------------------------------------------
    // Use these to build the "fields" object quickly with correct types.
    public static class F {
        public static object Str(string v) => new { stringValue = v };
        public static object Bool(bool v) => new { booleanValue = v };
        public static object Int(long v) => new { integerValue = v.ToString() };
        public static object Double(double v) => new { doubleValue = v };
        public static object Ts(DateTime vUtc) => new { timestampValue = vUtc.ToUniversalTime().ToString("o") };
        public static object Null() => new { nullValue = (string?)null };
        public static object Map(object o) => new { mapValue = new { fields = o } };
        public static object Array(params object[] items) => new { arrayValue = new { values = items } };
    }
}
