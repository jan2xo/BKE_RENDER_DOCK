# Read-only test on an OPERATOR-APPROVED disposable Windows installation.
# No machine enrollment, key creation, policy changes or service manipulation.
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ExpectedRenderDockSourceSha,
  [Parameter(Mandatory)][string]$ExpectedAgentSourceSha,
  [Parameter(Mandatory)][string]$ExpectedExecutableSha256,
  [Parameter(Mandatory)][ValidateSet('ACCOUNT','LICENSE_KEY','EITHER')][string]$ExpectedActivationMode,
  [Parameter(Mandatory)][ValidateSet('ACCOUNT','LICENSE_KEY')][string]$ExpectedCredential,
  [Parameter(Mandatory)][string]$EvidenceDirectory,
  [switch]$ApprovedDisposableMachine
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if (!$ApprovedDisposableMachine) { throw 'Explicit disposable-machine approval is mandatory.' }
if (-not $IsWindows -or -not ([Environment]::Is64BitOperatingSystem)) { throw 'Windows x64 only.' }
foreach ($sha in @($ExpectedRenderDockSourceSha, $ExpectedAgentSourceSha)) {
  if ($sha -cnotmatch '^[a-f0-9]{40}$') { throw 'Must provide immutable source SHAs.' }
}
if ($ExpectedExecutableSha256 -cnotmatch '^[a-f0-9]{64}$') { throw 'Must provide SHA256 of exact executable.' }
# The signed DS policy controls mode; EITHER is not a third credential.
if ($ExpectedActivationMode -cne 'EITHER' -and
    $ExpectedActivationMode -cne $ExpectedCredential) {
  throw 'Selected credential is incompatible with required signed activation mode.'
}
$operator = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$operator.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw 'Protected Agent grant audit must be inspected by approved Administrator.'
}
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source = (git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $source -cne $ExpectedRenderDockSourceSha) {
  throw 'Acceptance script source is not checked out at the pinned Render Dock SHA.'
}
$programFiles = [Environment]::GetFolderPath('ProgramFiles')
$root = Join-Path $programFiles 'BKE Digital Solutions'
$productRoot = Join-Path $root 'Render Dock'
$agentRoot = Join-Path $root 'Licensing Agent'
$exe = Join-Path $productRoot 'RENDER DOCK.exe'
$marker = Join-Path $productRoot 'bke-startup-acceptance.json'
$runtime = Join-Path $agentRoot 'runtime/bke-licensing-agent-runtime.exe'
$serviceExecutable = Join-Path $agentRoot 'service/bke-licensing-agent-service.exe'
$trust = Join-Path $agentRoot 'trust/account-product-lease-keys/issuers.json'
$policiesDir = Join-Path $agentRoot 'trust/standalone-startup-policies'
$ledger = Join-Path $agentRoot 'startup-grant-audit/grants.db'
$verifier = Join-Path $PSScriptRoot 'verify-installed-agent-startup-grant.py'

