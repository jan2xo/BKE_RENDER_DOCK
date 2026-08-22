using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BKE_MediaTools.Licensing
{
    internal sealed class AgentClient : IDisposable
    {
        private static readonly Uri AuthorizationEndpoint =
            new Uri("http://127.0.0.1:43873/v1/authorize", UriKind.Absolute);

        private readonly HttpClient _httpClient;

        internal AgentClient()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
        }

        internal async Task<AuthorizationResult> AuthorizeAsync(
            CancellationToken cancellationToken = default)
        {
            ProductManifest manifest;
            string installationId;
            try
            {
                manifest = LoadManifest();
                installationId = InstallationIdentity.GetOrCreate();
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is JsonException ||
                ex is InvalidDataException)
            {
                return new AuthorizationResult(
                    AuthorizationStatus.InvalidResponse,
                    "Render Dock product or installation identity is missing or invalid.");
            }

            var request = new AuthorizationRequest
            {
                ProductId = manifest.ProductId,
                Version = manifest.Version,
                InstallationId = installationId
            };

            try
            {
                var json = JsonSerializer.Serialize(request);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(
                    AuthorizationEndpoint,
                    content,
                    cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return new AuthorizationResult(
                        AuthorizationStatus.InvalidResponse,
                        "The Licensing Agent returned an invalid authorization response.");
                }

                var responseJson = await response.Content.ReadAsStringAsync()
                    .ConfigureAwait(false);
                var decision = JsonSerializer.Deserialize<AuthorizationResponse>(responseJson);

                if (decision == null || decision.Authorized == null ||
                    string.IsNullOrWhiteSpace(decision.Reason))
                {
                    return new AuthorizationResult(
                        AuthorizationStatus.InvalidResponse,
                        "The Licensing Agent returned an invalid authorization response.");
                }

                if (decision.Authorized.Value)
                {
                    return new AuthorizationResult(
                        AuthorizationStatus.Allowed,
                        "Render Dock is authorized.");
                }

                return MapDenial(decision.Reason, decision.LicenseCenterUrl);
            }
            catch (OperationCanceledException)
            {
                return new AuthorizationResult(
                    AuthorizationStatus.AgentUnavailable,
                    "The Licensing Agent did not respond in time.");
            }
            catch (HttpRequestException)
            {
                return new AuthorizationResult(
                    AuthorizationStatus.AgentUnavailable,
                    "The Licensing Agent is unavailable.");
            }
            catch (JsonException)
            {
                return new AuthorizationResult(
                    AuthorizationStatus.InvalidResponse,
                    "The Licensing Agent returned malformed data.");
            }
            catch (Exception)
            {
                return new AuthorizationResult(
                    AuthorizationStatus.InvalidResponse,
                    "Authorization could not be verified.");
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private static AuthorizationResult MapDenial(string reason, string? licenseCenterUrl)
        {
            if (string.Equals(reason, "activation_required", StringComparison.OrdinalIgnoreCase))
            {
                return new AuthorizationResult(
                    AuthorizationStatus.ActivationRequired,
                    "Render Dock requires activation. Open the Licensing Agent License Center.",
                    licenseCenterUrl);
            }

            if (string.Equals(reason, "unsupported", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reason, "unsupported_product", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reason, "unsupported_version", StringComparison.OrdinalIgnoreCase))
            {
                return new AuthorizationResult(
                    AuthorizationStatus.Unsupported,
                    "This Render Dock product or version is not supported.");
            }

            return new AuthorizationResult(
                AuthorizationStatus.Denied,
                "The Licensing Agent denied Render Dock startup.");
        }

        private static ProductManifest LoadManifest()
        {
            var manifestPath = Path.Combine(AppContext.BaseDirectory, "bke.manifest.json");
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<ProductManifest>(json);

            if (manifest == null ||
                manifest.SchemaVersion != 1 ||
                !string.Equals(manifest.ProductId, "bke-render-dock", StringComparison.Ordinal) ||
                !string.Equals(manifest.DisplayName, "Render Dock", StringComparison.Ordinal) ||
                !string.Equals(manifest.EntryPoint, "RENDER DOCK.exe", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.Version))
            {
                throw new InvalidDataException("Invalid Render Dock manifest.");
            }

            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            var canonicalVersion = assemblyVersion == null
                ? string.Empty
                : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";

            if (!string.Equals(manifest.Version, canonicalVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Manifest version does not match Render Dock.");
            }

            return manifest;
        }

        private sealed class ProductManifest
        {
            [JsonPropertyName("schemaVersion")]
            public int SchemaVersion { get; set; }

            [JsonPropertyName("productId")]
            public string ProductId { get; set; } = string.Empty;

            [JsonPropertyName("displayName")]
            public string DisplayName { get; set; } = string.Empty;

            [JsonPropertyName("version")]
            public string Version { get; set; } = string.Empty;

            [JsonPropertyName("entryPoint")]
            public string EntryPoint { get; set; } = string.Empty;
        }

        private sealed class AuthorizationRequest
        {
            [JsonPropertyName("product_id")]
            public string ProductId { get; set; } = string.Empty;

            [JsonPropertyName("version")]
            public string Version { get; set; } = string.Empty;

            [JsonPropertyName("installation_id")]
            public string InstallationId { get; set; } = string.Empty;
        }

        private sealed class AuthorizationResponse
        {
            [JsonPropertyName("authorized")]
            public bool? Authorized { get; set; }

            [JsonPropertyName("reason")]
            public string Reason { get; set; } = string.Empty;

            [JsonPropertyName("license_center_url")]
            public string? LicenseCenterUrl { get; set; }
        }
    }
}
