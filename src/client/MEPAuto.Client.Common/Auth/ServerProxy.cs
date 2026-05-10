using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using MEPAuto.Contracts.Auth;
using Newtonsoft.Json;

namespace MEPAuto.Client.Common.Auth
{
    /// <summary>
    /// HttpClient wrapper. Inject Bearer token, auto-refresh khi 401, retry 1 lần.
    /// </summary>
    public class ServerProxy : IServerProxy
    {
        private readonly HttpClient _http;
        private readonly JwtCache _cache;
        private readonly string _baseUrl;

        public ServerProxy(string baseUrl, JwtCache cache, HttpClient? httpClient = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _cache = cache;
            _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        // ConfigureAwait(false) ở MỌI internal await — feature command thường gọi .GetAwaiter().GetResult()
        // trên Revit UI thread (IExternalCommand không async-friendly). Thiếu ConfigureAwait(false) → deadlock.

        public async Task<TResponse> Post<TResponse>(string path, object body) where TResponse : class
        {
            var resp = await Send(HttpMethod.Post, path, body).ConfigureAwait(false);
            return Deserialize<TResponse>(resp);
        }

        public async Task Post(string path, object body)
        {
            await Send(HttpMethod.Post, path, body).ConfigureAwait(false);
        }

        public async Task<TResponse> Get<TResponse>(string path) where TResponse : class
        {
            var resp = await Send(HttpMethod.Get, path, body: null).ConfigureAwait(false);
            return Deserialize<TResponse>(resp);
        }

        private async Task<string> Send(HttpMethod method, string path, object? body)
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                using var req = new HttpRequestMessage(method, _baseUrl + path);
                if (body != null)
                {
                    var json = JsonConvert.SerializeObject(body);
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }
                var token = _cache.Load();
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

                using var resp = await _http.SendAsync(req).ConfigureAwait(false);

                if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
                {
                    if (await TryRefresh().ConfigureAwait(false)) continue;
                    throw new SessionExpiredException();
                }

                var bodyStr = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw new ServerErrorException((int)resp.StatusCode, bodyStr);
                return bodyStr;
            }
            throw new InvalidOperationException("Unreachable");
        }

        private static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ServerErrorException(500, "Empty response body");
            return JsonConvert.DeserializeObject<T>(json)
                   ?? throw new ServerErrorException(500, "Failed to deserialize response");
        }

        private async Task<bool> TryRefresh()
        {
            var token = _cache.Load();
            if (token == null || string.IsNullOrEmpty(token.RefreshToken)) return false;
            try
            {
                var json = JsonConvert.SerializeObject(new RefreshRequest { RefreshToken = token.RefreshToken });
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(_baseUrl + "/api/v1/auth/refresh", content).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return false;
                var respJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var login = JsonConvert.DeserializeObject<LoginResponse>(respJson);
                if (login == null || string.IsNullOrEmpty(login.AccessToken)) return false;
                token.AccessToken = login.AccessToken;
                token.ExpiresAt = login.ExpiresAt;
                if (!string.IsNullOrEmpty(login.RefreshToken)) token.RefreshToken = login.RefreshToken;
                _cache.Save(token);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
