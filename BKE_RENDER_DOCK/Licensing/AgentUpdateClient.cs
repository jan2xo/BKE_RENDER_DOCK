using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BKE_MediaTools.Licensing
{
    internal sealed class AgentUpdateClient : IDisposable
    {
        private static readonly Uri BaseUri = new("http://127.0.0.1:43873/", UriKind.Absolute);
        private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

        internal async Task<AgentUpdateStatus?> StatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var response = await _http.GetAsync(new Uri(BaseUri, "v1/updates/status?product_id=bke-render-dock"), timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var status = JsonSerializer.Deserialize<AgentUpdateStatus>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                return status?.ProductId == "bke-render-dock" ? status : null;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is JsonException) { return null; }
        }

        internal async Task QueueRefreshAsync(AgentUpdateStatus status, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(status.CurrentVersion)) return;
            await PostAsync("v1/updates/refresh", new { product_id = status.ProductId, version = status.CurrentVersion }, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }

        internal async Task<string> OpenCenterAsync(AgentUpdateStatus status, CancellationToken cancellationToken = default)
        {
            var correlation = Guid.NewGuid().ToString("N");
            var json = await PostAsync("v1/update-center/open", new { product_id = status.ProductId, version = status.CurrentVersion, correlation_id = correlation }, TimeSpan.FromMinutes(15), cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<UpdateCenterResponse>(json);
            return result != null && result.CorrelationId == correlation ? (string.IsNullOrWhiteSpace(result.Reason) ? result.Outcome : result.Reason) : "Invalid Update Center response.";
        }

        private async Task<string> PostAsync(string path, object body, TimeSpan duration, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(duration);
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(new Uri(BaseUri, path), content, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        public void Dispose() => _http.Dispose();
    }

    internal sealed class AgentUpdateStatus
    {
        [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
        [JsonPropertyName("product_id")] public string ProductId { get; set; } = string.Empty;
        [JsonPropertyName("current_version")] public string CurrentVersion { get; set; } = string.Empty;
        [JsonPropertyName("latest_version")] public string LatestVersion { get; set; } = string.Empty;
        internal bool Available => State == "update_available" || State == "stale_update";
    }

    internal sealed class UpdateCenterResponse
    {
        [JsonPropertyName("outcome")] public string Outcome { get; set; } = string.Empty;
        [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
        [JsonPropertyName("correlation_id")] public string CorrelationId { get; set; } = string.Empty;
    }
}