foreach ($directory in @($root,$productRoot,$agentRoot,
  (Join-Path $agentRoot 'runtime'),(Join-Path $agentRoot 'service'),
  (Join-Path $agentRoot 'trust'),
  (Join-Path $agentRoot 'trust/account-product-lease-keys'),
  $policiesDir,(Join-Path $agentRoot 'startup-grant-audit'))) {
  if (!(Test-Path -LiteralPath $directory -PathType Container) -or
      ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Expected protected installed folder missing or redirected.'
  }
}
foreach ($file in @($exe,$marker,$runtime,$serviceExecutable,$trust,$ledger,$verifier)) {
  if (!(Test-Path -LiteralPath $file -PathType Leaf) -or
      ((Get-Item -LiteralPath $file).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw "Expected read-only acceptance input missing or redirected: $file"
  }
}
$actual = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -cne $ExpectedExecutableSha256) {
  throw 'Signed-policy target executable hash differs from expected.'
}
# This marker is an OPERATOR declaration, not an Agent trust source. It
# prevents accidental testing of an unmodified customer shipping executable.
$declaration = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
if ($declaration.schema -cne 'bke.renderdock-startup-acceptance-marker.v1' -or
    $declaration.purpose -cne 'NONSHIPPABLE' -or
    $declaration.source_sha -cne $source -or
    $declaration.image_sha256 -cne $actual) {
  throw 'Missing exact-byte NONSHIPPABLE acceptance declaration.'
}
# Independent operator preflight: the SDK additionally authenticates its
# named-pipe server's OS identity before and after the connected verdict.
$service = Get-CimInstance Win32_Service -Filter "Name='BKE-Licensing-Agent'"
if (!$service -or $service.State -ne 'Running' -or $service.ProcessId -lt 1 -or
    $service.StartName -notin @('LocalSystem','NT AUTHORITY\SYSTEM')) {
  throw 'Actual installed Agent Windows service must run as LocalSystem.'
}
# Canonical Inno sc.exe registration contains an absolute EXE path only.
# Strip optional balanced outer quotes but reject extra arguments or redirects.
$scmImage = ([string]$service.PathName).Trim()
if ($scmImage.StartsWith('"') -and $scmImage.EndsWith('"')) {
  $scmImage = $scmImage.Substring(1,$scmImage.Length-2)
}
if (!$scmImage -or $scmImage.Contains('"') -or
    ![string]::Equals([IO.Path]::GetFullPath($scmImage),
        [IO.Path]::GetFullPath($serviceExecutable),
        [StringComparison]::OrdinalIgnoreCase)) {
  throw 'Installed Agent SCM image does not exactly match protected canonical executable.'
}
$serviceProcess = @(Get-CimInstance Win32_Process -Filter "ProcessId=$($service.ProcessId)")
if ($serviceProcess.Count -ne 1 -or !$serviceProcess[0].ExecutablePath -or
    $serviceProcess[0].SessionId -ne 0 -or
    ![string]::Equals([IO.Path]::GetFullPath($serviceProcess[0].ExecutablePath),
        [IO.Path]::GetFullPath($serviceExecutable),
        [StringComparison]::OrdinalIgnoreCase)) {
  throw 'Actual Agent service process must match SCM and Windows Session 0.'
}
$serviceOwner = Invoke-CimMethod -InputObject $serviceProcess[0] -MethodName GetOwner
if ($serviceOwner.ReturnValue -ne 0 -or
    $serviceOwner.User -cne 'SYSTEM' -or
    $serviceOwner.Domain -cne 'NT AUTHORITY') {
  throw 'Installed Agent service process must have LocalSystem token.'
}
$active = @(Get-CimInstance Win32_Process -Filter "Name='bke-licensing-agent-runtime.exe'")
$trustedProcesses = @($active | Where-Object {
  $_.ExecutablePath -and
  [string]::Equals([IO.Path]::GetFullPath($_.ExecutablePath),
                   [IO.Path]::GetFullPath($runtime),
                   [StringComparison]::OrdinalIgnoreCase)
})
if ($trustedProcesses.Count -ne 1) {
  throw 'Exactly one protected installed Agent runtime process is required.'
}
$runtimeProcess = $trustedProcesses[0]
if ($runtimeProcess.SessionId -ne 0 -or
    $runtimeProcess.ParentProcessId -ne $service.ProcessId) {
  throw 'Installed Agent runtime must be the Session 0 child of canonical service.'
}
$runtimeOwner = Invoke-CimMethod -InputObject $runtimeProcess -MethodName GetOwner
if ($runtimeOwner.ReturnValue -ne 0 -or
    $runtimeOwner.User -cne 'SYSTEM' -or
    $runtimeOwner.Domain -cne 'NT AUTHORITY') {
  throw 'Installed Agent runtime process must have LocalSystem token.'
}

