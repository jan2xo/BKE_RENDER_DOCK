using Xunit;

namespace RenderDock.Tests;

public sealed class NotificationStartupContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ProductBroadcastPreflightRunsBeforeStandaloneLicensingGate()
    {
        var startupGate = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Startup", "ProductStartupGate.cs"));
        var interaction = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Startup", "WinFormsStartupInteraction.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Notifications", "NotificationCoordinator.cs"));

        var preflight = startupGate.IndexOf(
            "ShowNotificationsBeforeLicensingAsync",
            StringComparison.Ordinal);
        var fallback = startupGate.IndexOf("if (!enterpriseSession)", StringComparison.Ordinal);
        var authorize = startupGate.IndexOf("EnsureAuthorizedAsync", StringComparison.Ordinal);

        Assert.True(preflight >= 0, "Notification preflight is not composed.");
        Assert.True(fallback > preflight, "Notification preflight must run before standalone licensing fallback.");
        Assert.True(authorize > preflight, "Notification preflight must run before Agent authorization.");
        Assert.Contains("NotificationCoordinator.ShowBeforeLicensing", interaction);
        Assert.Contains("ShowUnreadAsync(owner: null, enterpriseSession)", coordinator);
        Assert.Contains("Notifications are informational and must never block", coordinator);
        Assert.DoesNotContain("HttpClient", coordinator, StringComparison.Ordinal);
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
