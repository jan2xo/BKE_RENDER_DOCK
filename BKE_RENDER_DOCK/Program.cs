using BKE.Desktop.Licensing;
using BKE_MediaTools.Licensing;
using BKE_MediaTools.Updates;
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

            bool enterpriseSession;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                enterpriseSession = new EnterpriseSessionClient()
                    .TryRedeemAsync(timeout.Token)
                    .GetAwaiter()
                    .GetResult();
            }

            if (!enterpriseSession)
            {
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
                        authorization = agentClient.EnsureAuthorizedAsync().GetAwaiter().GetResult();
                    }

                    if (authorization.Status == AuthorizationStatus.ActivationCancelled)
                    {
                        return;
                    }

                    if (authorization.Status is AuthorizationStatus.AgentUnavailable or AuthorizationStatus.Timeout)
                    {
                        AgentRecoveryDialog.ShowRecovery();
                        return;
                    }

                    if (authorization.Status != AuthorizationStatus.Authorized)
                    {
                        MessageBox.Show(
                            authorization.Reason,
                            "Render Dock Licensing",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                }
            }

            FfmpegBootstrap.EnsurePresentOrOffer();
            var mainForm = new BKE_RenderDock();
            UpdateCoordinator.Attach(mainForm, enterpriseSession);
            Application.Run(mainForm);
        }
    }
}
