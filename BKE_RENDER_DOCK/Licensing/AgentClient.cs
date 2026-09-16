using BKE.Desktop.Licensing;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

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

            var authorization = await _client.EnsureAuthorizedAsync(
                manifest.ProductId,
                manifest.Version,
                installationId,
                new LicensingFlowOptions
                {
                    ActivationInteraction = ActivationInteraction.NativeDesktop
                },
                cancellationToken).ConfigureAwait(false);

            if (authorization.Status != AuthorizationStatus.Denied)
            {
                return authorization;
            }

            // A denied local authorization can represent a stale or otherwise
            // unverifiable persisted lease. The product must not expose the
            // Agent's internal denial reason. Ask the Agent to present its native
            // License Center so recovery/activation remains Agent-owned.
            var center = await _client.OpenLicenseCenterAsync(
                manifest.ProductId,
                manifest.Version,
                installationId,
                cancellationToken).ConfigureAwait(false);

            switch (center.Status)
            {
                case LicenseCenterStatus.AuthorizationRefreshed:
                case LicenseCenterStatus.Completed:
                    return await _client.AuthorizeAsync(
                        manifest.ProductId,
                        manifest.Version,
                        installationId,
                        cancellationToken).ConfigureAwait(false);

                case LicenseCenterStatus.Cancelled:
                    return new AuthorizationResult(
                        AuthorizationStatus.ActivationCancelled,
                        "activation_cancelled");

                case LicenseCenterStatus.AgentUnavailable:
                    return new AuthorizationResult(AuthorizationStatus.AgentUnavailable, center.Reason);

                case LicenseCenterStatus.Timeout:
                    return new AuthorizationResult(AuthorizationStatus.Timeout, center.Reason);

                case LicenseCenterStatus.ProtocolRejected:
                    return new AuthorizationResult(AuthorizationStatus.ProtocolRejected, center.Reason);

                case LicenseCenterStatus.InvalidRequest:
                    return new AuthorizationResult(AuthorizationStatus.InvalidRequest, center.Reason);

                case LicenseCenterStatus.InvalidResponse:
                    return new AuthorizationResult(AuthorizationStatus.InvalidResponse, center.Reason);

                case LicenseCenterStatus.InvalidProductContext:
                case LicenseCenterStatus.IncompatibleProductVersion:
                case LicenseCenterStatus.Unsupported:
                    return new AuthorizationResult(AuthorizationStatus.Unsupported, center.Reason);

                case LicenseCenterStatus.ActivationFailed:
                case LicenseCenterStatus.Failed:
                default:
                    return new AuthorizationResult(
                        AuthorizationStatus.Denied,
                        "license_center_recovery_failed");
            }
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
