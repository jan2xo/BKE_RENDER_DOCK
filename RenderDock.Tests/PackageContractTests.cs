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
        Assert.Equal("1.0.2", manifest.RootElement.GetProperty("version").GetString());
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
    public void ProductContainsNoAgentDatabaseOrPlatformAuthorityImplementation()
    {
        var combined = ProductSources();

        Assert.DoesNotContain("Microsoft.Data.Sqlite", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLiteConnection", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private signing", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed lease", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("download_grant", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trusted_key", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductTargetsDotNet10AndConsumesCanonicalSdkVersions()
    {
        var project = XDocument.Load(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "RENDER DOCK.csproj"));
        var testProject = XDocument.Load(Path.Combine(
            RepositoryRoot, "RenderDock.Tests", "RenderDock.Tests.csproj"));

        Assert.Equal("net10.0-windows", project.Descendants("TargetFramework").Single().Value);
        Assert.Equal("net10.0", testProject.Descendants("TargetFramework").Single().Value);
        AssertPackage(project, "BKE.Desktop.Licensing", "2.0.0");
        AssertPackage(project, "BKE.Updater", "0.4.0");
        Assert.DoesNotContain(
            project.Descendants("PackageReference"),
            element => string.Equals(
                element.Attribute("Include")?.Value,
                "BKE.Desktop.Client",
                StringComparison.Ordinal));
    }

    [Fact]
    public void StandaloneAuthorizationUsesDesktopLicensingCapabilityOnly()
    {
        var agent = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Licensing", "AgentClient.cs"));
        var program = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Program.cs"));

        Assert.Contains("BkeLicensingClient.Create", agent);
        Assert.Contains("EnsureAuthorizedAsync", agent);
        Assert.Contains("ActivationInteraction.NativeDesktop", agent);
        Assert.DoesNotContain("HttpClient", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1:43873", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("/v1/authorize", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("/v1/license-center/open", agent, StringComparison.Ordinal);
        Assert.Contains("AuthorizationStatus.Authorized", program);
        Assert.Contains("AuthorizationStatus.ActivationCancelled", program);
        Assert.DoesNotContain("AuthorizationStatus.ActivationRequired", program);
        Assert.False(File.Exists(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Licensing", "AuthorizationResult.cs")));
    }

    [Fact]
    public void UpdateDiscoveryUsesBkeUpdaterAndContainsNoProviderProtocol()
    {
        var coordinatorPath = Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Updates", "UpdateCoordinator.cs");
        var coordinator = File.ReadAllText(coordinatorPath);
        var program = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Program.cs"));
        var combined = ProductSources();

        Assert.Contains("BkeUpdaterClient.Create", coordinator);
        Assert.Contains("UpdateCheckRequest", coordinator);
        Assert.Contains("UpdateCheckStatus.UpdateAvailable", coordinator);
        Assert.Contains("UpdateCheckStatus.Failed", coordinator);
        Assert.Contains("UpdateCoordinator.Attach(mainForm, enterpriseSession)", program);
        Assert.Contains("if (enterpriseSession)", coordinator);
        Assert.DoesNotContain("HttpClient", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1:43873", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("v1/updates/status", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("v1/updates/refresh", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("v1/updates/dismiss", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/v1/updates/check", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/v1/update-center/open", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AgentUpdateStatus", combined, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Licensing", "AgentUpdateClient.cs")));
        Assert.False(File.Exists(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Licensing", "AgentUpdateCoordinator.cs")));
    }

    [Fact]
    public void SdkBootstrapIsPinnedToCanonicalSdkMerge()
    {
        var bootstrap = File.ReadAllText(Path.Combine(
            RepositoryRoot, "scripts", "bootstrap-bke-sdk.ps1"));

        Assert.Contains("be79a1d3e055353183622ed6676498e685475495", bootstrap);
        Assert.Contains("BKE.Desktop.Licensing.2.0.0.nupkg", bootstrap);
        Assert.Contains("BKE.Updater.0.4.0.nupkg", bootstrap);
        Assert.DoesNotContain("packages/BKE.Desktop.Client.1.0.0", bootstrap, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnterpriseModeStillUsesAgentAuthenticatedChildRendezvous()
    {
        var client = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Licensing", "EnterpriseSessionClient.cs"));
        var program = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Program.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Updates", "UpdateCoordinator.cs"));

        Assert.Contains("operation = \"redeem\"", client);
        Assert.Contains("NamedPipeClientStream", client);
        Assert.Contains("TryRedeemAsync", program);
        Assert.Contains("if (enterpriseSession)", coordinator);
        Assert.DoesNotContain("Environment.GetCommandLineArgs", client, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Environment.GetCommandLineArgs", program, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StandaloneLicensingRemainsFallbackWhenEnterpriseRedemptionFails()
    {
        var program = File.ReadAllText(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "Program.cs"));

        var redeem = program.IndexOf("TryRedeemAsync", StringComparison.Ordinal);
        var fallback = program.IndexOf("if (!enterpriseSession)", StringComparison.Ordinal);
        var standalone = program.IndexOf("EnsureAuthorizedAsync", StringComparison.Ordinal);
        Assert.True(redeem >= 0 && fallback > redeem && standalone > fallback);
    }

    private static string ProductSources()
    {
        var sources = Directory.GetFiles(
                Path.Combine(RepositoryRoot, "BKE_RENDER_DOCK"),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText);
        return string.Join("\n", sources);
    }

    private static void AssertPackage(XDocument project, string packageId, string version)
    {
        var package = project.Descendants("PackageReference")
            .SingleOrDefault(element => string.Equals(
                element.Attribute("Include")?.Value,
                packageId,
                StringComparison.Ordinal));

        Assert.NotNull(package);
        Assert.Equal(version, package!.Attribute("Version")?.Value);
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
