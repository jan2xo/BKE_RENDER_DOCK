using BKE_MediaTools.Notifications;
using BKE_MediaTools.Startup;
using BKE_MediaTools.Updates;

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

            var startup = ProductStartupGate
                .EvaluateAsync(new WinFormsStartupInteraction())
                .GetAwaiter()
                .GetResult();

            if (!startup.ContinueStartup)
            {
                return;
            }

            var enterpriseSession = startup.EnterpriseSession;
            var mainForm = new BKE_RenderDock();
            UpdateCoordinator.Attach(mainForm, enterpriseSession);
            NotificationCoordinator.Attach(mainForm, enterpriseSession);
            NotificationTrayController.Attach(mainForm, enterpriseSession);
            Application.Run(mainForm);
        }
    }
}
