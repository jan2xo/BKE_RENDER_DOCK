using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace RenderDock.Tests;

public sealed class PackageContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ManifestAndApplicationVersionsMatch()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryRoot, "BKE_RENDER_DOCK", "bke.manifest.json")));
        var project = XDocument.Load(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "RENDER DOCK.csproj"));

        Assert.Equal("bke-render-dock", manifest.RootElement.GetProperty("productId").GetString());
        Assert.Equal("Render Dock", manifest.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("RENDER DOCK.exe", manifest.RootElement.GetProperty("entryPoint").GetString());
        Assert.Equal("windows", manifest.RootElement.GetProperty("platform").GetString());
        Assert.Equal("x64", manifest.RootElement.GetProperty("architecture").GetString());
        Assert.Equal(
            project.Descendants("Version").Single().Value,
            manifest.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void InstallerAndManifestVersionsMatch()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryRoot, "BKE_RENDER_DOCK", "bke.manifest.json")));
        var installer = File.ReadAllText(Path.Combine(
            RepositoryRoot, "packaging", "windows", "render-dock.iss"));
        var installerVersion = Regex.Match(
            installer,
            @"#define ProductVersion ""(?<version>[^""]+)""");

        Assert.True(installerVersion.Success, "Installer ProductVersion is missing.");
        Assert.Equal(
            manifest.RootElement.GetProperty("version").GetString(),
            installerVersion.Groups["version"].Value);
    }

    [Fact]
    public void ProductContainsNoAgentDatabaseOrPlatformLicensingImplementation()
    {
        var sources = Directory.GetFiles(
                Path.Combine(RepositoryRoot, "BKE_RENDER_DOCK"),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText);
        var combined = string.Join("\n", sources);

        Assert.DoesNotContain("Microsoft.Data.Sqlite", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLiteConnection", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private signing", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed lease", combined, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BKE_RENDER_DOCK.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Render Dock repository root.");
    }
}
