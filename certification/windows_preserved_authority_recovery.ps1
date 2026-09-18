param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('InstallAndPrepare','Prepare','Exercise','Collect')]
    [string]$Mode,
    [string]$KitRoot = '',
    [string]$EvidenceRoot = "$env:ProgramData\BKE Digital Solutions\Render Dock\preserved-authority-recovery"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# $PSScriptRoot is not reliable as a parameter-default expression when the script
# is launched through a nested powershell -File invocation. Resolve the script
# directory only after parameter binding so KitRoot never becomes an empty path.
if ([string]::IsNullOrWhiteSpace($KitRoot)) {
    $scriptPath = [string]$MyInvocation.MyCommand.Path
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        throw 'Unable to resolve recovery kit root from the current script path.'
    }
    $KitRoot = Split-Path -Parent $scriptPath
}
$KitRoot = [IO.Path]::GetFullPath($KitRoot)

$productId = 'bke-render-dock'
$productVersion = '1.0.2'
$authorizeUri = 'http://127.0.0.1:43873/v1/authorize'
$graceUri = 'https://jl-bke.com/api/graceperiod/renderdock'
$recoverableReasons = @(
    'unverifiable_signed_lease',
    'lease_version_rejected',
    'lease_expired',
    'lease_revoked',
    'lease_superseded',
    'lease_authority_mismatch'
)

$agentData = "$env:ProgramData\BKE Digital Solutions\Licensing Agent"
$agentDb = Join-Path $agentData 'agent.db'
$agentRoot = 'C:\Program Files\BKE Digital Solutions\Licensing Agent'
$serviceExe = Join-Path $agentRoot 'service\bke-licensing-agent-service.exe'
$runtimeExe = Join-Path $agentRoot 'runtime\bke-licensing-agent-runtime.exe'
$licenseCenterExe = Join-Path $agentRoot 'license-center\bke-license-center.exe'

$renderRoot = 'C:\Program Files\BKE Digital Solutions\Render Dock'
$renderExe = Join-Path $renderRoot 'RENDER DOCK.exe'
$manifestPath = Join-Path $renderRoot 'bke.manifest.json'
$installationIdPath = Join-Path $env:LOCALAPPDATA 'BKE Digital Solutions\RENDERDOCK\installation.id'

$installerName = 'Render-Dock-1.0.2-Windows-x64.exe'
$installerPath = Join-Path $KitRoot $installerName
$shaSumsPath = Join-Path $KitRoot 'SHA256SUMS'
$expectedPath = Join-Path $EvidenceRoot 'expected.json'
$resultPath = Join-Path $EvidenceRoot 'result.json'
$installerLog = Join-Path $EvidenceRoot 'render-dock-installer.log'

function Sha([string]$path) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing file: $path" }
    (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function WriteJson([string]$path, $value) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
    $value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding utf8
}

function AssertAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'InstallAndPrepare must run from elevated PowerShell.'
    }
}

function ReadInstallationId {
    if (!(Test-Path -LiteralPath $installationIdPath -PathType Leaf)) {
        throw "Render Dock installation identity is missing: $installationIdPath"
    }
    $value = (Get-Content -LiteralPath $installationIdPath -Raw).Trim()
    $parsed = [Guid]::Empty
    if (![Guid]::TryParseExact($value,'D',[ref]$parsed)) {
        throw "Invalid Render Dock installation identity: $value"
    }
    $parsed.ToString('D')
}

function AssertProduct {
    foreach ($path in @($renderExe,$manifestPath,$agentDb,$serviceExe,$runtimeExe,$licenseCenterExe)) {
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required state is missing: $path" }
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.productId -ne $productId -or $manifest.version -ne $productVersion -or $manifest.entryPoint -ne 'RENDER DOCK.exe') {
        throw "Unexpected Render Dock identity: $($manifest.productId) $($manifest.version) $($manifest.entryPoint)"
    }
}

function Authorize([string]$installationId) {
    $body = @{
        product_id = $productId
        version = $productVersion
        installation_id = $installationId
    } | ConvertTo-Json -Compress
    Invoke-RestMethod -Method Post -Uri $authorizeUri -ContentType 'application/json' -Body $body -TimeoutSec 5
}

function WaitRecovery([string]$installationId, [int]$seconds = 45) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $last = $null
    do {
        try {
            $last = Authorize $installationId
            if ($last.authorized -eq $false -and $recoverableReasons -contains [string]$last.reason) { return $last }
        } catch { $last = $null }
        Start-Sleep -Milliseconds 750
    } while ((Get-Date) -lt $deadline)
    if ($null -eq $last) { throw 'Licensing Agent did not return a usable authorization response.' }
    throw "Expected recoverable denial; got authorized=$($last.authorized) reason=$($last.reason)"
}

