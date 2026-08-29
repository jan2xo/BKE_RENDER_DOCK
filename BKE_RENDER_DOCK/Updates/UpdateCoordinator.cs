using BKE.Updater;
using System.Diagnostics;
using System.Reflection;

namespace BKE_MediaTools.Updates
{
    internal static class UpdateCoordinator
    {
        private const string ProductId = "bke-render-dock";

        internal static void Attach(Form form, bool enterpriseSession)
        {
            if (enterpriseSession)
            {
                return;
            }

            form.Shown += async (_, __) => await CheckAfterShownAsync(form).ConfigureAwait(true);
        }

        private static async Task CheckAfterShownAsync(Form form)
        {
            try
            {
                using var client = BkeUpdaterClient.Create();
                var result = await client.CheckAsync(
                    new UpdateCheckRequest(ProductId, CurrentVersion())).ConfigureAwait(true);

                if (form.IsDisposed)
                {
                    return;
                }

                switch (result.Status)
                {
                    case UpdateCheckStatus.UpdateAvailable:
                        ShowAvailable(form, result.AvailableVersion!);
                        break;

                    case UpdateCheckStatus.Failed:
                        Debug.WriteLine(
                            $"Render Dock update check failed: {result.Error?.Code}: {result.Error?.Message}");
                        break;

                    case UpdateCheckStatus.UpToDate:
                    case UpdateCheckStatus.Deferred:
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                // Update discovery is informational and must never escape the post-startup UI event.
                Debug.WriteLine($"Render Dock update check did not complete: {ex.Message}");
            }
        }

        private static string CurrentVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null
                ? "0.0.0"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }

        private static void ShowAvailable(Form owner, string availableVersion)
        {
            MessageBox.Show(
                owner,
                $"Render Dock {availableVersion} is available. Update installation is managed by BKE. You can continue rendering and install the update when the managed update capability is available.",
                "Render Dock Update",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
