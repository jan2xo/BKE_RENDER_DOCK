using BKE_MediaTools.Licensing;
using static BKE_MediaTools.BKE_RenderDock;

namespace BKE_MediaTools
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            using var single = new Mutex(
                initiallyOwned: true,
                name: @"Global\BKE_RenderDock_SINGLE_INSTANCE",
                out bool isNew);

            if (!isNew)
            {
                return;
            }

            ApplicationConfiguration.Initialize();

            bool graceActive;
            using (var gracePeriodClient = new GracePeriodClient())
            {
                graceActive = gracePeriodClient.IsActiveAsync().GetAwaiter().GetResult();
            }

            if (!graceActive)
            {
                AuthorizationResult authorization;
                using (var agentClient = new AgentClient())
                {
                    authorization = agentClient.AuthorizeAsync().GetAwaiter().GetResult();
                }

            if (authorization.Status == AuthorizationStatus.AgentUnavailable)
            {
                AgentRecoveryDialog.ShowRecovery();
                return;
            }

            if (authorization.Status != AuthorizationStatus.Allowed)
                {
                    MessageBox.Show(
                        authorization.Message,
                        "Render Dock Licensing",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            FfmpegBootstrap.EnsurePresentOrOffer();
            Application.Run(new BKE_RenderDock());
        }
    }
}
