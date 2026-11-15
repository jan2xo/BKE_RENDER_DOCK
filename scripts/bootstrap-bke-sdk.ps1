[CmdletBinding()]
param(
    [string]$SdkCommit = 'e34431f5400aab00e87549b8aae8dedc3104f4bb',
    [string]$DotnetLibrariesCommit = '911ff400de4931f2bd7e80f86a0eecee914f2021',
    [string]$TypeScriptContractCommit = '0a6d0151e93acdfb5bd4435b64a7dbf53d010375',
    [switch]$CertifyStartupGateClient
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedBkeLicensingTreeSha = 'c9461f9a5d847c9b8c6230901f02c0469e581434'
$BkeLicensingPackageVersion = '0.0.0-source.911ff400de49'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packageDirectory = Join-Path $repositoryRoot 'packages'
$workDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("bke-sdk-" + $SdkCommit.Substring(0, 12))
$dotnetLibWorkDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("bke-dotnet-libs-" + $DotnetLibrariesCommit.Substring(0, 12))
$tsContractWorkDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("bke-ts-contract-" + $TypeScriptContractCommit.Substring(0, 12))

foreach ($dir in @($workDirectory, $dotnetLibWorkDirectory, $tsContractWorkDirectory)) {
    if (Test-Path -LiteralPath $dir) {
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $workDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $dotnetLibWorkDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

try {
    git -C $workDirectory init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Failed to initialize temporary BKE SDK checkout.' }

    git -C $workDirectory remote add origin 'https://github.com/jan2xo/bke-sdk.git'
    if ($LASTEXITCODE -ne 0) { throw 'Failed to configure BKE SDK origin.' }

    git -C $workDirectory fetch --quiet --depth 1 origin $SdkCommit
    if ($LASTEXITCODE -ne 0) { throw "Failed to fetch canonical BKE SDK commit $SdkCommit." }

    git -C $workDirectory checkout --quiet --detach FETCH_HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Failed to check out canonical BKE SDK commit.' }

    $resolved = (git -C $workDirectory rev-parse HEAD).Trim()
    if ($resolved -ne $SdkCommit) {
        throw "BKE SDK checkout mismatch. Expected $SdkCommit but resolved $resolved."
    }

    git -C $dotnetLibWorkDirectory init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Failed to initialize temporary bke-dotnet-libraries checkout.' }

    git -C $dotnetLibWorkDirectory remote add origin 'https://github.com/jan2xo/bke-dotnet-libraries.git'
    if ($LASTEXITCODE -ne 0) { throw 'Failed to configure bke-dotnet-libraries origin.' }

    git -C $dotnetLibWorkDirectory fetch --quiet --depth 1 origin $DotnetLibrariesCommit
    if ($LASTEXITCODE -ne 0) { throw "Failed to fetch canonical bke-dotnet-libraries commit $DotnetLibrariesCommit." }

    git -C $dotnetLibWorkDirectory checkout --quiet --detach FETCH_HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Failed to check out canonical bke-dotnet-libraries commit.' }

    $resolvedDotnet = (git -C $dotnetLibWorkDirectory rev-parse HEAD).Trim()
    if ($resolvedDotnet -ne $DotnetLibrariesCommit) {
        throw "bke-dotnet-libraries checkout mismatch. Expected $DotnetLibrariesCommit but resolved $resolvedDotnet."
    }

    $resolvedLicensingTree = (git -C $dotnetLibWorkDirectory rev-parse 'HEAD:src/BKE.Licensing').Trim()
    if ($resolvedLicensingTree -ne $ExpectedBkeLicensingTreeSha) {
        throw "BKE.Licensing source tree mismatch. Expected $ExpectedBkeLicensingTreeSha but resolved $resolvedLicensingTree."
    }

    Remove-Item -LiteralPath (Join-Path $packageDirectory 'BKE.Desktop.Licensing.2.0.1.nupkg') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $packageDirectory 'BKE.Desktop.Licensing.2.0.2.nupkg') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $packageDirectory 'BKE.Notifications.0.5.0.nupkg') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $packageDirectory 'BKE.Updater.0.4.0.nupkg') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $packageDirectory "BKE.Licensing.$BkeLicensingPackageVersion.nupkg") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $packageDirectory 'bke-licensing-provenance.json') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $packageDirectory 'ts-renderdock-live-interop.json') -Force -ErrorAction SilentlyContinue

    if ($CertifyStartupGateClient) {
        dotnet test (Join-Path $workDirectory 'tests/BKE.Desktop.Licensing.Tests/BKE.Desktop.Licensing.Tests.csproj') `
            --configuration Release --verbosity normal
        if ($LASTEXITCODE -ne 0) { throw 'Exact pinned SDK failed native Windows startup-gate denial tests.' }

        dotnet test (Join-Path $dotnetLibWorkDirectory 'tests/BKE.Licensing.Tests/BKE.Licensing.Tests.csproj') `
            --configuration Release --verbosity normal
        if ($LASTEXITCODE -ne 0) { throw 'Exact pinned bke-dotnet-libraries failed unit/adversarial tests.' }

        New-Item -ItemType Directory -Path $tsContractWorkDirectory -Force | Out-Null
        git -C $tsContractWorkDirectory init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Failed to initialize temporary bke-libraries-typescript checkout.' }

        git -C $tsContractWorkDirectory remote add origin 'https://github.com/jan2xo/bke-libraries-typescript.git'
        if ($LASTEXITCODE -ne 0) { throw 'Failed to configure bke-libraries-typescript origin.' }

        git -C $tsContractWorkDirectory fetch --quiet --depth 1 origin $TypeScriptContractCommit
        if ($LASTEXITCODE -ne 0) { throw "Failed to fetch canonical bke-libraries-typescript commit $TypeScriptContractCommit." }

        git -C $tsContractWorkDirectory checkout --quiet --detach FETCH_HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Failed to check out canonical bke-libraries-typescript commit.' }

        $resolvedTs = (git -C $tsContractWorkDirectory rev-parse HEAD).Trim()
        if ($resolvedTs -ne $TypeScriptContractCommit) {
            throw "bke-libraries-typescript checkout mismatch. Expected $TypeScriptContractCommit but resolved $resolvedTs."
        }

        Push-Location $tsContractWorkDirectory
        try {
            npm install --no-save --no-package-lock tsx@4.20.5
            if ($LASTEXITCODE -ne 0) { throw 'Failed to install pinned tsx runtime for TS->.NET interop proof.' }
        }
        finally {
            Pop-Location
        }

        $oldTsRepoEnv = $env:BKE_TS_REPO_PATH
        try {
            $env:BKE_TS_REPO_PATH = $tsContractWorkDirectory
            dotnet test (Join-Path $dotnetLibWorkDirectory 'tests/BKE.Licensing.Interop.Tests/BKE.Licensing.Interop.Tests.csproj') `
                --configuration Release --verbosity normal
            if ($LASTEXITCODE -ne 0) { throw 'Pinned TypeScript -> .NET interoperability tests failed.' }

            $interopScriptPath = Join-Path $tsContractWorkDirectory 'mint-renderdock-interop.mjs'
            $interopOutputPath = Join-Path $packageDirectory 'ts-renderdock-live-interop.json'
            $interopScript = @'
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
  typeScriptContractCommit: process.env.BKE_TS_COMMIT,
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
}, null, 2), 'utf8');
'@
            Set-Content -LiteralPath $interopScriptPath -Value $interopScript -Encoding UTF8
            $env:BKE_RD_INTEROP_OUT = $interopOutputPath
            $env:BKE_TS_COMMIT = $TypeScriptContractCommit
            & node (Join-Path $tsContractWorkDirectory 'node_modules/tsx/dist/cli.mjs') $interopScriptPath
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $interopOutputPath)) {
                throw 'Failed to mint live TypeScript Render Dock interop fixture.'
            }
        }
        finally {
            $env:BKE_TS_REPO_PATH = $oldTsRepoEnv
            Remove-Item Env:BKE_RD_INTEROP_OUT -ErrorAction SilentlyContinue
            Remove-Item Env:BKE_TS_COMMIT -ErrorAction SilentlyContinue
        }
    }

    dotnet pack (Join-Path $workDirectory 'src/BKE.Desktop.Licensing/BKE.Desktop.Licensing.csproj') `
        --configuration Release `
        --output $packageDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build BKE.Desktop.Licensing 2.0.2 from canonical SDK source.' }

    dotnet pack (Join-Path $workDirectory 'src/BKE.Notifications/BKE.Notifications.csproj') `
        --configuration Release `
        --output $packageDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build BKE.Notifications 0.5.0 from canonical SDK source.' }

    dotnet pack (Join-Path $workDirectory 'src/BKE.Updater/BKE.Updater.csproj') `
        --configuration Release `
        --output $packageDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build BKE.Updater 0.4.0 from canonical SDK source.' }

    dotnet pack (Join-Path $dotnetLibWorkDirectory 'src/BKE.Licensing/BKE.Licensing.csproj') `
        --configuration Release `
        "-p:Version=$BkeLicensingPackageVersion" `
        "-p:SourceRevisionId=$DotnetLibrariesCommit" `
        --output $packageDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build BKE.Licensing from canonical bke-dotnet-libraries source.' }

    $licensing = Join-Path $packageDirectory 'BKE.Desktop.Licensing.2.0.2.nupkg'
    $notifications = Join-Path $packageDirectory 'BKE.Notifications.0.5.0.nupkg'
    $updater = Join-Path $packageDirectory 'BKE.Updater.0.4.0.nupkg'
    $bkeLicensingPkg = Join-Path $packageDirectory 'BKE.Licensing.0.0.0-source.911ff400de49.nupkg'
    if (-not (Test-Path -LiteralPath $licensing -PathType Leaf)) {
        throw 'BKE.Desktop.Licensing 2.0.2 package was not produced.'
    }
    if (-not (Test-Path -LiteralPath $notifications -PathType Leaf)) {
        throw 'BKE.Notifications 0.5.0 package was not produced.'
    }
    if (-not (Test-Path -LiteralPath $updater -PathType Leaf)) {
        throw 'BKE.Updater 0.4.0 package was not produced.'
    }
    if (-not (Test-Path -LiteralPath $bkeLicensingPkg -PathType Leaf)) {
        throw "BKE.Licensing $BkeLicensingPackageVersion package was not produced."
    }

    $bkeLicensingPkgHash = (Get-FileHash -LiteralPath $bkeLicensingPkg -Algorithm SHA256).Hash.ToLowerInvariant()
    $bkeLicensingDll = Join-Path $dotnetLibWorkDirectory 'src/BKE.Licensing/bin/Release/net10.0/BKE.Licensing.dll'
    $bkeLicensingDllHash = if (Test-Path -LiteralPath $bkeLicensingDll -PathType Leaf) {
        (Get-FileHash -LiteralPath $bkeLicensingDll -Algorithm SHA256).Hash.ToLowerInvariant()
    } else {
        'NOT_FOUND'
    }

    $provenance = [ordered]@{
        schema = 'bke.render-dock.licensing-provenance.v1'
        dotnetLibrariesCommit = $DotnetLibrariesCommit
        bkeLicensingTreeSha = $ExpectedBkeLicensingTreeSha
        typeScriptContractCommit = $TypeScriptContractCommit
        packageId = 'BKE.Licensing'
        packageVersion = $BkeLicensingPackageVersion
        nupkgFilename = "BKE.Licensing.$BkeLicensingPackageVersion.nupkg"
        nupkgSha256 = $bkeLicensingPkgHash
        dllSha256 = $bkeLicensingDllHash
    }
    $provenance | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $packageDirectory 'bke-licensing-provenance.json') -Encoding UTF8

    Write-Host "Prepared canonical BKE SDK packages from $SdkCommit"
    Write-Host " - $licensing"
    Write-Host " - $notifications"
    Write-Host " - $updater"
    Write-Host "Prepared canonical BKE.Licensing development package from $DotnetLibrariesCommit (tree=$ExpectedBkeLicensingTreeSha)"
    Write-Host " - $bkeLicensingPkg (sha256=$bkeLicensingPkgHash)"
}
finally {
    foreach ($dir in @($workDirectory, $dotnetLibWorkDirectory, $tsContractWorkDirectory)) {
        if (Test-Path -LiteralPath $dir) {
            Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
