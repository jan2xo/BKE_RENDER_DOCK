using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BKE.Licensing.Contracts;
using BKE.Licensing.Policy;
using BKE.Licensing.Verification;
using BKE_MediaTools.Licensing;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Xunit;

namespace RenderDock.Tests;

[Collection("RenderDockExecutable")]
public sealed class RenderDockNativeLicensingExecutableTests
{
    private static readonly string RepoRoot = FindRepositoryRoot();

    [Fact]
    public void NativeGate_EnforcesAllCommercialAndAdversarialScenarios_CrossPlatform()
    {
        var now = DateTimeOffset.UtcNow;
        var authority = GenerateKeyPair("rd-authority-1");
        var authorityThumbprint = Ed25519KeyUtilities.ComputeSpkiThumbprint(authority.Public);
        var rogueAuthority = GenerateKeyPair("rd-authority-rogue");
        var issuer = GenerateKeyPair("rd-issuer-active");
        var revokedIssuer = GenerateKeyPair("rd-issuer-revoked");
        var unknownIssuer = GenerateKeyPair("rd-issuer-unknown");
        var agent = GenerateKeyPair("rd-agent-1");
        var wrongAgent = GenerateKeyPair("rd-agent-wrong");
        var publicFixture = GeneratePublicFixtureKeyPair();

        var baseScope = CreateDefaultScope(agent.Thumbprint);

        // 1. Valid ACCOUNT + Agent proof -> ALLOW
        var accountBundle = BuildSignedBundle(
            authority,
            ActivationModes.Account,
            issuer,
            "ACTIVE",
            agent,
            baseScope,
            now,
            presentedCredential: ActivationCredentials.Account,
            includeAccountJws: true,
            includeCommercialEnvelope: false,
            includeAgentChallenge: true);
        var acctOk = RenderDockStartupAuthorizationGate.EvaluateBundle(accountBundle, authorityThumbprint, false, now);
        Assert.True(acctOk.Authorized, acctOk.Reason);
        Assert.Equal(0, acctOk.ExitCode);
        Assert.Equal(ActivationCredentials.Account, acctOk.Decision?.CredentialUsed);

        // 2. Valid LICENSE_KEY + Agent proof -> ALLOW
        var licenseBundle = BuildSignedBundle(
            authority,
            ActivationModes.LicenseKey,
            issuer,
            "ACTIVE",
            agent,
            baseScope,
            now,
            presentedCredential: ActivationCredentials.LicenseKey,
            includeAccountJws: false,
            includeCommercialEnvelope: true,
            includeAgentChallenge: true);
        var commOk = RenderDockStartupAuthorizationGate.EvaluateBundle(licenseBundle, authorityThumbprint, false, now);
        Assert.True(commOk.Authorized, commOk.Reason);
        Assert.Equal(0, commOk.ExitCode);
        Assert.Equal(ActivationCredentials.LicenseKey, commOk.Decision?.CredentialUsed);

        // 3. Correct EITHER policy allows either independently valid credential
        var eitherAccount = BuildSignedBundle(
            authority, ActivationModes.Either, issuer, "ACTIVE", agent, baseScope, now,
            ActivationCredentials.Account, includeAccountJws: true, includeCommercialEnvelope: false, includeAgentChallenge: true);
        Assert.True(RenderDockStartupAuthorizationGate.EvaluateBundle(eitherAccount, authorityThumbprint, false, now).Authorized);

        var eitherLicense = BuildSignedBundle(
            authority, ActivationModes.Either, issuer, "ACTIVE", agent, baseScope, now,
            ActivationCredentials.LicenseKey, includeAccountJws: false, includeCommercialEnvelope: true, includeAgentChallenge: true);
        Assert.True(RenderDockStartupAuthorizationGate.EvaluateBundle(eitherLicense, authorityThumbprint, false, now).Authorized);

        // 3b. Account mode rejects commercial-only presentation
        var modeMismatch = BuildSignedBundle(
            authority, ActivationModes.Account, issuer, "ACTIVE", agent, baseScope, now,
            ActivationCredentials.LicenseKey, includeAccountJws: false, includeCommercialEnvelope: true, includeAgentChallenge: true);
        var modeMismatchResult = RenderDockStartupAuthorizationGate.EvaluateBundle(modeMismatch, authorityThumbprint, false, now);
        Assert.False(modeMismatchResult.Authorized);
        Assert.Equal(86, modeMismatchResult.ExitCode);

        // 4. Expired leases -> DENY (85)
        var expiredAcct = BuildSignedBundle(
            authority, ActivationModes.Account, issuer, "ACTIVE", agent, baseScope, now,
            ActivationCredentials.Account, true, false, true,
            leaseExpiresAt: now.AddMinutes(-5));
        var expiredAcctResult = RenderDockStartupAuthorizationGate.EvaluateBundle(expiredAcct, authorityThumbprint, false, now);
        Assert.False(expiredAcctResult.Authorized);
        Assert.Equal(85, expiredAcctResult.ExitCode);
        Assert.Equal(AccountProductLeaseFailureReasons.Expired, expiredAcctResult.Reason);

        var expiredComm = BuildSignedBundle(
            authority, ActivationModes.LicenseKey, issuer, "ACTIVE", agent, baseScope, now,
            ActivationCredentials.LicenseKey, false, true, true,
            leaseExpiresAt: now.AddMinutes(-5));
        var expiredCommResult = RenderDockStartupAuthorizationGate.EvaluateBundle(expiredComm, authorityThumbprint, false, now);
        Assert.False(expiredCommResult.Authorized);
        Assert.Equal(85, expiredCommResult.ExitCode);
        Assert.Equal(CommercialLeaseFailureReasons.Expired, expiredCommResult.Reason);

        // 5. Tampered signatures (JWS, Commercial Envelope, Policy Envelope) -> DENY (85)
        var acctParts = accountBundle.AccountJws!.Split('.');
        var tamperedSigAcct = accountBundle with
        {
            AccountJws = $"{acctParts[0]}.{acctParts[1]}.{FlipFirstChar(acctParts[2])}",
        };
        var tamperedAcctRes = RenderDockStartupAuthorizationGate.EvaluateBundle(tamperedSigAcct, authorityThumbprint, false, now);
        Assert.False(tamperedAcctRes.Authorized);
        Assert.Equal(85, tamperedAcctRes.ExitCode);

        var tamperedComm = licenseBundle with
        {
            CommercialEnvelope = licenseBundle.CommercialEnvelope! with
            {
                Signature = FlipFirstBase64Char(licenseBundle.CommercialEnvelope.Signature),
            },
        };
        var tamperedCommRes = RenderDockStartupAuthorizationGate.EvaluateBundle(tamperedComm, authorityThumbprint, false, now);
        Assert.False(tamperedCommRes.Authorized);
        Assert.Equal(85, tamperedCommRes.ExitCode);

        var tamperedPolicy = accountBundle with
        {
            PolicySignatureBase64Url = FlipFirstChar(accountBundle.PolicySignatureBase64Url),
        };
        var tamperedPolicyRes = RenderDockStartupAuthorizationGate.EvaluateBundle(tamperedPolicy, authorityThumbprint, false, now);
        Assert.False(tamperedPolicyRes.Authorized);
        Assert.Equal(85, tamperedPolicyRes.ExitCode);

        // 6. Revoked, untrusted, rogue authority, and public test fixture keys -> DENY (85)
        var revokedBundle = BuildSignedBundle(
            authority, ActivationModes.Account, revokedIssuer, AccountProductLeaseKeyStates.Revoked, agent, baseScope, now,
            ActivationCredentials.Account, true, false, true);
        Assert.False(RenderDockStartupAuthorizationGate.EvaluateBundle(revokedBundle, authorityThumbprint, false, now).Authorized);

        var untrustedKidBundle = BuildSignedBundle(
            authority, ActivationModes.Account, issuer, "ACTIVE", agent, baseScope, now,
            ActivationCredentials.Account, true, false, true,
            signingKeyOverride: unknownIssuer);
        Assert.False(RenderDockStartupAuthorizationGate.EvaluateBundle(untrustedKidBundle, authorityThumbprint, false, now).Authorized);

        var fixtureSignerBundle = BuildSignedBundle(
            authority, ActivationModes.Account, publicFixture, "ACTIVE", agent, baseScope, now,
            ActivationCredentials.Account, true, false, true);
        Assert.False(RenderDockStartupAuthorizationGate.EvaluateBundle(fixtureSignerBundle, authorityThumbprint, false, now).Authorized);

        var rogueAuthorityBundle = BuildSignedBundle(
            rogueAuthority, ActivationModes.Account, issuer, "ACTIVE", agent, baseScope, now,
            ActivationCredentials.Account, true, false, true);
        Assert.False(RenderDockStartupAuthorizationGate.EvaluateBundle(rogueAuthorityBundle, authorityThumbprint, false, now).Authorized);

        // 7. Wrong product ID -> DENY (85)
        var wrongProduct = BuildSignedBundle(
            authority, ActivationModes.Account, issuer, "ACTIVE", agent, baseScope, now,
            ActivationCredentials.Account, true, false, true,
            leaseProductIdOverride: "bke-other-product");
        var wrongProductRes = RenderDockStartupAuthorizationGate.EvaluateBundle(wrongProduct, authorityThumbprint, false, now);
        Assert.False(wrongProductRes.Authorized);
        Assert.Equal(85, wrongProductRes.ExitCode);

        // 8. Wrong machine / session binding -> DENY (85)
        var wrongSession = BuildSignedBundle(
            authority, ActivationModes.Account, issuer, "ACTIVE", agent, baseScope, now,
            ActivationCredentials.Account, true, false, true,
            leaseAgentSessionOverride: "sess-wrong-machine");
        Assert.False(RenderDockStartupAuthorizationGate.EvaluateBundle(wrongSession, authorityThumbprint, false, now).Authorized);

        var wrongDevice = BuildSignedBundle(
            authority, ActivationModes.LicenseKey, issuer, "ACTIVE", agent, baseScope, now,
            ActivationCredentials.LicenseKey, false, true, true,
            leaseDeviceIdOverride: "dev-wrong-machine");
        Assert.False(RenderDockStartupAuthorizationGate.EvaluateBundle(wrongDevice, authorityThumbprint, false, now).Authorized);

        // 9. CLAIM_CODE presented as activation -> DENY (86)
        foreach (var mode in new[] { ActivationModes.Account, ActivationModes.LicenseKey, ActivationModes.Either })
        {
            var claimBundle = accountBundle with { PresentedCredential = ActivationCredentials.ClaimCode };
            if (mode != ActivationModes.Account)
            {
                claimBundle = BuildSignedBundle(
                    authority, mode, issuer, "ACTIVE", agent, baseScope, now,
                    ActivationCredentials.ClaimCode, true, true, true);
            }

            var claimRes = RenderDockStartupAuthorizationGate.EvaluateBundle(claimBundle, authorityThumbprint, false, now);
            Assert.False(claimRes.Authorized);
            Assert.Equal(86, claimRes.ExitCode);
            Assert.Equal(ActivationModeFailureReasons.ClaimCodeNotActivationCredential, claimRes.Reason);
        }

        // 10. Absent or untrusted Agent proof -> DENY (84)
        var absentAgent = accountBundle with { AgentChallenge = null };
        var absentAgentRes = RenderDockStartupAuthorizationGate.EvaluateBundle(absentAgent, authorityThumbprint, false, now);
        Assert.False(absentAgentRes.Authorized);
        Assert.Equal(84, absentAgentRes.ExitCode);

        var wrongAgentChallenge = BuildSignedBundle(
            authority, ActivationModes.Account, issuer, "ACTIVE", agent, baseScope, now,
            ActivationCredentials.Account, true, false, true,
            challengeSignerOverride: wrongAgent);
        var wrongAgentRes = RenderDockStartupAuthorizationGate.EvaluateBundle(wrongAgentChallenge, authorityThumbprint, false, now);
        Assert.False(wrongAgentRes.Authorized);
        Assert.Equal(84, wrongAgentRes.ExitCode);
    }

