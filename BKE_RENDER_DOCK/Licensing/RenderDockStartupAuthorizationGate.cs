using System.Reflection;
using System.Text;
using System.Text.Json;
using BKE.Licensing.Contracts;
using BKE.Licensing.Policy;
using BKE.Licensing.Verification;

namespace BKE_MediaTools.Licensing;

internal static class RenderDockStartupAuthorizationGate
{
    internal const string CanonicalProductId = "bke-render-dock";
    internal const string CanonicalDisplayName = "Render Dock";
    internal const string CanonicalEntryPoint = "RENDER DOCK.exe";
    internal const string CanonicalVersion = "1.0.3";
    internal const string CanonicalIssuer = "BKE Digital Solutions";
    internal const string CanonicalAccountAudience = "bke-licensing-agent";
    internal const string CanonicalRequiredAction = "OPEN";
    internal const string PolicySchema = "bke.render-dock.startup-policy.v1";
    internal const string BundleEnvVar = "BKE_RENDER_DOCK_STARTUP_BUNDLE_PATH";
    internal const string PinnedAuthorityMetadataKey = "BkeCertificationAuthorityThumbprint";

    internal static bool HasStagedBundle()
    {
#if BKE_STARTUP_GATE_ACCEPTANCE
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BundleEnvVar));
#else
        return false;
