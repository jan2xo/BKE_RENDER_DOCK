using System.Diagnostics;
using BKE_MediaTools.Licensing;
using static BKE_MediaTools.BKE_RenderDock;

namespace BKE_MediaTools
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--package-smoke")
            {
                Environment.ExitCode = PackageSmoke.VerifyPublishedLayout(
                    AppContext.BaseDirectory) ? 0 : 1;
                return;
            }

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

                if (authorization.Status == AuthorizationStatus.ActivationRequired)
                {
                    OfferLicenseCenter(authorization);
                    return;
                }

                if (!StartupGate.CanStart(graceActive, authorization))
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

        private static void OfferLicenseCenter(AuthorizationResult authorization)
        {
            var buttons = authorization.LicenseCenterUrl == null
                ? MessageBoxButtons.OK
                : MessageBoxButtons.YesNo;
            var message = authorization.LicenseCenterUrl == null
                ? authorization.Message
                : authorization.Message + Environment.NewLine + Environment.NewLine +
                  "Open License Center now?";
            var result = MessageBox.Show(
                message,
                "Render Dock Licensing",
                buttons,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes && authorization.LicenseCenterUrl != null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = authorization.LicenseCenterUrl.AbsoluteUri,
                    UseShellExecute = true
                });
            }
        }
    }
}