    [Fact]
    public void TypeScriptSignedAuthorizations_VerifyInRenderDockNativeGate()
    {
        var fixture = LoadOrGenerateTypeScriptInteropFixture();
        Assert.NotNull(fixture);

        var authority = GenerateKeyPair("rd-ts-authority");
        var authorityThumbprint = Ed25519KeyUtilities.ComputeSpkiThumbprint(authority.Public);
        var now = DateTimeOffset.UtcNow;

        var accountBundle = BuildBundleFromTypeScriptFixture(
            authority,
            fixture!,
            ActivationModes.Either,
            ActivationCredentials.Account,
            probeOutputPath: null,
            now);
        var accountResult = RenderDockStartupAuthorizationGate.EvaluateBundle(
            accountBundle, authorityThumbprint, agentProcessAuthorized: false, now);
        Assert.True(accountResult.Authorized, accountResult.Reason);
        Assert.Equal(ActivationCredentials.Account, accountResult.Decision?.CredentialUsed);

        var licenseBundle = BuildBundleFromTypeScriptFixture(
            authority,
            fixture!,
            ActivationModes.Either,
            ActivationCredentials.LicenseKey,
            probeOutputPath: null,
            now);
        var licenseResult = RenderDockStartupAuthorizationGate.EvaluateBundle(
            licenseBundle, authorityThumbprint, agentProcessAuthorized: false, now);
        Assert.True(licenseResult.Authorized, licenseResult.Reason);
        Assert.Equal(ActivationCredentials.LicenseKey, licenseResult.Decision?.CredentialUsed);
    }

