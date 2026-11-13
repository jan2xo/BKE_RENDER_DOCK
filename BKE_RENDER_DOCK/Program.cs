using BKE.Desktop.Licensing;
using BKE_MediaTools.Licensing;
using BKE_MediaTools.Notifications;
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

            // Only the Agent's process-bound signed-policy decision can set this.
            // Normal retail builds retain authenticated Air Stack or signed-key licensing,
            // never an unauthenticated HTTP grace override.
            bool strictAgentGateGranted = false;

#if BKE_STARTUP_GATE_CERTIFICATION
            // PREPRODUCTION ONLY: Agent must authenticate this very Windows
            // process and commit a signed-policy/credential/audit-backed grant
            // before ANY enterprise, signed-key licensing or application path.
            // No response, fake pipe, timeout or exception can bypass denial.
            bool agentGranted = false;
            StandaloneStartupGateResult? agentDecision = null;
            try
            {
                using var startupTimeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));
                agentDecision = new BkeStandaloneStartupGateClient()
                    .AuthorizeConnectedProcessAsync(startupTimeout.Token)
                    .GetAwaiter().GetResult();
                agentGranted = agentDecision.Authorized;
            }
            catch (Exception)
            {
                // A failed preproduction startup proof never opens the UI.
            }

            if (!agentGranted)
            {
#if BKE_STARTUP_GATE_ACCEPTANCE
                // NONSHIPPABLE headless proof only: 85 means the SDK verified
                // the installed Agent transport and received an explicit DENY.
                // Missing, fake or untrusted Agent remains 84. Neither opens UI.
                Environment.ExitCode =
                    agentDecision?.Status == StandaloneStartupGateStatus.Denied ? 85 : 84;
#else
                Environment.ExitCode = 84;
#endif
                return;
            }
            strictAgentGateGranted = true;

#if BKE_STARTUP_GATE_ACCEPTANCE
            // Separate NONSHIPPABLE HEADLESS acceptance binary. A zero exit
            // proves the real SDK returned an Agent grant for THIS process.
            // The acceptance harness independently verifies the Agent's
            // durable grant row; this marker is not commercial authority.
            Environment.ExitCode = 0;
            return;
#endif
#endif

            bool enterpriseSession;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                enterpriseSession = new EnterpriseSessionClient()
                    .TryRedeemAsync(timeout.Token)
                    .GetAwaiter()
                    .GetResult();
            }

            // Product broadcasts are informational and intentionally run before the
            // licensing gate so beta/trial/support-end notices remain visible even
            // when licensing subsequently denies application startup.
            NotificationCoordinator.ShowBeforeLicensing(enterpriseSession);

            if (!enterpriseSession && !strictAgentGateGranted)
            {
                // No unsigned global grace response may authorize a product launch.
                // The shared SDK remains the standalone commercial-key gate.
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
                        "Render Dock could not establish a valid license for this installation. " +
                        "Use BKE License Center to activate or repair licensing.",
                        "Render Dock Licensing",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            FfmpegBootstrap.EnsurePresentOrOffer();
            var mainForm = new BKE_RenderDock();
            UpdateCoordinator.Attach(mainForm, enterpriseSession);
            NotificationCoordinator.Attach(mainForm, enterpriseSession);
            NotificationTrayController.Attach(mainForm, enterpriseSession);
            Application.Run(mainForm);
        }
    }
}
