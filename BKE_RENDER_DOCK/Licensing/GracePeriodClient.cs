using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BKE_MediaTools.Licensing
{
    internal sealed class GracePeriodClient : IDisposable
    {
        private static readonly Uri GraceEndpoint =
            new Uri("https://jl-bke.com/api/graceperiod/renderdock", UriKind.Absolute);

        private readonly HttpClient _httpClient;

        internal GracePeriodClient()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2)
            };
        }

        internal async Task<bool> IsActiveAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.GetAsync(
                    GraceEndpoint,
                    cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var json = await response.Content.ReadAsStringAsync()
                    .ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                var propertyCount = 0;
                JsonElement grace = default;
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    propertyCount++;
                    if (property.NameEquals("grace"))
                    {
                        grace = property.Value;
                    }
                }

                return propertyCount == 1 &&
                    grace.ValueKind == JsonValueKind.True;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