    [Fact]
    public async Task WindowsExecutable_ProvesAllPositiveAndNegativeScenarios_And_ExecutesProtectedPathOnlyWhenAuthorized()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var testRoot = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
            ? Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? throw new InvalidOperationException("RUNNER_TEMP missing")
            : Path.GetTempPath();
        var workDir = Path.Combine(testRoot, "bke-rd-native-exec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            var authority = GenerateKeyPair("rd-exe-authority");
            var authorityThumbprint = Ed25519KeyUtilities.ComputeSpkiThumbprint(authority.Public);
            var publishDir = Path.Combine(workDir, "publish");
            var project = Path.Combine(RepoRoot, "BKE_RENDER_DOCK", "RENDER DOCK.csproj");

            var publish = await RunAsync("dotnet", new[]
            {
                "publish", project,
                "--configuration", "Release",
                "-r", "win-x64",
                "--self-contained", "false",
                "-p:BkeStartupGateCertification=true",
                "-p:BkeStartupGateAcceptance=true",
                $"-p:BkeCertificationAuthorityThumbprint={authorityThumbprint}",
                "-o", publishDir,
            }, TimeSpan.FromMinutes(3));
            Assert.True(publish.ExitCode == 0, "Acceptance publish failed: " + publish.Error + publish.Output);

            var exePath = Path.Combine(publishDir, "RENDER DOCK.exe");
            Assert.True(File.Exists(exePath));

            var now = DateTimeOffset.UtcNow;
            var issuer = GenerateKeyPair("rd-exe-issuer");
            var revokedIssuer = GenerateKeyPair("rd-exe-revoked");
            var unknownIssuer = GenerateKeyPair("rd-exe-unknown");
            var agent = GenerateKeyPair("rd-exe-agent");
            var rogueAgent = GenerateKeyPair("rd-exe-rogue-agent");
            var publicFixture = GeneratePublicFixtureKeyPair();
            var scope = CreateDefaultScope(agent.Thumbprint);

            // Helper to execute RENDER DOCK.exe with optional staged bundle and extra args/env
            async Task<(int ExitCode, string ProbePath, bool ProbeCreated, JsonDocument? ProbeJson)> ExecuteScenarioAsync(
                string scenarioName,
                RenderDockStartupBundle? bundle,
                string[]? args = null)
            {
                var scenarioDir = Path.Combine(workDir, scenarioName);
                Directory.CreateDirectory(scenarioDir);
                var probePath = Path.Combine(scenarioDir, "protected-startup-proof.json");
                var env = new Dictionary<string, string?>();

                if (bundle is not null)
                {
                    var bundleWithProbe = bundle with { ProtectedProbeOutputPath = probePath };
                    var bundlePath = Path.Combine(scenarioDir, "startup-bundle.json");
                    File.WriteAllText(bundlePath, JsonSerializer.Serialize(bundleWithProbe), Encoding.UTF8);
                    env[RenderDockStartupAuthorizationGate.BundleEnvVar] = bundlePath;
                }
                else
                {
                    env[RenderDockStartupAuthorizationGate.BundleEnvVar] = null;
                }

                var run = await RunAsync(exePath, args ?? Array.Empty<string>(), TimeSpan.FromSeconds(30), env);
                var created = File.Exists(probePath);
                JsonDocument? doc = created ? JsonDocument.Parse(File.ReadAllText(probePath, Encoding.UTF8)) : null;
                return (run.ExitCode, probePath, created, doc);
            }

            // POSITIVE 1: Valid ACCOUNT lease + required Agent proof -> ALLOW + protected UI path runs
            var acctPos = await ExecuteScenarioAsync(
                "allow-account",
                BuildSignedBundle(authority, ActivationModes.Account, issuer, "ACTIVE", agent, scope, now,
                    ActivationCredentials.Account, true, false, true));
            using (acctPos.ProbeJson)
            {
                Assert.Equal(0, acctPos.ExitCode);
                Assert.True(acctPos.ProbeCreated, "Valid ACCOUNT startup did not execute protected Render Dock UI path.");
                AssertProtectedProbe(acctPos.ProbeJson!, ActivationModes.Account, ActivationCredentials.Account);
            }

            // POSITIVE 2: Valid LICENSE_KEY lease + required Agent proof -> ALLOW + protected UI path runs
            var commPos = await ExecuteScenarioAsync(
                "allow-license-key",
                BuildSignedBundle(authority, ActivationModes.LicenseKey, issuer, "ACTIVE", agent, scope, now,
                    ActivationCredentials.LicenseKey, false, true, true));
            using (commPos.ProbeJson)
            {
                Assert.Equal(0, commPos.ExitCode);
                Assert.True(commPos.ProbeCreated, "Valid LICENSE_KEY startup did not execute protected Render Dock UI path.");
                AssertProtectedProbe(commPos.ProbeJson!, ActivationModes.LicenseKey, ActivationCredentials.LicenseKey);
            }

            // POSITIVE 3: EITHER policy allows valid ACCOUNT and valid LICENSE_KEY independently
            var eitherAcctPos = await ExecuteScenarioAsync(
                "allow-either-account",
                BuildSignedBundle(authority, ActivationModes.Either, issuer, "ACTIVE", agent, scope, now,
                    ActivationCredentials.Account, true, false, true));
            using (eitherAcctPos.ProbeJson)
            {
                Assert.Equal(0, eitherAcctPos.ExitCode);
                Assert.True(eitherAcctPos.ProbeCreated);
                AssertProtectedProbe(eitherAcctPos.ProbeJson!, ActivationModes.Either, ActivationCredentials.Account);
            }

            var eitherCommPos = await ExecuteScenarioAsync(
                "allow-either-license-key",
                BuildSignedBundle(authority, ActivationModes.Either, issuer, "ACTIVE", agent, scope, now,
                    ActivationCredentials.LicenseKey, false, true, true));
            using (eitherCommPos.ProbeJson)
            {
                Assert.Equal(0, eitherCommPos.ExitCode);
                Assert.True(eitherCommPos.ProbeCreated);
                AssertProtectedProbe(eitherCommPos.ProbeJson!, ActivationModes.Either, ActivationCredentials.LicenseKey);
            }

            // POSITIVE 4: Live TypeScript-minted ACCOUNT and LICENSE_KEY leases execute real RENDER DOCK.exe
            var tsFixture = LoadOrGenerateTypeScriptInteropFixture();
            Assert.NotNull(tsFixture);
            var tsAcctPos = await ExecuteScenarioAsync(
                "allow-ts-account",
                BuildBundleFromTypeScriptFixture(authority, tsFixture!, ActivationModes.Either, ActivationCredentials.Account, null, now));
            using (tsAcctPos.ProbeJson)
            {
                Assert.Equal(0, tsAcctPos.ExitCode);
                Assert.True(tsAcctPos.ProbeCreated);
                AssertProtectedProbe(tsAcctPos.ProbeJson!, ActivationModes.Either, ActivationCredentials.Account);
            }

            var tsCommPos = await ExecuteScenarioAsync(
                "allow-ts-license-key",
                BuildBundleFromTypeScriptFixture(authority, tsFixture!, ActivationModes.Either, ActivationCredentials.LicenseKey, null, now));
            using (tsCommPos.ProbeJson)
            {
                Assert.Equal(0, tsCommPos.ExitCode);
                Assert.True(tsCommPos.ProbeCreated);
                AssertProtectedProbe(tsCommPos.ProbeJson!, ActivationModes.Either, ActivationCredentials.LicenseKey);
            }

            // NEGATIVE SCENARIOS: every denial must return non-zero AND must NOT create protected probe file
            var validAcctTemplate = BuildSignedBundle(authority, ActivationModes.Account, issuer, "ACTIVE", agent, scope, now,
                ActivationCredentials.Account, true, false, true);
            var validCommTemplate = BuildSignedBundle(authority, ActivationModes.LicenseKey, issuer, "ACTIVE", agent, scope, now,
                ActivationCredentials.LicenseKey, false, true, true);

            // EITHER policy rejects missing credential
            var eitherMissing = await ExecuteScenarioAsync(
                "deny-either-missing",
                BuildSignedBundle(authority, ActivationModes.Either, issuer, "ACTIVE", agent, scope, now,
                    ActivationCredentials.Account, false, false, true));
            AssertDenied(eitherMissing, 86);

            // Expired lease
            var expiredExe = await ExecuteScenarioAsync(
                "deny-expired",
                BuildSignedBundle(authority, ActivationModes.Account, issuer, "ACTIVE", agent, scope, now,
                    ActivationCredentials.Account, true, false, true, leaseExpiresAt: now.AddMinutes(-2)));
            AssertDenied(expiredExe, 85);

            // Tampered signature
            var parts = validAcctTemplate.AccountJws!.Split('.');
            var tamperedExe = await ExecuteScenarioAsync(
                "deny-tampered-signature",
                validAcctTemplate with { AccountJws = $"{parts[0]}.{parts[1]}.{FlipFirstChar(parts[2])}" });
            AssertDenied(tamperedExe, 85);

            // Revoked signing key
            var revokedExe = await ExecuteScenarioAsync(
                "deny-revoked-key",
                BuildSignedBundle(authority, ActivationModes.Account, revokedIssuer, AccountProductLeaseKeyStates.Revoked, agent, scope, now,
                    ActivationCredentials.Account, true, false, true));
            AssertDenied(revokedExe, 85);

            // Untrusted signing key + public test fixture key
            var untrustedKeyExe = await ExecuteScenarioAsync(
                "deny-untrusted-key",
                BuildSignedBundle(authority, ActivationModes.Account, issuer, "ACTIVE", agent, scope, now,
                    ActivationCredentials.Account, true, false, true, signingKeyOverride: unknownIssuer));
            AssertDenied(untrustedKeyExe, 85);

            var fixtureKeyExe = await ExecuteScenarioAsync(
                "deny-public-fixture-key",
                BuildSignedBundle(authority, ActivationModes.Account, publicFixture, "ACTIVE", agent, scope, now,
                    ActivationCredentials.Account, true, false, true));
            AssertDenied(fixtureKeyExe, 85);

            // Wrong product ID
            var wrongProductExe = await ExecuteScenarioAsync(
                "deny-wrong-product",
                BuildSignedBundle(authority, ActivationModes.Account, issuer, "ACTIVE", agent, scope, now,
                    ActivationCredentials.Account, true, false, true, leaseProductIdOverride: "bke-other-product"));
            AssertDenied(wrongProductExe, 85);

            // Wrong machine/session binding
            var wrongBindingExe = await ExecuteScenarioAsync(
                "deny-wrong-session-binding",
                BuildSignedBundle(authority, ActivationModes.Account, issuer, "ACTIVE", agent, scope, now,
                    ActivationCredentials.Account, true, false, true, leaseAgentSessionOverride: "sess-wrong"));
            AssertDenied(wrongBindingExe, 85);

            var wrongDeviceExe = await ExecuteScenarioAsync(
                "deny-wrong-device-binding",
                BuildSignedBundle(authority, ActivationModes.LicenseKey, issuer, "ACTIVE", agent, scope, now,
                    ActivationCredentials.LicenseKey, false, true, true, leaseDeviceIdOverride: "dev-wrong"));
            AssertDenied(wrongDeviceExe, 85);

            // CLAIM_CODE presented as activation
            var claimCodeExe = await ExecuteScenarioAsync(
                "deny-claim-code",
                BuildSignedBundle(authority, ActivationModes.Either, issuer, "ACTIVE", agent, scope, now,
                    ActivationCredentials.ClaimCode, true, true, true));
            AssertDenied(claimCodeExe, 86);

            // Absent / untrusted Agent proof in bundle + direct missing Agent pipe
            var absentAgentExe = await ExecuteScenarioAsync(
                "deny-absent-agent-bundle",
                validAcctTemplate with { AgentChallenge = null });
            AssertDenied(absentAgentExe, 84);

            var untrustedAgentExe = await ExecuteScenarioAsync(
                "deny-untrusted-agent-bundle",
                BuildSignedBundle(authority, ActivationModes.Account, issuer, "ACTIVE", agent, scope, now,
                    ActivationCredentials.Account, true, false, true, challengeSignerOverride: rogueAgent));
            AssertDenied(untrustedAgentExe, 84);

            // Fake Launcher command-line markers must never authorize startup
            var fakeLauncherCli = await ExecuteScenarioAsync(
                "deny-fake-launcher-args",
                bundle: null,
                args: new[] { "--launcher-authorized", "--authorized=true", "--activation-mode=EITHER", "--skip-agent" });
            AssertDenied(fakeLauncherCli, 84);

            // No authorization on direct EXE startup
            var directNoAuth = await ExecuteScenarioAsync("deny-direct-exe", bundle: null);
            AssertDenied(directNoAuth, 84);
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }

