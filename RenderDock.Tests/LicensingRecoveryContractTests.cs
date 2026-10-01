using Xunit;

namespace RenderDock.Tests;

public sealed class LicensingRecoveryContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ProductDelegatesRecoveryPolicyToSharedLicensingSdk()
    {
        var agentClient = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Licensing", "AgentClient.cs"));
        var startupGate = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Startup", "ProductStartupGate.cs"));
        var interaction = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Startup", "WinFormsStartupInteraction.cs"));

        Assert.Contains("EnsureAuthorizedAsync", agentClient);
        Assert.Contains("ActivationInteraction.NativeDesktop", agentClient);
        Assert.DoesNotContain("OpenLicenseCenterAsync", agentClient, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthorizeAsync", agentClient, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthorizationStatus.Denied", agentClient, StringComparison.Ordinal);
        Assert.DoesNotContain("authorization.Reason", startupGate, StringComparison.Ordinal);
        Assert.Contains("Use BKE License Center to activate or repair licensing.", interaction);
    }

    [Fact]
    public void BroadcastPreflightOccursBeforeStandaloneLicensing()
    {
        var startupGate = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Startup", "ProductStartupGate.cs"));

        var broadcast = startupGate.IndexOf(
            "ShowNotificationsBeforeLicensingAsync",
            StringComparison.Ordinal);
        var fallback = startupGate.IndexOf("if (!enterpriseSession)", StringComparison.Ordinal);
        var authorize = startupGate.IndexOf("EnsureAuthorizedAsync", StringComparison.Ordinal);

        Assert.True(broadcast >= 0 && fallback > broadcast && authorize > fallback);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BKE_RENDER_DOCK.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Render Dock repository root.");
    }
}
