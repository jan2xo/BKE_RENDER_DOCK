using Xunit;

namespace RenderDock.Tests;

public sealed class NotificationTrayContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void TrayUsesAgentBackedNotificationContractsWithoutRawTransport()
    {
        var tray = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Notifications", "NotificationTrayController.cs"));
        var program = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Program.cs"));

        Assert.Contains("NotificationTrayController.Attach(mainForm, enterpriseSession)", program);
        Assert.Contains("ProductId = \"bke-render-dock\"", tray);
        Assert.Contains("BkeNotificationInboxClient.Create", tray);
        Assert.Contains("GetUnreadCountAsync", tray);
        Assert.Contains("GetFeedAsync", tray);
        Assert.Contains("includeDismissed: false", tray);
        Assert.Contains("MarkReadAsync", tray);
        Assert.Contains("DismissAsync", tray);
        Assert.Contains("NotificationState.Unread", tray);
        Assert.DoesNotContain("HttpClient", tray, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1:43873", tray, StringComparison.Ordinal);
        Assert.DoesNotContain("/v1/notifications/", tray, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupPopupAndTrayRemainSeparateAndProcessDeduplicationRemainsIntact()
    {
        var startup = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Notifications", "NotificationCoordinator.cs"));
        var tray = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Notifications", "NotificationTrayController.cs"));

        Assert.Contains("ShownThisProcess", startup);
        Assert.Contains("MarkReadAsync(item.Id)", startup);
        Assert.DoesNotContain("ShownThisProcess", tray);
        Assert.Contains("new NotificationFeedQuery(limit: 50, includeDismissed: false)", tray);
        Assert.Contains("Notifications are temporarily unavailable.", tray);
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