    private static void AssertProtectedProbe(JsonDocument doc, string expectedMode, string expectedCredential)
    {
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ProtectedStartupExecuted").GetBoolean());
        Assert.True(root.GetProperty("MainFormHandleCreated").GetBoolean());
        Assert.True(root.GetProperty("AllowDrop").GetBoolean());
        Assert.True(root.GetProperty("NotificationBellAttached").GetBoolean());
        Assert.Equal("BKE_RenderDock", root.GetProperty("FormName").GetString());
        Assert.Equal("bke-render-dock", root.GetProperty("ProductId").GetString());
        Assert.Equal("1.0.3", root.GetProperty("Version").GetString());
        Assert.Equal(expectedMode, root.GetProperty("ActivationMode").GetString());
        Assert.Equal(expectedCredential, root.GetProperty("CredentialUsed").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("LeaseId").GetString()));
    }

    private static void AssertDenied(
        (int ExitCode, string ProbePath, bool ProbeCreated, JsonDocument? ProbeJson) outcome,
        int expectedExitCode)
    {
        outcome.ProbeJson?.Dispose();
        Assert.Equal(expectedExitCode, outcome.ExitCode);
        Assert.False(outcome.ProbeCreated, "Denied executable unexpectedly opened protected Render Dock UI.");
    }

    private static TypeScriptInteropFixture? LoadOrGenerateTypeScriptInteropFixture()
    {
        var stagedPath = Path.Combine(RepoRoot, "packages", "ts-renderdock-live-interop.json");
        if (File.Exists(stagedPath))
        {
            return JsonSerializer.Deserialize<TypeScriptInteropFixture>(
                File.ReadAllText(stagedPath, Encoding.UTF8),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        // Local fallback when running cross-platform unit tests alongside bke-libraries-typescript
        var tsSibling = Path.Combine(Directory.GetParent(RepoRoot)!.FullName, "bke-libraries-typescript");
        var tsxCli = Path.Combine(tsSibling, "node_modules", "tsx", "dist", "cli.mjs");
        if (!Directory.Exists(tsSibling) || !File.Exists(tsxCli))
        {
            Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")),
                "CI must stage packages/ts-renderdock-live-interop.json during bootstrap.");
            return null;
        }

        var tmpOut = Path.Combine(Path.GetTempPath(), $"bke-rd-ts-{Guid.NewGuid():N}.json");
        var tmpScript = Path.Combine(Path.GetTempPath(), $"bke-rd-ts-{Guid.NewGuid():N}.mjs");
        try
        {
            File.WriteAllText(tmpScript, """
import { generateKeyPairSync } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const repoRoot = process.env.BKE_TS_REPO_PATH;
const outFile = process.env.BKE_RD_INTEROP_OUT;
const load = (rel) => import(pathToFileURL(path.join(repoRoot, rel)).href);
const { issueAccountProductLeaseJws, spkiThumbprint } = await load('@bke/licensing/providers/ed25519-account-product-lease.ts');
const { createEd25519CommercialLeaseSigner } = await load('@bke/licensing/providers/ed25519-commercial-lease-signer.ts');
const { createInMemoryLeaseChallengeStore, issueLeaseChallenge, signLeaseChallengeProof } = await load('@bke/licensing/logic/lease-challenge-proof.ts');

const authKp = generateKeyPairSync('ed25519');
const agentKp = generateKeyPairSync('ed25519');
const authPubPem = authKp.publicKey.export({ type: 'spki', format: 'pem' }).toString();
const authPrivPem = authKp.privateKey.export({ type: 'pkcs8', format: 'pem' }).toString();
const agentPubPem = agentKp.publicKey.export({ type: 'spki', format: 'pem' }).toString();
const agentPrivPem = agentKp.privateKey.export({ type: 'pkcs8', format: 'pem' }).toString();
const thumb = spkiThumbprint(agentKp.publicKey);
const now = new Date();
const issuedAtDate = new Date(now.getTime() - 2 * 60 * 1000);
const refreshAfterDate = new Date(now.getTime() + 6 * 60 * 60 * 1000);
const expiresAtDate = new Date(now.getTime() + 24 * 60 * 60 * 1000);

const accountJws = issueAccountProductLeaseJws({
  schema: 'bke.product-lease.v1',
  issuer: 'BKE Digital Solutions',
  audience: 'bke-licensing-agent',
  leaseId: 'rd-ts-acct-lease-001',
  accountId: 'acct-rd-ts-001',
  userId: 'usr-rd-ts-001',
  productId: 'bke-render-dock',
  agentSessionId: 'sess-rd-ts-001',
  agentKeyThumbprint: thumb,
  keyProtection: 'tpm',
  plan: 'PRO',
  allowedActions: ['OPEN', 'USE'],
  issuedAt: issuedAtDate.toISOString(),
  refreshAfter: refreshAfterDate.toISOString(),
  expiresAt: expiresAtDate.toISOString(),
}, { kid: 'ts-renderdock-acct-k1', privateKeyPem: authPrivPem });

const signer = createEd25519CommercialLeaseSigner({ resolve: () => authPrivPem });
const commercialEnvelope = await signer.issue({
  license_id: 'lic-rd-ts-001',
  lease_id: 'rd-ts-comm-lease-001',
  generation: 1,
  server_revision: 1,
  product_id: 'bke-render-dock',
  installation_id: 'inst-rd-ts-device-0001',
  device_id: 'dev-rd-ts-machine-0001',
  version: '1.0.3',
  issuer: 'BKE Digital Solutions',
  issued_at: issuedAtDate.toISOString(),
  not_before: issuedAtDate.toISOString(),
  expires_at: expiresAtDate.toISOString(),
  key_id: 'ts-renderdock-comm-k1',
  algorithm: 'Ed25519',
  revoked: false,
  superseded_by: null,
}, {
  id: 'krec-rd-ts-1',
  keyId: 'ts-renderdock-comm-k1',
  algorithm: 'Ed25519',
  status: 'ACTIVE',
  publicKey: authPubPem,
  privateKeyReference: 'mem:rd-ts-live',
  createdAt: issuedAtDate,
  activatedAt: issuedAtDate,
  retiredAt: null,
  rotationReason: null,
  createdBy: 'renderdock-interop',
});

const challengeStore = createInMemoryLeaseChallengeStore();
const challenge = await issueLeaseChallenge(challengeStore, {
  challengeId: 'chg-rd-ts-001',
  accountId: 'acct-rd-ts-001',
  userId: 'usr-rd-ts-001',
  productId: 'bke-render-dock',
  agentSessionId: 'sess-rd-ts-001',
  agentKeyThumbprint: thumb,
  action: 'OPEN',
  now,
});
const challengeSignature = signLeaseChallengeProof(challenge.messageBase64url, agentPrivPem);

fs.writeFileSync(outFile, JSON.stringify({
  typeScriptContractCommit: 'local',
  issuerPublicKeyPem: authPubPem,
  agentPublicKeyPem: agentPubPem,
  agentKeyThumbprint: thumb,
  accountKeyId: 'ts-renderdock-acct-k1',
  accountJws,
  commercialKeyId: 'ts-renderdock-comm-k1',
  commercialEnvelope,
  expectedScope: {
    issuer: 'BKE Digital Solutions',
    audience: 'bke-licensing-agent',
    accountId: 'acct-rd-ts-001',
    userId: 'usr-rd-ts-001',
    productId: 'bke-render-dock',
    version: '1.0.3',
    agentSessionId: 'sess-rd-ts-001',
    installationId: 'inst-rd-ts-device-0001',
    deviceId: 'dev-rd-ts-machine-0001',
    action: 'OPEN',
  },
  challenge: {
    challengeId: challenge.record.challengeId,
    nonce: challenge.record.nonce,
    messageHash: challenge.record.messageHash,
    createdAt: challenge.record.createdAt.toISOString(),
    expiresAt: challenge.record.expiresAt.toISOString(),
    messageBase64Url: challenge.messageBase64url,
    signatureBase64Url: challengeSignature,
  },
}), 'utf8');
""");

            var psi = new ProcessStartInfo("node")
            {
                WorkingDirectory = tsSibling,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.Environment["BKE_TS_REPO_PATH"] = tsSibling;
            psi.Environment["BKE_RD_INTEROP_OUT"] = tmpOut;
            psi.ArgumentList.Add(tsxCli);
            psi.ArgumentList.Add(tmpScript);
            using var p = Process.Start(psi)!;
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, $"Local TS fixture mint failed: {stderr}");
            return JsonSerializer.Deserialize<TypeScriptInteropFixture>(
                File.ReadAllText(tmpOut, Encoding.UTF8),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        finally
        {
            if (File.Exists(tmpOut)) File.Delete(tmpOut);
            if (File.Exists(tmpScript)) File.Delete(tmpScript);
        }
    }

    private static RenderDockStartupBundle BuildBundleFromTypeScriptFixture(
        Ed25519TestKeyPair authority,
        TypeScriptInteropFixture fixture,
        string activationMode,
        string presentedCredential,
        string? probeOutputPath,
        DateTimeOffset now)
    {
        var policyObj = new Dictionary<string, object?>
        {
            ["schema"] = RenderDockStartupAuthorizationGate.PolicySchema,
            ["issuer"] = fixture.ExpectedScope.Issuer,
            ["productId"] = fixture.ExpectedScope.ProductId,
            ["version"] = fixture.ExpectedScope.Version,
            ["activationMode"] = activationMode,
            ["requireAgentProof"] = true,
            ["issuedAt"] = FormatUtc(now.AddMinutes(-2)),
            ["expiresAt"] = FormatUtc(now.AddHours(12)),
            ["accountId"] = fixture.ExpectedScope.AccountId,
            ["userId"] = fixture.ExpectedScope.UserId,
            ["agentSessionId"] = fixture.ExpectedScope.AgentSessionId,
            ["agentKeyThumbprint"] = fixture.AgentKeyThumbprint,
            ["installationId"] = fixture.ExpectedScope.InstallationId,
            ["deviceId"] = fixture.ExpectedScope.DeviceId,
            ["accountKeys"] = new Dictionary<string, object?>
            {
                [fixture.AccountKeyId] = new Dictionary<string, object?>
                {
                    ["publicKeyPem"] = fixture.IssuerPublicKeyPem,
                    ["state"] = AccountProductLeaseKeyStates.Active,
                },
            },
            ["commercialKeys"] = new Dictionary<string, object?>
            {
                [fixture.CommercialKeyId] = new Dictionary<string, object?>
                {
                    ["publicKey"] = fixture.IssuerPublicKeyPem,
                    ["status"] = CommercialLeaseKeyStatuses.Active,
                },
            },
        };

        var policyBytes = JsonSerializer.SerializeToUtf8Bytes(policyObj);
        var policyB64 = Ed25519KeyUtilities.EncodeBase64Url(policyBytes);
        var policySig = Ed25519KeyUtilities.EncodeBase64Url(SignEd25519(authority.Private, policyBytes));

        var commercialEnvelope = new CommercialLeaseEnvelope(
            fixture.CommercialEnvelope.Payload,
            fixture.CommercialEnvelope.Signature,
            fixture.CommercialEnvelope.Key_Id,
            fixture.CommercialEnvelope.Algorithm);

        return new RenderDockStartupBundle(
            authority.PublicPem,
            policyB64,
            policySig,
            presentedCredential,
            AccountJws: fixture.AccountJws,
            CommercialEnvelope: commercialEnvelope,
            AgentChallenge: new RenderDockAgentChallengeProof(
                fixture.Challenge.ChallengeId,
                fixture.Challenge.Nonce,
                fixture.Challenge.MessageHash,
                fixture.Challenge.CreatedAt,
                fixture.Challenge.ExpiresAt,
                fixture.Challenge.MessageBase64Url,
                fixture.Challenge.SignatureBase64Url,
                fixture.AgentPublicKeyPem),
            ProtectedProbeOutputPath: probeOutputPath);
    }

    private static TestScope CreateDefaultScope(string agentThumbprint) => new(
        "acct-rd-001",
        "usr-rd-001",
        "sess-rd-001",
        agentThumbprint,
        "inst-rd-device-0001",
        "dev-rd-machine-0001");

    private static RenderDockStartupBundle BuildSignedBundle(
        Ed25519TestKeyPair authority,
        string activationMode,
        Ed25519TestKeyPair trustedIssuer,
        string trustedKeyState,
        Ed25519TestKeyPair agent,
        TestScope scope,
        DateTimeOffset now,
        string presentedCredential,
        bool includeAccountJws,
        bool includeCommercialEnvelope,
        bool includeAgentChallenge,
        Ed25519TestKeyPair? signingKeyOverride = null,
        Ed25519TestKeyPair? challengeSignerOverride = null,
        DateTimeOffset? leaseExpiresAt = null,
        string? leaseProductIdOverride = null,
        string? leaseAgentSessionOverride = null,
        string? leaseDeviceIdOverride = null)
    {
        var policyObj = new Dictionary<string, object?>
        {
            ["schema"] = RenderDockStartupAuthorizationGate.PolicySchema,
            ["issuer"] = RenderDockStartupAuthorizationGate.CanonicalIssuer,
            ["productId"] = RenderDockStartupAuthorizationGate.CanonicalProductId,
            ["version"] = RenderDockStartupAuthorizationGate.CanonicalVersion,
            ["activationMode"] = activationMode,
            ["requireAgentProof"] = true,
            ["issuedAt"] = FormatUtc(now.AddMinutes(-5)),
            ["expiresAt"] = FormatUtc(now.AddHours(12)),
            ["accountId"] = scope.AccountId,
            ["userId"] = scope.UserId,
            ["agentSessionId"] = scope.AgentSessionId,
            ["agentKeyThumbprint"] = scope.AgentKeyThumbprint,
            ["installationId"] = scope.InstallationId,
            ["deviceId"] = scope.DeviceId,
            ["accountKeys"] = new Dictionary<string, object?>
            {
                [trustedIssuer.KeyId] = new Dictionary<string, object?>
                {
                    ["publicKeyPem"] = trustedIssuer.PublicPem,
                    ["state"] = trustedKeyState,
                },
            },
            ["commercialKeys"] = new Dictionary<string, object?>
            {
                [trustedIssuer.KeyId] = new Dictionary<string, object?>
                {
                    ["publicKey"] = trustedIssuer.PublicPem,
                    ["status"] = trustedKeyState,
                },
            },
        };

        var policyBytes = JsonSerializer.SerializeToUtf8Bytes(policyObj);
        var policyB64 = Ed25519KeyUtilities.EncodeBase64Url(policyBytes);
        var policySig = Ed25519KeyUtilities.EncodeBase64Url(SignEd25519(authority.Private, policyBytes));

        var effectiveSigner = signingKeyOverride ?? trustedIssuer;
        var exp = leaseExpiresAt ?? now.AddHours(6);
        var leaseIssuedAt = exp <= now ? exp.AddHours(-2) : now.AddMinutes(-10);
        var leaseRefreshAfter = exp <= now ? exp.AddHours(-1) : now.AddHours(1);

        string? accountJws = null;
        if (includeAccountJws)
        {
            var headerBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                alg = AccountProductLeaseConstants.Algorithm,
                kid = effectiveSigner.KeyId,
                typ = AccountProductLeaseConstants.JwsTyp,
            });
            var claimsBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = AccountProductLeaseConstants.Schema,
                issuer = RenderDockStartupAuthorizationGate.CanonicalIssuer,
                audience = RenderDockStartupAuthorizationGate.CanonicalAccountAudience,
                leaseId = "rd-acct-lease-" + Guid.NewGuid().ToString("N")[..8],
                accountId = scope.AccountId,
                userId = scope.UserId,
                productId = leaseProductIdOverride ?? RenderDockStartupAuthorizationGate.CanonicalProductId,
                agentSessionId = leaseAgentSessionOverride ?? scope.AgentSessionId,
                agentKeyThumbprint = scope.AgentKeyThumbprint,
                keyProtection = "tpm",
                plan = "PRO",
                allowedActions = new[] { "OPEN", "USE" },
                issuedAt = FormatUtc(leaseIssuedAt),
                refreshAfter = FormatUtc(leaseRefreshAfter),
                expiresAt = FormatUtc(exp),
            });
            var signingInput = $"{Ed25519KeyUtilities.EncodeBase64Url(headerBytes)}.{Ed25519KeyUtilities.EncodeBase64Url(claimsBytes)}";
            var jwsSig = Ed25519KeyUtilities.EncodeBase64Url(SignEd25519(effectiveSigner.Private, Encoding.ASCII.GetBytes(signingInput)));
            accountJws = $"{signingInput}.{jwsSig}";
        }

        CommercialLeaseEnvelope? commercialEnvelope = null;
        if (includeCommercialEnvelope)
        {
            var payloadJson = JsonSerializer.Serialize(new
            {
                license_id = "lic-rd-001",
                lease_id = "rd-comm-lease-" + Guid.NewGuid().ToString("N")[..8],
                generation = 1,
                server_revision = 1,
                product_id = leaseProductIdOverride ?? RenderDockStartupAuthorizationGate.CanonicalProductId,
                installation_id = scope.InstallationId,
                device_id = leaseDeviceIdOverride ?? scope.DeviceId,
                version = RenderDockStartupAuthorizationGate.CanonicalVersion,
                issuer = RenderDockStartupAuthorizationGate.CanonicalIssuer,
                issued_at = FormatUtc(now.AddMinutes(-10)),
                not_before = FormatUtc(now.AddMinutes(-10)),
                expires_at = FormatUtc(exp),
                key_id = effectiveSigner.KeyId,
                algorithm = CommercialLeaseConstants.Algorithm,
                revoked = false,
                superseded_by = (string?)null,
            });
            var sigBytes = SignEd25519(effectiveSigner.Private, Encoding.UTF8.GetBytes(payloadJson));
            commercialEnvelope = new CommercialLeaseEnvelope(
                payloadJson,
                Convert.ToBase64String(sigBytes),
                effectiveSigner.KeyId,
                CommercialLeaseConstants.Algorithm);
        }

        RenderDockAgentChallengeProof? agentChallenge = null;
        if (includeAgentChallenge)
        {
            var challengeSigner = challengeSignerOverride ?? agent;
            var challengeId = "chg-rd-" + Guid.NewGuid().ToString("N")[..8];
            var nonce = Guid.NewGuid().ToString("N");
            var placeholderRecord = new LeaseChallengeRecord(
                challengeId,
                scope.AccountId,
                scope.UserId,
                RenderDockStartupAuthorizationGate.CanonicalProductId,
                scope.AgentSessionId,
                scope.AgentKeyThumbprint,
                RenderDockStartupAuthorizationGate.CanonicalRequiredAction,
                nonce,
                string.Empty,
                now.AddMinutes(-1),
                now.AddMinutes(5),
                null);
            var msgBytes = LeaseChallengeProofVerifier.BuildCanonicalMessage(placeholderRecord);
            var msgHash = Convert.ToHexStringLower(SHA256.HashData(msgBytes));
            var msgB64Url = Ed25519KeyUtilities.EncodeBase64Url(msgBytes);
            var sigB64Url = Ed25519KeyUtilities.EncodeBase64Url(SignEd25519(challengeSigner.Private, msgBytes));

            agentChallenge = new RenderDockAgentChallengeProof(
                challengeId,
                nonce,
                msgHash,
                FormatUtc(now.AddMinutes(-1)),
                FormatUtc(now.AddMinutes(5)),
                msgB64Url,
                sigB64Url,
                challengeSigner.PublicPem);
        }

        return new RenderDockStartupBundle(
            authority.PublicPem,
            policyB64,
            policySig,
            presentedCredential,
            accountJws,
            commercialEnvelope,
            agentChallenge);
    }

    private static string FormatUtc(DateTimeOffset dt) =>
        dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    private static Ed25519TestKeyPair GenerateKeyPair(string keyId)
    {
        var gen = new Ed25519KeyPairGenerator();
        gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var kp = gen.GenerateKeyPair();
        return ToTestKeyPair(keyId, (Ed25519PrivateKeyParameters)kp.Private, (Ed25519PublicKeyParameters)kp.Public);
    }

    private static Ed25519TestKeyPair GeneratePublicFixtureKeyPair()
    {
        var seed = new byte[32];
        Array.Fill(seed, (byte)0x42);
        var priv = new Ed25519PrivateKeyParameters(seed, 0);
        var pub = priv.GeneratePublicKey();
        return ToTestKeyPair("public-fixture-k1", priv, pub);
    }

    private static Ed25519TestKeyPair ToTestKeyPair(
        string keyId,
        Ed25519PrivateKeyParameters priv,
        Ed25519PublicKeyParameters pub)
    {
        var spki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pub).GetDerEncoded();
        var pubPem = "-----BEGIN PUBLIC KEY-----\n" +
            Convert.ToBase64String(spki, Base64FormattingOptions.InsertLineBreaks) +
            "\n-----END PUBLIC KEY-----";
        return new Ed25519TestKeyPair(
            keyId,
            priv,
            pub,
            pubPem,
            Ed25519KeyUtilities.ComputeSpkiThumbprint(pub));
    }

    private static byte[] SignEd25519(Ed25519PrivateKeyParameters privateKey, byte[] message)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    private static string FlipFirstChar(string base64Url)
    {
        var c = base64Url[0] == 'A' ? 'B' : 'A';
        return c + base64Url[1..];
    }

    private static string FlipFirstBase64Char(string base64)
    {
        var c = base64[0] == 'A' ? 'B' : 'A';
        return c + base64[1..];
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string command,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        IDictionary<string, string?>? envOverrides = null)
    {
        var psi = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot,
        };
        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        if (envOverrides is not null)
        {
            foreach (var kvp in envOverrides)
            {
                if (kvp.Value is null)
                {
                    psi.Environment.Remove(kvp.Key);
                }
                else
                {
                    psi.Environment[kvp.Key] = kvp.Value;
                }
            }
        }

        using var process = new Process { StartInfo = psi };
        Assert.True(process.Start(), "Could not launch subprocess: " + command);
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
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw new TimeoutException($"Subprocess {command} exceeded timeout {timeout}.");
        }
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
        throw new DirectoryNotFoundException("Could not locate Render Dock repository root.");
    }

    private sealed record Ed25519TestKeyPair(
        string KeyId,
        Ed25519PrivateKeyParameters Private,
        Ed25519PublicKeyParameters Public,
        string PublicPem,
        string Thumbprint);

    private sealed record TestScope(
        string AccountId,
        string UserId,
        string AgentSessionId,
        string AgentKeyThumbprint,
        string InstallationId,
        string DeviceId);

    private sealed record TypeScriptInteropFixture(
        string IssuerPublicKeyPem,
        string AgentPublicKeyPem,
        string AgentKeyThumbprint,
        string AccountKeyId,
        string AccountJws,
        string CommercialKeyId,
        TsCommercialEnvelope CommercialEnvelope,
        TsExpectedScope ExpectedScope,
        TsChallenge Challenge);

    private sealed record TsCommercialEnvelope(
        string Payload,
        string Signature,
        string Key_Id,
        string Algorithm);

    private sealed record TsExpectedScope(
        string Issuer,
        string Audience,
        string AccountId,
        string UserId,
        string ProductId,
        string Version,
        string AgentSessionId,
        string InstallationId,
        string DeviceId,
        string Action);

    private sealed record TsChallenge(
        string ChallengeId,
        string Nonce,
        string MessageHash,
        string CreatedAt,
        string ExpiresAt,
        string MessageBase64Url,
        string SignatureBase64Url);
}
