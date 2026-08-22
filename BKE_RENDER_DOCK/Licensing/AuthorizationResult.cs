namespace BKE_MediaTools.Licensing
{
    internal enum AuthorizationStatus
    {
        Allowed,
        Denied,
        ActivationRequired,
        AgentUnavailable,
        Unsupported,
        InvalidResponse
    }

    internal sealed class AuthorizationResult
    {
        internal AuthorizationResult(AuthorizationStatus status, string message, string? licenseCenterUrl = null)
        {
            Status = status;
            Message = message;
            LicenseCenterUrl = licenseCenterUrl;
        }

        internal AuthorizationStatus Status { get; }

        internal string Message { get; }

        internal string? LicenseCenterUrl { get; }
    }
}