function AssertGraceInactive {
    try {
        $g = Invoke-RestMethod -Method Get -Uri $graceUri -TimeoutSec 3
        if ($null -ne $g -and [bool]$g.grace) { throw 'Operational grace is active; licensing recovery would be bypassed.' }
    } catch {
        if ($_.Exception.Message -like 'Operational grace is active*') { throw }
        # Render Dock itself treats grace lookup failure as grace=false.
    }
}

function ProcessByPath([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_.ExecutablePath) -and
            [string]::Equals([IO.Path]::GetFullPath([string]$_.ExecutablePath),$full,[StringComparison]::OrdinalIgnoreCase)
        }
}

function WaitProcessByPath([string]$path,[int]$seconds = 45) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $p = ProcessByPath $path | Select-Object -First 1
        if ($p) { return $p }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    $null
}

function VerifyKitInstaller {
    if (!(Test-Path -LiteralPath $installerPath -PathType Leaf)) { throw "Missing candidate installer: $installerPath" }
    if (!(Test-Path -LiteralPath $shaSumsPath -PathType Leaf)) { throw "Missing CI SHA256SUMS: $shaSumsPath" }
    $line = Get-Content -LiteralPath $shaSumsPath | Where-Object { $_ -match [Regex]::Escape($installerName) } | Select-Object -First 1
    if (!$line) { throw "SHA256SUMS does not contain $installerName" }
    $expected = (($line -split '\s+')[0]).Trim().ToLowerInvariant()
    $actual = Sha $installerPath
    if ($expected -ne $actual) { throw "Candidate installer hash mismatch. Expected=$expected Actual=$actual" }
    $actual
}

function Prepare {
    AssertProduct
    if (ProcessByPath $renderExe) { throw 'Close Render Dock before certification.' }
    if (ProcessByPath $licenseCenterExe) { throw 'Close BKE License Center before certification.' }
    AssertGraceInactive
    $installationId = ReadInstallationId
    $decision = WaitRecovery $installationId
    $expected = [ordered]@{
        schema = 'bke.render-dock-preserved-authority-recovery.v1'
        machine_name = $env:COMPUTERNAME
        prepared_at = [DateTimeOffset]::UtcNow.ToString('O')
        product_id = $productId
        product_version = $productVersion
        installation_id = $installationId
        authorize_reason_before = [string]$decision.reason
        agent_db_sha256_before = Sha $agentDb
        installation_id_sha256_before = Sha $installationIdPath
        agent_service_sha256_before = Sha $serviceExe
        agent_runtime_sha256_before = Sha $runtimeExe
        license_center_sha256_before = Sha $licenseCenterExe
        render_dock_sha256 = Sha $renderExe
    }
    WriteJson $expectedPath $expected
    Write-Host 'PREPARE: PASS'
    Write-Host "Authorization reason: $($decision.reason)"
    Write-Host "agent.db SHA256: $($expected.agent_db_sha256_before)"
    Write-Host 'No Agent reinstall and no licensing mutation were performed.'
}

if ($Mode -eq 'InstallAndPrepare') {
    AssertAdmin
    New-Item -ItemType Directory -Force $EvidenceRoot | Out-Null
    Remove-Item $expectedPath,$resultPath -Force -ErrorAction SilentlyContinue
    $installerHash = VerifyKitInstaller
    $args = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="{0}"' -f $installerLog))
    $p = Start-Process -FilePath $installerPath -ArgumentList $args -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Render Dock installer failed with exit code $($p.ExitCode). See $installerLog" }
    Write-Host "Installed exact CI candidate: $installerHash"
    Prepare
    exit 0
}

if ($Mode -eq 'Prepare') {
    New-Item -ItemType Directory -Force $EvidenceRoot | Out-Null
    Remove-Item $resultPath -Force -ErrorAction SilentlyContinue
    Prepare
    exit 0
}

