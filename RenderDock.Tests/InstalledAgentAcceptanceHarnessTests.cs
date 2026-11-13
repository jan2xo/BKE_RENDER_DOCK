using System.Diagnostics;
using System.Text;
using Xunit;

namespace RenderDock.Tests;

/// <summary>
/// Nonshippable native Windows probe: executable must fail BEFORE enterprise,
/// commercial-key licensing or UI when the installed Agent is absent. Does not claim a
/// positive installed Agent or hardware TPM result.
/// </summary>
public sealed class InstalledAgentAcceptanceHarnessTests
{
    private static readonly string RepoRoot = FindRoot();

    [Fact]
    public async Task WindowsHeadlessPositiveModeStillDeniesMissingAgent()
    {
        if (!OperatingSystem.IsWindows()) return;

        var agentPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "BKE Digital Solutions", "Licensing Agent");
        Assert.False(Directory.Exists(agentPath),
            "Disposable hosted Windows runner unexpectedly has an installed Agent; refuse negative proof.");

        // CI's guarded disposable installation may consume ONLY RUNNER_TEMP.
        // Path.GetTempPath() on hosted Windows can point to a different volume.
        var testTemporaryRoot =
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
                ? Environment.GetEnvironmentVariable("RUNNER_TEMP") ??
                    throw new InvalidOperationException("GitHub RUNNER_TEMP missing")
                : Path.GetTempPath();
        var folder = Path.Combine(testTemporaryRoot,
            "bke-renderdock-acceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var project = Path.Combine(RepoRoot, "BKE_RENDER_DOCK", "RENDER DOCK.csproj");
            var compiled = await RunAsync("dotnet", new[] {
                "publish", project, "--configuration", "Release",
                "-r", "win-x64", "--self-contained", "false",
                "-p:BkeStartupGateCertification=true",
                "-p:BkeStartupGateAcceptance=true", "-o", folder
            }, TimeSpan.FromMinutes(2));
            Assert.True(compiled.ExitCode == 0,
                "Preproduction-only headless acceptance build failed: " + compiled.Error);

            var executable = Path.Combine(folder, "RENDER DOCK.exe");
            Assert.True(File.Exists(executable), "Headless acceptance executable missing");
            var outcome = await RunAsync(executable, [], TimeSpan.FromSeconds(25));
            Assert.Equal(84, outcome.ExitCode);
            Assert.DoesNotContain("PASS installed Agent", outcome.Output);

            // Prevent accidental shippable headless-success mode:
            var illegal = await RunAsync("dotnet", new[] {
                "build", project, "--configuration", "Release",
                "-p:BkeStartupGateCertification=false",
                "-p:BkeStartupGateAcceptance=true"
            }, TimeSpan.FromMinutes(2));
            Assert.NotEqual(0, illegal.ExitCode);
            Assert.Contains("NONSHIPPABLE strict preproduction", illegal.Output + illegal.Error);

            // This EXTRA integration proof is never run on a developer or
            // customer machine. The trusted main Windows x64 certification
            // already runs this xUnit test against the exact PR source head.
            // CI-only proof installs a freshly built canonical SYSTEM Agent
            // with an ephemeral signed test policy; a genuine commercial
            // credential is DELIBERATELY ABSENT, so real Render Dock must
            // receive the exact missing-credential reason (test exit 86).
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            {
                var reviewedHead = Environment.GetEnvironmentVariable("SOURCE_SHA");
                Assert.Matches("^[a-f0-9]{40}$", reviewedHead ?? "");
                var script = Path.Combine(RepoRoot, "scripts", "certification",
                    "invoke-hosted-installed-agent-denial.ps1");
                Assert.True(File.Exists(script), "Hosted installed Agent test script missing");
                const string agentSource = "f594063493ddf39b4acb11a049eb3d4234f42794";
                var installed = await RunAsync("pwsh", new[] {
                    "-NoProfile", "-NonInteractive", "-File", script,
                    "-ExpectedRenderDockSourceSha", reviewedHead!,
                    "-ExpectedAgentSourceSha", agentSource,
                    "-AcceptanceBuildDirectory", folder,
                    "-ApprovedDisposableWindowsRunner"
                }, TimeSpan.FromMinutes(28));
                Assert.True(installed.ExitCode == 0,
                    "Real installed SYSTEM Agent / Render Dock denial test failed: " +
                    installed.Error + Environment.NewLine +
                    installed.Output[^Math.Min(installed.Output.Length, 5000)..]);
                Assert.Contains("HOSTED_INSTALLED_AGENT_RENDER_DOCK_DENIAL_PASS",
                    installed.Output);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task HostedInstalledAgentHarnessCannotRunWithoutDisposableApproval()
    {
        if (!OperatingSystem.IsWindows()) return;
        var script = Path.Combine(RepoRoot, "scripts", "certification",
            "invoke-hosted-installed-agent-denial.ps1");
        Assert.True(File.Exists(script));
        var result = await RunAsync("pwsh", new[] {
            "-NoProfile", "-NonInteractive", "-File", script,
            "-ExpectedRenderDockSourceSha", new string('a', 40),
            "-ExpectedAgentSourceSha", new string('b', 40),
            "-AcceptanceBuildDirectory", Path.GetTempPath()
        }, TimeSpan.FromSeconds(20));
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Only explicitly approved disposable Windows x64 GitHub Actions",
            result.Output + result.Error);
    }

    [Fact]
    public async Task WindowsManualHarnessCannotRunWithoutExplicitDisposableApproval()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(RepoRoot, "scripts", "certification",
            "invoke-installed-agent-startup-acceptance.ps1");
        var result = await RunAsync("pwsh", new[] {
            "-NoProfile", "-NonInteractive", "-File", path,
            "-ExpectedRenderDockSourceSha", new string('a', 40),
            "-ExpectedAgentSourceSha", new string('b', 40),
            "-ExpectedExecutableSha256", new string('c', 64),
            "-ExpectedCredential", "LICENSE_KEY",
            "-EvidenceDirectory", Path.GetTempPath()
        }, TimeSpan.FromSeconds(20));
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Explicit disposable-machine approval",
            result.Output + result.Error);
    }

    [Fact]
    public async Task WindowsReadOnlyDurableGrantVerifierRejectsForgedFixtureRows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var script = Path.Combine(RepoRoot, "scripts", "certification",
            "test-installed-agent-startup-grant.py");
        Assert.True(File.Exists(script));
        var result = await RunAsync("py", ["-3", script],
            TimeSpan.FromSeconds(30));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("12/12 PASS", result.Output);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string command, IEnumerable<string> arguments, TimeSpan timeout)
    {
        using var process = new Process {
            StartInfo = new ProcessStartInfo(command) {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = RepoRoot
            }
        };
        foreach (var arg in arguments) process.StartInfo.ArgumentList.Add(arg);
        Assert.True(process.Start(), "Could not launch test subprocess.");
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            return (process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new TimeoutException("Disposable native Render Dock test exceeded its certification timeout.");
        }
    }

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BKE_RENDER_DOCK.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Render Dock source root missing.");
    }
}
