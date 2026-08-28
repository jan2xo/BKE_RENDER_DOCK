using BKE.Desktop.Licensing;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SdkAuthorizationStatus = BKE.Desktop.Licensing.AuthorizationStatus;

namespace BKE_MediaTools.Licensing
{
    internal sealed class AgentClient : IDisposable
    {
        private readonly BkeLicensingClient _client = BkeLicensingClient.Create();

        internal async Task<AuthorizationResult> EnsureAuthorizedAsync(
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

            var result = await _client.EnsureAuthorizedAsync(
                manifest.ProductId,
                manifest.Version,
                installationId,
                new LicensingFlowOptions
                {
                    ActivationInteraction = ActivationInteraction.NativeDesktop
                },
                cancellationToken).ConfigureAwait(false);

            return result.Status switch
            {
                SdkAuthorizationStatus.Authorized => new AuthorizationResult(
                    AuthorizationStatus.Allowed,
                    "Render Dock is authorized."),
                SdkAuthorizationStatus.ActivationCancelled => new AuthorizationResult(
                    AuthorizationStatus.Cancelled,
                    "Render Dock activation was cancelled."),
                SdkAuthorizationStatus.AgentUnavailable => new AuthorizationResult(
                    AuthorizationStatus.AgentUnavailable,
                    "The Licensing Agent is unavailable."),
                SdkAuthorizationStatus.Timeout => new AuthorizationResult(
                    AuthorizationStatus.AgentUnavailable,
                    "The Licensing Agent or License Center did not respond in time."),
                SdkAuthorizationStatus.Unsupported => new AuthorizationResult(
                    AuthorizationStatus.Unsupported,
                    "This Render Dock product, version, or activation presentation is not supported."),
                SdkAuthorizationStatus.Denied => new AuthorizationResult(
                    AuthorizationStatus.Denied,
                    "The Licensing Agent denied Render Dock startup."),
                SdkAuthorizationStatus.ActivationRequired => new AuthorizationResult(
                    AuthorizationStatus.Denied,
                    "Render Dock activation did not complete."),
                SdkAuthorizationStatus.ProtocolRejected => new AuthorizationResult(
                    AuthorizationStatus.InvalidResponse,
                    "The Licensing Agent rejected the licensing request."),
                _ => new AuthorizationResult(
                    AuthorizationStatus.InvalidResponse,
                    "Authorization could not be verified.")
            };
        }

        public void Dispose()
        {
            _client.Dispose();
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
    }
}