#endif
    }

    internal static RenderDockStartupEvaluation Evaluate(
        bool agentProcessAuthorized,
        int fallbackExitCode)
    {
        if (!TryValidateApplicationManifest(out var manifestFailure))
        {
            return RenderDockStartupEvaluation.Deny(85, manifestFailure);
        }

#if BKE_STARTUP_GATE_ACCEPTANCE
        var bundlePath = Environment.GetEnvironmentVariable(BundleEnvVar);
        if (!string.IsNullOrWhiteSpace(bundlePath))
        {
            var pinnedThumbprint = ReadPinnedAuthorityThumbprint();
            if (string.IsNullOrWhiteSpace(pinnedThumbprint))
            {
                return RenderDockStartupEvaluation.Deny(84, AuthorizationFailureReasons.AuthorityUnavailable);
            }

            return EvaluateBundleFile(bundlePath, pinnedThumbprint, agentProcessAuthorized, DateTimeOffset.UtcNow);
        }
#endif

        return agentProcessAuthorized
            ? RenderDockStartupEvaluation.AllowLegacyAgentOnly()
            : RenderDockStartupEvaluation.Deny(fallbackExitCode, "AGENT_DENIED");
    }

    internal static RenderDockStartupEvaluation EvaluateBundleFile(
        string bundlePath,
        string pinnedAuthorityThumbprint,
        bool agentProcessAuthorized,
        DateTimeOffset now)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
            {
                return RenderDockStartupEvaluation.Deny(84, AuthorizationFailureReasons.MissingAuthorizationProof);
            }

            var rawBytes = File.ReadAllBytes(bundlePath);
            if (!StrictJsonParser.TryParseUnambiguous(rawBytes, out var doc) || doc is null)
            {
                return RenderDockStartupEvaluation.Deny(85, "MALFORMED_BUNDLE");
            }

            using (doc)
            {
                var bundle = JsonSerializer.Deserialize<RenderDockStartupBundle>(doc.RootElement.GetRawText());
                if (bundle is null)
                {
                    return RenderDockStartupEvaluation.Deny(85, "MALFORMED_BUNDLE");
                }

                return EvaluateBundle(bundle, pinnedAuthorityThumbprint, agentProcessAuthorized, now);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return RenderDockStartupEvaluation.Deny(85, "MALFORMED_BUNDLE");
        }
    }

    internal static RenderDockStartupEvaluation EvaluateBundle(
        RenderDockStartupBundle? bundle,
        string? pinnedAuthorityThumbprint,
        bool agentProcessAuthorized,
        DateTimeOffset now)
    {
        if (bundle is null ||
            string.IsNullOrWhiteSpace(pinnedAuthorityThumbprint) ||
            !Ed25519KeyUtilities.IsValidThumbprint(pinnedAuthorityThumbprint))
        {
            return RenderDockStartupEvaluation.Deny(84, AuthorizationFailureReasons.AuthorityUnavailable);
        }

        if (Ed25519KeyUtilities.IsPublicTestFixtureKey(bundle.AuthorityPublicKeyPem, null) ||
            !Ed25519KeyUtilities.TryParsePublicKey(bundle.AuthorityPublicKeyPem, out var authorityKey) ||
            authorityKey is null)
        {
            return RenderDockStartupEvaluation.Deny(85, "UNTRUSTED_AUTHORITY_ROOT");
        }

        var actualAuthorityThumbprint = Ed25519KeyUtilities.ComputeSpkiThumbprint(authorityKey);
        if (!string.Equals(actualAuthorityThumbprint, pinnedAuthorityThumbprint, StringComparison.Ordinal))
        {
            return RenderDockStartupEvaluation.Deny(85, "UNTRUSTED_AUTHORITY_ROOT");
        }

        var policyBytes = Ed25519KeyUtilities.DecodeCanonicalBase64Url(bundle.PolicyPayloadBase64Url, 65536);
        var policySigBytes = Ed25519KeyUtilities.DecodeCanonicalBase64Url(bundle.PolicySignatureBase64Url, 128);
        if (policyBytes is null ||
            policySigBytes is null ||
            policySigBytes.Length != 64 ||
            !Ed25519KeyUtilities.VerifyEd25519Signature(authorityKey, policyBytes, policySigBytes))
        {
            return RenderDockStartupEvaluation.Deny(85, "INVALID_POLICY_SIGNATURE");
        }

        if (!TryParseVerifiedPolicy(policyBytes, now, out var policy, out var policyErrorCode, out var policyReason) ||
            policy is null)
        {
            return RenderDockStartupEvaluation.Deny(policyErrorCode, policyReason);
        }

        // Agent trust boundary: either kernel-verified standalone gate already passed,
        // or a valid, unconsumed Agent PoP challenge signature bound to policy scope must pass.
        if (!agentProcessAuthorized)
        {
            if (bundle.AgentChallenge is null)
            {
                return RenderDockStartupEvaluation.Deny(84, "AGENT_PROOF_REQUIRED");
            }

            if (!Ed25519KeyUtilities.TryParseStrictIsoUtc(bundle.AgentChallenge.CreatedAt, out var chgCreatedAt) ||
                !Ed25519KeyUtilities.TryParseStrictIsoUtc(bundle.AgentChallenge.ExpiresAt, out var chgExpiresAt))
            {
                return RenderDockStartupEvaluation.Deny(84, LeaseChallengeFailureReasons.InvalidChallenge);
            }

            var challengeStore = new InMemoryLeaseChallengeStore();
            challengeStore.IssueChallenge(new LeaseChallengeRecord(
                bundle.AgentChallenge.ChallengeId,
                policy.AccountId,
                policy.UserId,
                CanonicalProductId,
                policy.AgentSessionId,
                policy.AgentKeyThumbprint,
                CanonicalRequiredAction,
                bundle.AgentChallenge.Nonce,
                bundle.AgentChallenge.MessageHash,
                chgCreatedAt,
                chgExpiresAt,
                null));

            var popResult = LeaseChallengeProofVerifier.VerifyAndConsume(
                challengeStore,
                bundle.AgentChallenge.ChallengeId,
                bundle.AgentChallenge.MessageBase64Url,
                bundle.AgentChallenge.SignatureBase64Url,
                bundle.AgentChallenge.AgentPublicKeyPem,
                policy.AccountId,
                policy.UserId,
                CanonicalProductId,
                policy.AgentSessionId,
                policy.AgentKeyThumbprint,
                CanonicalRequiredAction,
                now);

            if (!popResult.Ok)
            {
                return RenderDockStartupEvaluation.Deny(84, popResult.Reason ?? LeaseChallengeFailureReasons.BadSignature);
            }
        }

        var modeEvaluation = ActivationModePolicy.Evaluate(policy.ActivationMode, bundle.PresentedCredential);
        if (!modeEvaluation.Ok)
        {
            return RenderDockStartupEvaluation.Deny(86, modeEvaluation.Reason ?? ActivationModeFailureReasons.ActivationModeMismatch);
        }

        var accountExpected = new AccountProductLeaseVerificationContext(
            CanonicalIssuer,
            CanonicalAccountAudience,
            policy.AccountId,
            policy.UserId,
            CanonicalProductId,
            policy.AgentSessionId,
            policy.AgentKeyThumbprint,
            CanonicalRequiredAction,
            now,
            DisallowPublicTestKeys: true);

        var commercialExpected = new CommercialLeaseVerificationContext(
            CanonicalIssuer,
            CanonicalProductId,
            policy.InstallationId,
            policy.DeviceId,
            CanonicalVersion,
            now,
            DisallowPublicTestKeys: true);

        var request = new ProductAuthorizationRequest
        {
            ProductId = CanonicalProductId,
            RequiredActivationMode = policy.ActivationMode,
            PresentedCredential = bundle.PresentedCredential,
            AuthorityAvailable = true,
            DisallowPublicTestKeys = true,
            AccountJws = bundle.AccountJws,
            AccountTrustedKeys = policy.AccountKeys,
            AccountExpected = accountExpected,
            CommercialEnvelope = bundle.CommercialEnvelope,
            CommercialTrustedKeys = policy.CommercialKeys,
            CommercialExpected = commercialExpected,
        };

        var decision = ProductAuthorizationGate.Authorize(request);
        if (!decision.Authorized)
        {
            return RenderDockStartupEvaluation.Deny(ClassifyFailureExitCode(decision.Reason), decision.Reason);
        }

        if (decision.CredentialUsed == ActivationCredentials.Account)
        {
            var accountDirect = AccountProductLeaseVerifier.Verify(
                bundle.AccountJws,
                policy.AccountKeys,
                accountExpected);
            if (!accountDirect.Ok)
            {
                return RenderDockStartupEvaluation.Deny(85, accountDirect.Reason ?? AccountProductLeaseFailureReasons.InvalidClaims);
            }
        }
        else if (decision.CredentialUsed == ActivationCredentials.LicenseKey)
        {
            var commercialDirect = CommercialLeaseVerifier.Verify(
                bundle.CommercialEnvelope,
                policy.CommercialKeys,
                commercialExpected);
            if (!commercialDirect.Ok)
            {
                return RenderDockStartupEvaluation.Deny(85, commercialDirect.Reason ?? CommercialLeaseFailureReasons.InvalidPayload);
            }
        }
        else
        {
            return RenderDockStartupEvaluation.Deny(86, ActivationModeFailureReasons.InvalidCredentialType);
        }

        return RenderDockStartupEvaluation.AllowNativeAuthorized(
            decision,
            policy.ActivationMode,
            bundle.ProtectedProbeOutputPath);
    }

    private static bool TryValidateApplicationManifest(out string failureReason)
    {
        try
        {
            var manifestPath = Path.Combine(AppContext.BaseDirectory, "bke.manifest.json");
            if (!File.Exists(manifestPath))
            {
                failureReason = "MANIFEST_MISSING";
                return false;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
            var root = doc.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1 ||
                !root.TryGetProperty("productId", out var productId) || productId.GetString() != CanonicalProductId ||
                !root.TryGetProperty("displayName", out var displayName) || displayName.GetString() != CanonicalDisplayName ||
                !root.TryGetProperty("entryPoint", out var entryPoint) || entryPoint.GetString() != CanonicalEntryPoint ||
                !root.TryGetProperty("version", out var version) || version.GetString() != CanonicalVersion)
            {
                failureReason = "MANIFEST_INVALID";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            failureReason = "MANIFEST_INVALID";
            return false;
        }
    }

    private static bool TryParseVerifiedPolicy(
        byte[] policyBytes,
        DateTimeOffset now,
        out VerifiedStartupPolicy? policy,
        out int exitCode,
        out string reason)
    {
        policy = null;
        exitCode = 85;
        reason = "INVALID_POLICY";

        if (!StrictJsonParser.TryParseUnambiguous(policyBytes, out var doc) || doc is null)
        {
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetString(root, "schema", out var schema) || schema != PolicySchema ||
                !TryGetString(root, "issuer", out var issuer) || issuer != CanonicalIssuer ||
                !TryGetString(root, "productId", out var productId) || productId != CanonicalProductId ||
                !TryGetString(root, "version", out var version) || version != CanonicalVersion)
            {
                reason = "POLICY_SCOPE_MISMATCH";
                return false;
            }

            if (!root.TryGetProperty("requireAgentProof", out var reqAgentElem) ||
                reqAgentElem.ValueKind != JsonValueKind.True)
            {
                exitCode = 84;
                reason = "AGENT_PROOF_REQUIRED";
                return false;
            }

            if (!TryGetString(root, "activationMode", out var mode) ||
                !ActivationModePolicy.IsValidMode(mode))
            {
                exitCode = 86;
                reason = ActivationModeFailureReasons.InvalidActivationMode;
                return false;
            }

            if (!TryGetString(root, "issuedAt", out var issuedAtRaw) ||
                !Ed25519KeyUtilities.TryParseStrictIsoUtc(issuedAtRaw, out var issuedAt) ||
                !TryGetString(root, "expiresAt", out var expiresAtRaw) ||
                !Ed25519KeyUtilities.TryParseStrictIsoUtc(expiresAtRaw, out var expiresAt))
            {
                reason = "INVALID_POLICY_TIME";
                return false;
            }

            if (issuedAt > now.AddMilliseconds(AccountProductLeaseConstants.MaxFutureSkewMs))
            {
                reason = "FUTURE_POLICY_ISSUED_AT";
                return false;
            }

            if (expiresAt <= now)
            {
                reason = "POLICY_EXPIRED";
                return false;
            }

            if (!TryGetBoundedString(root, "accountId", out var accountId) ||
                !TryGetBoundedString(root, "userId", out var userId) ||
                !TryGetBoundedString(root, "agentSessionId", out var agentSessionId) ||
                !TryGetString(root, "agentKeyThumbprint", out var agentKeyThumbprint) ||
                !Ed25519KeyUtilities.IsValidThumbprint(agentKeyThumbprint) ||
                !TryGetBoundedString(root, "installationId", out var installationId) ||
                !TryGetBoundedString(root, "deviceId", out var deviceId))
            {
                reason = "INVALID_POLICY_BINDING";
                return false;
            }

            var accountKeys = new Dictionary<string, AccountProductLeaseTrustedKey>(StringComparer.Ordinal);
            if (root.TryGetProperty("accountKeys", out var acctKeysElem))
            {
                if (acctKeysElem.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (var prop in acctKeysElem.EnumerateObject())
                {
                    if (!Ed25519KeyUtilities.IsValidKeyId(prop.Name) ||
                        prop.Value.ValueKind != JsonValueKind.Object ||
                        !TryGetString(prop.Value, "publicKeyPem", out var pubPem) ||
                        !TryGetString(prop.Value, "state", out var state))
                    {
                        return false;
                    }

                    accountKeys[prop.Name] = new AccountProductLeaseTrustedKey(pubPem, state);
                }
            }

            var commercialKeys = new Dictionary<string, CommercialLeaseTrustedKey>(StringComparer.Ordinal);
            if (root.TryGetProperty("commercialKeys", out var commKeysElem))
            {
                if (commKeysElem.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (var prop in commKeysElem.EnumerateObject())
                {
                    if (!Ed25519KeyUtilities.IsValidKeyId(prop.Name) ||
                        prop.Value.ValueKind != JsonValueKind.Object ||
                        !TryGetString(prop.Value, "publicKey", out var pubKey) ||
                        !TryGetString(prop.Value, "status", out var status))
                    {
                        return false;
                    }

                    commercialKeys[prop.Name] = new CommercialLeaseTrustedKey(pubKey, status);
                }
            }

            policy = new VerifiedStartupPolicy(
                mode,
                accountId,
                userId,
                agentSessionId,
                agentKeyThumbprint,
                installationId,
                deviceId,
                accountKeys,
                commercialKeys);
            exitCode = 0;
            reason = "VALID_POLICY";
            return true;
        }
    }

    private static int ClassifyFailureExitCode(string reason) => reason switch
    {
        AuthorizationFailureReasons.AuthorityUnavailable => 84,
        AuthorizationFailureReasons.MissingAuthorizationProof => 86,
        ActivationModeFailureReasons.ClaimCodeNotActivationCredential => 86,
        ActivationModeFailureReasons.ActivationModeMismatch => 86,
        ActivationModeFailureReasons.InvalidCredentialType => 86,
        ActivationModeFailureReasons.InvalidActivationMode => 86,
        _ => 85,
    };

#if BKE_STARTUP_GATE_ACCEPTANCE
    private static string? ReadPinnedAuthorityThumbprint()
    {
        return typeof(RenderDockStartupAuthorizationGate).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attr => string.Equals(attr.Key, PinnedAuthorityMetadataKey, StringComparison.Ordinal))
            ?.Value;
    }
#endif

    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(name, out var elem) || elem.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = elem.GetString() ?? string.Empty;
        return !string.IsNullOrEmpty(value);
    }

    private static bool TryGetBoundedString(JsonElement obj, string name, out string value)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(name, out var elem) || elem.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = elem.GetString();
        if (!Ed25519KeyUtilities.IsBoundedNonWhitespaceText(candidate, 128))
        {
            return false;
        }

        value = candidate!;
        return true;
    }

    private sealed record VerifiedStartupPolicy(
        string ActivationMode,
        string AccountId,
        string UserId,
        string AgentSessionId,
        string AgentKeyThumbprint,
        string InstallationId,
        string DeviceId,
        Dictionary<string, AccountProductLeaseTrustedKey> AccountKeys,
        Dictionary<string, CommercialLeaseTrustedKey> CommercialKeys);
}

