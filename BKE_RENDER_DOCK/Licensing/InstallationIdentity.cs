using System;
using System.IO;

namespace BKE_MediaTools.Licensing
{
    internal static class InstallationIdentity
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BKE Digital Solutions",
            "RENDERDOCK",
            "installation.id");

        internal static string GetOrCreate()
        {
            if (File.Exists(FilePath))
            {
                return ReadExisting();
            }

            var directory = Path.GetDirectoryName(FilePath)
                ?? throw new InvalidDataException("RENDERDOCK installation identity path is invalid.");
            Directory.CreateDirectory(directory);

            var installationId = Guid.NewGuid().ToString("D");
            File.WriteAllText(FilePath, installationId);
            return installationId;
        }

        private static string ReadExisting()
        {
            var value = File.ReadAllText(FilePath).Trim();
            if (!Guid.TryParseExact(value, "D", out var installationId))
            {
                throw new InvalidDataException("RENDERDOCK installation identity is invalid.");
            }

            return installationId.ToString("D");
        }
    }
}
