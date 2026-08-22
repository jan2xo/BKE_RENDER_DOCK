namespace BKE_MediaTools.Licensing
{
    internal static class StartupGate
    {
        internal static bool CanStart(bool graceActive, AuthorizationResult authorization)
        {
            return graceActive || authorization.Status == AuthorizationStatus.Allowed;
        }
    }
}
