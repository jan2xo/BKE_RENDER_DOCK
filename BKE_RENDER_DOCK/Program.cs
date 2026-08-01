using BKE_MediaTools;
using static BKE_MediaTools.BKE_RenderDock;

namespace BKE_MediaTools
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]

            

        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.

            using var single = new Mutex(initiallyOwned: true, name: @"Global\BKE_RenderDock_SINGLE_INSTANCE", out bool isNew);
            if (!isNew) return; // already running → exit quietly
            ApplicationConfiguration.Initialize();
            if (ExpiryLite.TryHandleCli()) return;     // optional owner shortcut
            ExpiryLite.InitializeOrCreateTrial(trialDays: 0);  // or 0 if you’ll set via CLI
            // ⬇️ Ensure ffmpeg exists (download & install to C:\ffmpeg if missing)
            FfmpegBootstrap.EnsurePresentOrOffer();
            Application.Run(new BKE_RenderDock());
        }
    }
}