using Xunit;

namespace RenderDock.Tests;

public sealed class LicensingRecoveryContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void DeniedAuthorizationUsesAgentOwnedLicenseCenterInsteadOfRawReasonPopup()
    {
        var agentClient = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Licensing", "AgentClient.cs"));
        var program = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Program.cs"));

        Assert.Contains("authorization.Status != AuthorizationStatus.Denied", agentClient);
        Assert.Contains("_client.OpenLicenseCenterAsync", agentClient);
        Assert.Contains("_client.AuthorizeAsync", agentClient);
        Assert.Contains("LicenseCenterStatus.AuthorizationRefreshed", agentClient);
        Assert.Contains("LicenseCenterStatus.Completed", agentClient);
        Assert.DoesNotContain("MessageBox.Show(\n                            authorization.Reason", program);
        Assert.DoesNotContain("unverifiable_signed_lease", program);
        Assert.DoesNotContain("unverifiable_signed_lease", agentClient);
    }

    [Fact]
    public void BroadcastPreflightStillOccursBeforeStandaloneLicensing()
    {
        var program = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Program.cs"));

        var broadcast = program.IndexOf("NotificationCoordinator.ShowBeforeLicensing", StringComparison.Ordinal);
        var authorize = program.IndexOf("EnsureAuthorizedAsync", StringComparison.Ordinal);

        Assert.True(broadcast >= 0 && authorize > broadcast);
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