if ($Mode -eq 'Exercise') {
    $result = [ordered]@{
        schema = 'bke.render-dock-preserved-authority-recovery.v1'
        status = 'FAIL'
        verified_at = [DateTimeOffset]::UtcNow.ToString('O')
        machine_name = $env:COMPUTERNAME
        checks = [ordered]@{}
        error = $null
    }
    $dock = $null
    try {
        if (!(Test-Path -LiteralPath $expectedPath -PathType Leaf)) { throw "Missing preparation evidence: $expectedPath" }
        $expected = Get-Content -LiteralPath $expectedPath -Raw | ConvertFrom-Json
        if ($expected.schema -ne 'bke.render-dock-preserved-authority-recovery.v1') { throw 'Unexpected evidence schema.' }
        if ($expected.machine_name -ne $env:COMPUTERNAME) { throw 'Evidence belongs to another machine.' }

        AssertProduct
        AssertGraceInactive
        $result.checks['operational_grace_inactive'] = $true

        $installationId = ReadInstallationId
        if ($installationId -ne [string]$expected.installation_id) { throw 'Installation identity changed after preparation.' }
        if ((Sha $agentDb) -ne [string]$expected.agent_db_sha256_before) { throw 'agent.db changed before exercise.' }
        if ((Sha $serviceExe) -ne [string]$expected.agent_service_sha256_before) { throw 'Agent service binary changed.' }
        if ((Sha $runtimeExe) -ne [string]$expected.agent_runtime_sha256_before) { throw 'Agent runtime binary changed.' }
        if ((Sha $licenseCenterExe) -ne [string]$expected.license_center_sha256_before) { throw 'License Center binary changed.' }
        if ((Sha $renderExe) -ne [string]$expected.render_dock_sha256) { throw 'Render Dock binary changed.' }
        $result.checks['baseline_hashes_preserved'] = $true

        $before = WaitRecovery $installationId
        $result.authorize_reason_before_launch = [string]$before.reason
        $result.checks['recoverable_denial_observed'] = $true

        if (ProcessByPath $renderExe) { throw 'Render Dock is already running.' }
        if (ProcessByPath $licenseCenterExe) { throw 'License Center is already running.' }

        $dock = Start-Process -FilePath $renderExe -WorkingDirectory $renderRoot -PassThru
        if (!(WaitProcessByPath $licenseCenterExe 45)) { throw 'License Center did not open.' }
        $result.checks['license_center_opened'] = $true

        Write-Host ''
        Write-Host '============================================================'
        Write-Host 'LICENSE CENTER DETECTED — CLICK CANCEL.'
        Write-Host 'DO NOT ENTER OR ACTIVATE A LICENSE IN THIS GATE.'
        Write-Host '============================================================'
        Write-Host ''

        if (!$dock.WaitForExit(120000)) { throw 'Render Dock did not exit within 120 seconds after recovery interaction.' }
        $result.render_dock_exit_code = [int]$dock.ExitCode
        $result.checks['render_dock_stayed_closed_after_cancel'] = $true

        $deadline = (Get-Date).AddSeconds(15)
        do {
            if (!(ProcessByPath $licenseCenterExe)) { break }
            Start-Sleep -Milliseconds 250
        } while ((Get-Date) -lt $deadline)
        if (ProcessByPath $licenseCenterExe) { throw 'License Center remained open after Render Dock exited.' }

        if ((Sha $agentDb) -ne [string]$expected.agent_db_sha256_before) { throw 'agent.db changed during cancel-only recovery.' }
        if ((Sha $installationIdPath) -ne [string]$expected.installation_id_sha256_before) { throw 'Installation identity changed.' }
        if ((Sha $serviceExe) -ne [string]$expected.agent_service_sha256_before) { throw 'Agent service binary changed.' }
        if ((Sha $runtimeExe) -ne [string]$expected.agent_runtime_sha256_before) { throw 'Agent runtime binary changed.' }
        if ((Sha $licenseCenterExe) -ne [string]$expected.license_center_sha256_before) { throw 'License Center binary changed.' }
        $result.checks['real_authority_state_untouched'] = $true
        $result.checks['agent_installation_untouched'] = $true

        $after = WaitRecovery $installationId
        $result.authorize_reason_after_cancel = [string]$after.reason
        $result.checks['authorization_remains_denied_after_cancel'] = $true
        $result.agent_db_sha256_after = Sha $agentDb
        $result.status = 'PASS'
    } catch {
        $result.error = $_.Exception.Message
        if ($null -ne $dock -and !$dock.HasExited) {
            try { Stop-Process -Id $dock.Id -Force -ErrorAction SilentlyContinue } catch {}
        }
    } finally {
        WriteJson $resultPath $result
    }

    if ($result.status -ne 'PASS') {
        Write-Error "PRESERVED-AUTHORITY RECOVERY FAILED: $($result.error)"
        exit 1
    }
    Write-Host 'EXERCISE: PASS'
    Write-Host 'License Center opened, cancel kept Render Dock closed, and real Agent state remained unchanged.'
    exit 0
}

if ($Mode -eq 'Collect') {
    if (!(Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw "No result found: $resultPath" }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $result | ConvertTo-Json -Depth 12
    if ($result.status -ne 'PASS') { throw "Certification did not pass: $($result.error)" }
    exit 0
}
