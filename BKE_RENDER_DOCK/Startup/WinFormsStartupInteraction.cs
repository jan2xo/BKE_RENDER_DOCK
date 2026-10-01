using BKE_MediaTools.Licensing;
using BKE_MediaTools.Notifications;
using System.Windows.Forms;

namespace BKE_MediaTools.Startup
{
    internal sealed class WinFormsStartupInteraction : IStartupInteraction
    {
        public Task ShowNotificationsBeforeLicensingAsync(bool enterpriseSession)
        {
            NotificationCoordinator.ShowBeforeLicensing(enterpriseSession);
            return Task.CompletedTask;
        }

        public Task ShowAgentRecoveryAsync()
        {
            AgentRecoveryDialog.ShowRecovery();
            return Task.CompletedTask;
        }

        public Task ShowLicensingFailureAsync()
        {
            MessageBox.Show(
                "Render Dock could not establish a valid license for this installation. " +
                "Use BKE License Center to activate or repair licensing.",
                "Render Dock Licensing",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return Task.CompletedTask;
        }

        public Task<bool> EnsureFfmpegAsync()
        {
            BKE_RenderDock.FfmpegBootstrap.EnsurePresentOrOffer();
            return Task.FromResult(true);
        }
    }
}
