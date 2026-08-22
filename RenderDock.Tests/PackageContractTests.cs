using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace RenderDock.Tests;

public sealed class PackageContractTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void ManifestAndApplicationVersionsMatch()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryRoot, "BKE_RENDER_DOCK", "bke.manifest.json")));
        var project = XDocument.Load(Path.Combine(
            RepositoryRoot, "BKE_RENDER_DOCK", "RENDER DOCK.csproj"));
        Assert.Equal("bke-render-dock", manifest.RootElement.GetProperty("productId").GetString());
        Assert.Equal("RENDER DOCK.exe", manifest.RootElement.GetProperty("entryPoint").GetString());
        Assert.Equal(
            project.Descendants("Version").Single().Value,
            manifest.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void ProductContainsNoAgentDatabaseOrPlatformLicensingImplementation()
    {
        var sources = Directory.GetFiles(
            Path.Combine(RepositoryRoot, "BKE_RENDER_DOCK"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText);
        var combined = string.Join("\n", sources);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLiteConnection", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private signing", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed lease", combined, StringComparison.OrdinalIgnoreCase);
    }
}