# Inspection alone cannot verify the signature. The actual Agent MUST
# validate signed DS mode/issuer/target before a grant can be returned.
# Correlate the signed payload's scoped installation/user to the independent
# protected Agent audit, not merely to the executable's visible hash.
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$matchingPolicies = @()
foreach ($policy in @(Get-ChildItem -LiteralPath $policiesDir -Filter '*.json' -File)) {
  if ($policy.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
  try {
    $envelope = Get-Content -LiteralPath $policy.FullName -Raw | ConvertFrom-Json
    $signed = $envelope.payload | ConvertFrom-Json
    if ($signed.product_id -ceq 'bke-render-dock' -and
        $signed.version -ceq '1.0.3' -and
        $signed.activation_mode -ceq $ExpectedActivationMode -and
        $signed.image_sha256 -ceq $actual -and
        $signed.allowed_user_sid -ceq $sid -and
        [string]::Equals($signed.image_path,$exe,[StringComparison]::OrdinalIgnoreCase)) {
      $matchingPolicies += $signed
    }
  } catch {
    throw 'Malformed installed DS startup policy; do not rewrite policy during acceptance.'
  }
}
if ($matchingPolicies.Count -ne 1) { throw 'Exactly one matching installed DS signed policy is required.' }
$expectedInstallationId = [string]$matchingPolicies[0].installation_id
if ($expectedInstallationId -cnotmatch '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$') {
  throw 'Installed DS signed policy has no canonical installation ID.'
}
if (!(Get-Command py -ErrorAction SilentlyContinue)) {
  throw 'Python 3 Windows launcher is required to read Agent grant SQLite without mutation.'
}
$pythonVersion = & py -3 --version
if ($LASTEXITCODE -ne 0 -or "$pythonVersion" -notmatch '^Python 3\.') {
  throw 'Python 3 not available.'
}
$reportRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
if ($reportRoot.StartsWith($root + [IO.Path]::DirectorySeparatorChar,
                          [StringComparison]::OrdinalIgnoreCase)) {
  throw 'Evidence must not be written inside the installed product or Agent.'
}

$app = Start-Process -FilePath $exe -PassThru
try {
  $processId = $app.Id
  $startedTicks = $app.StartTime.ToUniversalTime().Ticks
  if (!$app.WaitForExit(20000)) { throw 'Acceptance executable did not complete its startup verdict.' }
  if ($app.ExitCode -ne 0) {
    throw "Installed Agent denied or unavailable; acceptance exit code $($app.ExitCode)."
  }
  $pythonArgs = @(
    '-3','-B',$verifier,'--db',$ledger,'--pid',[string]$processId,
    '--started-ticks',[string]$startedTicks,'--sid',$sid,
    '--sha256',$actual,'--product','bke-render-dock','--version','1.0.3',
    '--installation-id',$expectedInstallationId,
    '--signed-activation-mode',$ExpectedActivationMode,
    '--expected-credential',$ExpectedCredential
  )
  $raw = & py @pythonArgs
  if ($LASTEXITCODE -ne 0) { throw 'Agent durable grant audit lacked the exact connected process.' }
  $grant = "$raw" | ConvertFrom-Json
  if ($grant.status -cne 'PASS' -or
      $grant.durable_ledger -cne 'verified_exact_process') {
    throw 'Malformed durable Agent grant acceptance evidence.'
  }

  New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
  $report = [ordered]@{
    schema='bke.renderdock-startup-installed-acceptance.v1'
    result='PASS'
    product_source_sha=$source
    agent_source_sha_operator_claim=$ExpectedAgentSourceSha
    installed_agent_release_provenance='NOT_PROVEN'
    installed_agent_service='EXACT_SYSTEM_SESSION0'
    installed_agent_runtime='EXACT_SYSTEM_CHILD_SESSION0'
    genuine_ds_issuer_origin='NOT_INDEPENDENTLY_PROVEN'
    physical_tpm_provenance='NOT_PROVEN'
    signed_policy='AGENT_VALIDATED'
    installation_binding='SIGNED_POLICY_EQUALS_AGENT_GRANT'
    signed_activation_mode=$ExpectedActivationMode
    credential=$grant.credential
    exact_exe_sha256=$actual
    durable_grant=$grant.durable_ledger
    process_id=$processId
    started_utc_ticks=$startedTicks
    sid_sha256=$grant.sid_sha256
    timestamp_utc=[DateTime]::UtcNow.ToString('O')
  }
  $resultPath = Join-Path $reportRoot (
    'renderdock-installed-acceptance-'+[Guid]::NewGuid().ToString('N')+'.json')
  $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resultPath -Encoding utf8
  Write-Host "PASS: installed Agent granted exact Render Dock process and durably audited: $resultPath"
} finally {
  if (!$app.HasExited) { $app.Kill(); $app.WaitForExit() }
  $app.Dispose()
}