internal sealed record RenderDockStartupEvaluation(
    bool Authorized,
    int ExitCode,
    string Reason,
    bool RunProtectedStartupProbe,
    string? ProtectedProbeOutputPath,
    string? ActivationMode,
    ProductAuthorizationDecision? Decision)
{
    public static RenderDockStartupEvaluation AllowLegacyAgentOnly() =>
        new(true, 0, "AUTHORIZED_LEGACY_AGENT", false, null, null, null);

    public static RenderDockStartupEvaluation AllowNativeAuthorized(
        ProductAuthorizationDecision decision,
        string activationMode,
        string? protectedProbeOutputPath) =>
        new(
            true,
            0,
            decision.Reason,
            !string.IsNullOrWhiteSpace(protectedProbeOutputPath),
            protectedProbeOutputPath,
            activationMode,
            decision);

    public static RenderDockStartupEvaluation Deny(int exitCode, string reason) =>
        new(false, exitCode, reason, false, null, null, null);
}

internal sealed record RenderDockAgentChallengeProof(
    string ChallengeId,
    string Nonce,
    string MessageHash,
    string CreatedAt,
    string ExpiresAt,
    string MessageBase64Url,
    string SignatureBase64Url,
    string AgentPublicKeyPem);

internal sealed record RenderDockStartupBundle(
    string AuthorityPublicKeyPem,
    string PolicyPayloadBase64Url,
    string PolicySignatureBase64Url,
    string PresentedCredential,
    string? AccountJws = null,
    CommercialLeaseEnvelope? CommercialEnvelope = null,
    RenderDockAgentChallengeProof? AgentChallenge = null,
    string? ProtectedProbeOutputPath = null);
