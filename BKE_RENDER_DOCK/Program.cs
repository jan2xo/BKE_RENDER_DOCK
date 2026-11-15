using BKE.Desktop.Licensing;
using BKE_MediaTools.Licensing;
using BKE_MediaTools.Notifications;
using BKE_MediaTools.Updates;
using System.Text.Json;
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
            // before ANY enterprise, license verification, or application path.
            // No response, fake pipe, timeout or exception can bypass denial.
            bool agentGranted = false;
            StandaloneStartupGateResult? agentDecision = null;
            try
            {
                var pipeTimeout = RenderDockStartupAuthorizationGate.HasStagedBundle()
                    ? TimeSpan.FromMilliseconds(150)
                    : TimeSpan.FromSeconds(10);
                using var startupTimeout = new CancellationTokenSource(pipeTimeout);
                agentDecision = new BkeStandaloneStartupGateClient()
                    .AuthorizeConnectedProcessAsync(startupTimeout.Token)
                    .GetAwaiter().GetResult();
                agentGranted = agentDecision.Authorized;
            }
            catch (Exception)
            {
                // A failed preproduction startup proof never opens the UI.
            }

            int fallbackExitCode =
                agentDecision?.Status != StandaloneStartupGateStatus.Denied
                    ? 84
                    : agentDecision?.Reason == "independent_credential_required"
                        ? 86
                        : 85;
            var nativeEvaluation = RenderDockStartupAuthorizationGate.Evaluate(agentGranted, fallbackExitCode);
            agentGranted = nativeEvaluation.Authorized;

            if (!agentGranted)
            {
#if BKE_STARTUP_GATE_ACCEPTANCE
                // NONSHIPPABLE headless proof only. Do not mistake ANY
                // Agent denial for evidence of an otherwise valid signed
                // installed product that merely lacks a commercial credential.
                // 84: absent/fake/untrusted Agent; 85: invalid/expired/tampered
                // policy or lease; 86: independent ACCOUNT/LICENSE_KEY proof
                // required or mode mismatch. None of these opens the UI.
                Environment.ExitCode = nativeEvaluation.ExitCode;
#else
                Environment.ExitCode = 84;
#endif
                return;
            }
            strictAgentGateGranted = true;

#if BKE_STARTUP_GATE_ACCEPTANCE
            if (!nativeEvaluation.RunProtectedStartupProbe)
            {
                // Separate NONSHIPPABLE HEADLESS acceptance binary. A zero exit
                // proves the real SDK returned an Agent grant for THIS process.
                // The acceptance harness independently verifies the Agent's
                // durable grant row; this marker is not commercial authority.
                Environment.ExitCode = 0;
                return;
            }
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

#if BKE_STARTUP_GATE_CERTIFICATION && BKE_STARTUP_GATE_ACCEPTANCE
            if (!nativeEvaluation.RunProtectedStartupProbe)
            {
                FfmpegBootstrap.EnsurePresentOrOffer();
            }
            var mainForm = new BKE_RenderDock();
            UpdateCoordinator.Attach(mainForm, enterpriseSession);
            NotificationCoordinator.Attach(mainForm, enterpriseSession);
            NotificationTrayController.Attach(mainForm, enterpriseSession);
            if (nativeEvaluation.RunProtectedStartupProbe &&
                !string.IsNullOrWhiteSpace(nativeEvaluation.ProtectedProbeOutputPath))
            {
                var probePath = nativeEvaluation.ProtectedProbeOutputPath;
                mainForm.Shown += (_, _) =>
                {
                    try
                    {
                        var probeDir = Path.GetDirectoryName(probePath);
                        if (!string.IsNullOrWhiteSpace(probeDir))
                        {
                            Directory.CreateDirectory(probeDir);
                        }

                        var evidence = JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            ProtectedStartupExecuted = true,
                            MainFormHandleCreated = mainForm.IsHandleCreated,
                            FormName = mainForm.Name,
                            FormTitle = mainForm.Text,
                            AllowDrop = mainForm.AllowDrop,
                            NotificationBellAttached = mainForm.Controls.ContainsKey("notificationBell"),
                            ProcessId = Environment.ProcessId,
                            ProductId = RenderDockStartupAuthorizationGate.CanonicalProductId,
                            Version = RenderDockStartupAuthorizationGate.CanonicalVersion,
                            ActivationMode = nativeEvaluation.ActivationMode,
                            CredentialUsed = nativeEvaluation.Decision?.CredentialUsed,
                            LeaseId = nativeEvaluation.Decision?.LeaseId,
                        });
                        File.WriteAllBytes(probePath, evidence);
                    }
                    finally
                    {
                        if (mainForm.IsHandleCreated)
                        {
                            mainForm.BeginInvoke(new Action(mainForm.Close));
                        }
                        else
                        {
                            mainForm.Close();
                        }
                    }
                };
            }
            Application.Run(mainForm);
#else
            FfmpegBootstrap.EnsurePresentOrOffer();
            var mainForm = new BKE_RenderDock();
            UpdateCoordinator.Attach(mainForm, enterpriseSession);
            NotificationCoordinator.Attach(mainForm, enterpriseSession);
            NotificationTrayController.Attach(mainForm, enterpriseSession);
            Application.Run(mainForm);
#endif
        }
    }
}
