using BKE.Desktop.Licensing;
using BKE_MediaTools.Licensing;

namespace BKE_MediaTools.Startup
{
    internal readonly record struct StartupGateResult(
        bool ContinueStartup,
        bool EnterpriseSession);

    internal interface IStartupInteraction
    {
        Task ShowNotificationsBeforeLicensingAsync(bool enterpriseSession);
        Task ShowAgentRecoveryAsync();
        Task ShowLicensingFailureAsync();
        Task<bool> EnsureFfmpegAsync();
    }

    internal static class ProductStartupGate
    {
        internal static async Task<StartupGateResult> EvaluateAsync(
            IStartupInteraction interaction,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(interaction);

            bool enterpriseSession;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                enterpriseSession = await new EnterpriseSessionClient()
                    .TryRedeemAsync(timeout.Token)
                    .ConfigureAwait(false);
            }

            // Product broadcasts remain visible before the standalone licensing gate.
            await interaction
                .ShowNotificationsBeforeLicensingAsync(enterpriseSession)
                .ConfigureAwait(false);

            if (!enterpriseSession)
            {
                bool graceActive;
                using (var gracePeriodClient = new GracePeriodClient())
                {
                    graceActive = await gracePeriodClient
                        .IsActiveAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!graceActive)
                {
                    AuthorizationResult authorization;
                    using (var agentClient = new AgentClient())
                    {
                        authorization = await agentClient
                            .EnsureAuthorizedAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (authorization.Status == AuthorizationStatus.ActivationCancelled)
                    {
                        return new StartupGateResult(false, enterpriseSession);
                    }

                    if (authorization.Status is AuthorizationStatus.AgentUnavailable or AuthorizationStatus.Timeout)
                    {
                        await interaction.ShowAgentRecoveryAsync().ConfigureAwait(false);
                        return new StartupGateResult(false, enterpriseSession);
                    }

                    if (authorization.Status != AuthorizationStatus.Authorized)
                    {
                        await interaction.ShowLicensingFailureAsync().ConfigureAwait(false);
                        return new StartupGateResult(false, enterpriseSession);
                    }
                }
            }

            if (!await interaction.EnsureFfmpegAsync().ConfigureAwait(false))
            {
                return new StartupGateResult(false, enterpriseSession);
            }

            return new StartupGateResult(true, enterpriseSession);
        }
    }
}
