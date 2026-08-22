using System.Net;
using System.Text;
using BKE_MediaTools.Licensing;
using Xunit;

namespace RenderDock.Tests;

public sealed class AgentBoundaryTests
{
    [Fact]
    public async Task ExplicitAllowPermitsStartup()
    {
        using var client = CreateClient("{\"authorized\":true,\"reason\":\"authorized\"}");
        var result = await client.AuthorizeAsync();
        Assert.Equal(AuthorizationStatus.Allowed, result.Status);
        Assert.True(StartupGate.CanStart(false, result));
    }

    [Theory]
    [InlineData("unknown_product_or_version")]
    [InlineData("unsupported_version")]
    public async Task DenialNeverPermitsProtectedStartup(string reason)
    {
        using var client = CreateClient($"{{\"authorized\":false,\"reason\":\"{reason}\"}}");
        var result = await client.AuthorizeAsync();
        var protectedFunctionExecuted = false;
        if (StartupGate.CanStart(false, result)) protectedFunctionExecuted = true;
        Assert.False(protectedFunctionExecuted);
    }

    [Fact]
    public async Task ActivationRequiredCarriesOnlyAgentOwnedLicenseCenter()
    {
        using var client = CreateClient("{\"authorized\":false,\"reason\":\"activation_required\",\"license_center_url\":\"http://127.0.0.1:43873/license-center\"}");
        var result = await client.AuthorizeAsync();
        Assert.Equal(AuthorizationStatus.ActivationRequired, result.Status);
        Assert.Equal("http://127.0.0.1:43873/license-center", result.LicenseCenterUrl?.AbsoluteUri);
        Assert.False(StartupGate.CanStart(false, result));
    }

    [Fact]
    public async Task UntrustedLicenseCenterUrlIsDiscarded()
    {
        using var client = CreateClient("{\"authorized\":false,\"reason\":\"activation_required\",\"license_center_url\":\"https://example.com/steal\"}");
        var result = await client.AuthorizeAsync();
        Assert.Null(result.LicenseCenterUrl);
        Assert.False(StartupGate.CanStart(false, result));
    }

    [Fact]
    public async Task MalformedResponseFailsClosed()
    {
        using var client = CreateClient("not-json");
        var result = await client.AuthorizeAsync();
        Assert.Equal(AuthorizationStatus.InvalidResponse, result.Status);
        Assert.False(StartupGate.CanStart(false, result));
    }

    [Fact]
    public async Task AgentUnavailableFailsClosed()
    {
        using var httpClient = new HttpClient(new ThrowingHandler());
        using var client = CreateClient(httpClient);
        var result = await client.AuthorizeAsync();
        Assert.Equal(AuthorizationStatus.AgentUnavailable, result.Status);
        Assert.False(StartupGate.CanStart(false, result));
    }

    private static AgentClient CreateClient(string json)
    {
        return CreateClient(new HttpClient(new ResponseHandler(json)));
    }

    private static AgentClient CreateClient(HttpClient httpClient)
    {
        var manifest = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "BKE_RENDER_DOCK", "bke.manifest.json"));
        return new AgentClient(httpClient, manifest, () => "d06b6709-83e4-4128-8ec4-f6b473a11c98");
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        private readonly string _json;
        internal ResponseHandler(string json) => _json = json;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("http://127.0.0.1:43873/v1/authorize", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
