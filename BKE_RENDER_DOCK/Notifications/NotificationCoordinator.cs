using BKE.Notifications;
using BKE_MediaTools.Licensing;
using System.Diagnostics;
using System.Reflection;

namespace BKE_MediaTools.Notifications
{
    internal static class NotificationCoordinator
    {
        private const string ProductId = "bke-render-dock";

        internal static void Attach(Form form, bool enterpriseSession)
        {
            ArgumentNullException.ThrowIfNull(form);
            form.Shown += async (_, __) =>
                await ShowUnreadAfterStartupAsync(form, enterpriseSession).ConfigureAwait(true);
        }

        private static async Task ShowUnreadAfterStartupAsync(Form form, bool enterpriseSession)
        {
            try
            {
                using var client = BkeNotificationInboxClient.Create(
                    ProductId,
                    CurrentVersion(),
                    InstallationIdentity.GetOrCreate());

                var feed = await client.GetFeedAsync(
                    new NotificationFeedQuery(limit: 10, includeDismissed: false)).ConfigureAwait(true);

                if (!feed.Succeeded)
                {
                    Debug.WriteLine(
                        $"Render Dock notification inbox failed: {feed.Error?.Code}: {feed.Error?.Message}");
                    return;
                }

                foreach (var item in feed.Items)
                {
                    if (item.State != NotificationState.Unread)
                    {
                        continue;
                    }

                    if (enterpriseSession && item.Category == NotificationCategory.Licensing)
                    {
                        continue;
                    }

                    if (form.IsDisposed)
                    {
                        return;
                    }

                    MessageBox.Show(
                        form,
                        item.Body,
                        item.Title,
                        MessageBoxButtons.OK,
                        IconFor(item.Severity));

                    var markRead = await client.MarkReadAsync(item.Id).ConfigureAwait(true);
                    if (markRead.Status != NotificationOperationStatus.Succeeded)
                    {
                        Debug.WriteLine(
                            $"Render Dock notification {item.Id} could not be marked read: " +
                            $"{markRead.Status}: {markRead.Error?.Code}: {markRead.Error?.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Notifications are informational and must never block or terminate Render Dock.
                Debug.WriteLine($"Render Dock notification inbox did not complete: {ex.Message}");
            }
        }

        private static string CurrentVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null
                ? "0.0.0"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }

        private static MessageBoxIcon IconFor(NotificationSeverity severity) =>
            severity switch
            {
                NotificationSeverity.Error => MessageBoxIcon.Error,
                NotificationSeverity.Warning => MessageBoxIcon.Warning,
                NotificationSeverity.Success => MessageBoxIcon.Information,
                _ => MessageBoxIcon.Information
            };
    }
}
