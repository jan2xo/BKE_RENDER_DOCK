using System.Text.Json;

namespace BKE_MediaTools
{
    internal static class PackageSmoke
    {
        internal static bool VerifyPublishedLayout(string directory)
        {
            var manifestPath = Path.Combine(directory, "bke.manifest.json");
            var entryPointPath = Path.Combine(directory, "RENDER DOCK.exe");
            if (!File.Exists(manifestPath) || !File.Exists(entryPointPath))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = document.RootElement;
                return root.GetProperty("schemaVersion").GetInt32() == 1 &&
                    root.GetProperty("productId").GetString() == "bke-render-dock" &&
                    root.GetProperty("displayName").GetString() == "Render Dock" &&
                    root.GetProperty("version").GetString() == "1.0.0" &&
                    root.GetProperty("entryPoint").GetString() == "RENDER DOCK.exe" &&
                    root.GetProperty("platform").GetString() == "windows" &&
                    root.GetProperty("architecture").GetString() == "x64";
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
