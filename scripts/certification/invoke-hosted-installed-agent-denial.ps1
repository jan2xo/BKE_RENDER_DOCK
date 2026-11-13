# NONSHIPPABLE GitHub-hosted Windows SYSTEM Agent / Render Dock denial certification.
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ExpectedRenderDockSourceSha,
  [Parameter(Mandatory)][string]$ExpectedAgentSourceSha,
  [Parameter(Mandatory)][string]$AcceptanceBuildDirectory,
  [switch]$ApprovedDisposableWindowsRunner
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if (!$ApprovedDisposableWindowsRunner -or $env:GITHUB_ACTIONS -cne 'true' -or
    $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or
    $env:RUNNER_OS -cne 'Windows' -or !$IsWindows -or
    ![Environment]::Is64BitOperatingSystem -or !$env:RUNNER_TEMP) {
  throw 'Only explicitly approved disposable Windows x64 GitHub Actions.'
}
foreach ($sha in @($ExpectedRenderDockSourceSha,$ExpectedAgentSourceSha)) {
  if ($sha -cnotmatch '^[a-f0-9]{40}$') { throw 'Exact Git SHAs required.' }
}
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($env:SOURCE_SHA -cne $ExpectedRenderDockSourceSha -or
    (git -C $repo rev-parse HEAD).Trim() -cne $ExpectedRenderDockSourceSha) {
  throw 'Exact Render Dock reviewed head required.'
}
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw 'Windows hosted installation requires elevated disposable runner.'
}
$programFiles=[Environment]::GetFolderPath('ProgramFiles')
$agentRoot=Join-Path $programFiles 'BKE Digital Solutions\Licensing Agent'
$productRoot=Join-Path $programFiles 'BKE Digital Solutions\Render Dock'
$agentData=Join-Path $env:ProgramData 'BKE Digital Solutions\Licensing Agent'
$service='BKE-Licensing-Agent'
$realImage=Join-Path $productRoot 'RENDER DOCK.exe'
$runtime=Join-Path $agentRoot 'runtime\bke-licensing-agent-runtime.exe'
if ((Get-Service -Name $service -ErrorAction SilentlyContinue) -or
    (Test-Path -LiteralPath $agentRoot) -or (Test-Path -LiteralPath $productRoot) -or
    (Test-Path -LiteralPath $agentData)) {
  throw 'Preexisting installed BKE state; do not overwrite or modify.'
}
$build=[IO.Path]::GetFullPath($AcceptanceBuildDirectory)
$temp=[IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\')
if (!$build.StartsWith($temp+'\',[StringComparison]::OrdinalIgnoreCase)) {
  throw 'Accept only NONSHIPPABLE image built into RUNNER_TEMP.'
}
$sourceImage=Join-Path $build 'RENDER DOCK.exe'
if (!(Test-Path -LiteralPath $sourceImage -PathType Leaf)) { throw 'Real Render Dock image absent.' }
$imageSha=(Get-FileHash -LiteralPath $sourceImage -Algorithm SHA256).Hash.ToLowerInvariant()
$agentSource=Join-Path $env:RUNNER_TEMP ('bke-agent-src-'+[Guid]::NewGuid().ToString('N'))
$fixture=Join-Path $env:RUNNER_TEMP ('bke-agent-fixture-'+[Guid]::NewGuid().ToString('N'))
$installLog=Join-Path $env:RUNNER_TEMP 'agent-disposable-install.log'
$installed=$false
$createdProduct=$false
$createdData=$false
$oldEnvironment=$env:BKE_ENVIRONMENT
$oldOrigin=$env:BKE_PLATFORM_BASE_URL

try {
  New-Item -ItemType Directory -Path $productRoot -Force | Out-Null
  $createdProduct=$true
  Get-ChildItem -LiteralPath $build -Force | Copy-Item -Destination $productRoot -Force -Recurse
  if ((Get-FileHash -LiteralPath $realImage -Algorithm SHA256).Hash.ToLowerInvariant() -cne $imageSha) {
    throw 'Installed real Render Dock image hash drift.'
  }
  New-Item -ItemType Directory -Path $agentData -Force | Out-Null
  $createdData=$true
  $origin='http://127.0.0.1:43999'
  $config="BKE_ENVIRONMENT=certification" + [Environment]::NewLine +
    "BKE_PLATFORM_BASE_URL=$origin" + [Environment]::NewLine +
    "BKE_STANDALONE_STARTUP_GATE_ENABLED=1" + [Environment]::NewLine
  [IO.File]::WriteAllText((Join-Path $agentData '.env'),$config,[Text.UTF8Encoding]::new($false))
  $env:BKE_ENVIRONMENT='certification'
  $env:BKE_PLATFORM_BASE_URL=$origin
  New-Item -ItemType Directory -Path $agentSource -Force | Out-Null
  & git -C $agentSource init --quiet
  if ($LASTEXITCODE -ne 0) { throw 'Git init failed.' }
  & git -C $agentSource remote add origin 'https://github.com/jan2xo/bke-licensing-agent.git'
  if ($LASTEXITCODE -ne 0) { throw 'Git origin failed.' }
  & git -C $agentSource fetch --quiet --depth 1 origin $ExpectedAgentSourceSha
  if ($LASTEXITCODE -ne 0) { throw 'Cannot fetch immutable Agent source SHA.' }
  & git -C $agentSource checkout --quiet --detach FETCH_HEAD
  if ($LASTEXITCODE -ne 0 -or
      (git -C $agentSource rev-parse HEAD).Trim() -cne $ExpectedAgentSourceSha) {
    throw 'Agent checkout SHA drift.'
  }
  Push-Location $agentSource
  try {
    & dotnet run --project 'dotnet/certification/BKE.LicensingAgent.ContractCertification/BKE.LicensingAgent.ContractCertification.csproj' --configuration Release -- --emit-disposable-installed-startup-policy $fixture $sourceImage $realImage
    if ($LASTEXITCODE -ne 0) { throw 'Agent ephemeral public-only signed fixture creation failed.' }
    $pub=Join-Path $fixture 'account-product-lease-keys\issuers.json'
    $policy=Join-Path $fixture 'standalone-startup-policies\bke-render-dock.json'
    if (!(Test-Path -LiteralPath $pub) -or !(Test-Path -LiteralPath $policy) -or
        (Get-Content $pub -Raw) -match 'PRIVATE KEY' -or
        (Get-Content $policy -Raw) -match 'PRIVATE KEY') {
      throw 'Missing public-only issuer or signed policy, or private key leaked.'
    }
    & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File 'scripts\publish_dotnet_windows_payloads.ps1' -Version 2.0.0 -Architecture x64
    if ($LASTEXITCODE -ne 0) { throw 'Agent .NET-only support payload build failed.' }
    & dotnet run --project 'dotnet/tools/BKE.LicensingAgent.ReleaseTooling/BKE.LicensingAgent.ReleaseTooling.csproj' --configuration Release -- generate-disposable-trust --root dist/windows --target-key-id ci-target-only --policy-id ci-installed-only --update-key-id ci-update-only
    if ($LASTEXITCODE -ne 0) { throw 'Agent installer fixture trust generation failed.' }
    foreach ($target in @(
      @{project='dotnet/src/BKE.LicensingAgent.Bootstrap/BKE.LicensingAgent.Bootstrap.csproj';output='dist/windows/bke-licensing-agent-service';extra=@('-p:PublishSingleFile=true')},
      @{project='dotnet/src/BKE.LicensingAgent.Host/BKE.LicensingAgent.Host.csproj';output='dist/windows/bke-licensing-agent-runtime';extra=@('-p:PublishSingleFile=true','-p:BKEAgentRuntimeBridge=true')}
    )) {
      New-Item -ItemType Directory -Path $target.output -Force | Out-Null
      $args=@('publish',$target.project,'--configuration','Release','--runtime','win-x64',
        '--self-contained','true','-p:Version=2.0.0','--output',$target.output)+$target.extra
      & dotnet @args
      if ($LASTEXITCODE -ne 0) { throw "Agent service/runtime publish failed: $($target.project)" }
    }
    New-Item -ItemType Directory -Path 'dist/windows/account-product-lease-keys' -Force | Out-Null
    New-Item -ItemType Directory -Path 'dist/windows/standalone-startup-policies' -Force | Out-Null
    Copy-Item -LiteralPath $pub -Destination 'dist/windows/account-product-lease-keys/issuers.json'
    Copy-Item -LiteralPath $policy -Destination 'dist/windows/standalone-startup-policies/bke-render-dock.json'
    $programFilesX86=[Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    $iscc=Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'
    if (!(Test-Path -LiteralPath $iscc)) {
      & choco install innosetup -y --no-progress
      if ($LASTEXITCODE -ne 0) { throw 'Inno Setup installation on hosted runner failed.' }
    }
    & $iscc 'packaging/windows/bke-licensing-agent.iss'
    if ($LASTEXITCODE -ne 0) { throw 'Agent canonical installer compilation failed.' }
    $installer=(Resolve-Path -LiteralPath 'dist/installer/BKE-Licensing-Agent-2.0.0-Windows-x64.exe').Path
    $installerHash=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
  } finally { Pop-Location }

  $installed=$true
  $setup=Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=$installLog") -Wait -PassThru
  if ($setup.ExitCode -ne 0) { throw "Canonical Agent disposable install failed: $($setup.ExitCode)" }
  # Installer exits only after startup health, but SCM/WMI state can lag.
  # Bound the post-install observation window; never reinterpret a stopped
  # service, wrong account, or wrong image path as a successful installation.
  $serviceDeadline=[DateTime]::UtcNow.AddSeconds(35)
  $svc=$null
  do {
    $svc=Get-CimInstance Win32_Service -Filter "Name='$service'"
    if ($svc -and $svc.State -eq 'Running' -and
        $svc.StartName -in @('LocalSystem','NT AUTHORITY\SYSTEM') -and
        $svc.PathName -match 'service\\bke-licensing-agent-service\.exe') {
      break
    }
    Start-Sleep -Milliseconds 750
  } while ([DateTime]::UtcNow -lt $serviceDeadline)
  if (!$svc -or $svc.State -ne 'Running' -or
      $svc.StartName -notin @('LocalSystem','NT AUTHORITY\SYSTEM') -or
      $svc.PathName -notmatch 'service\\bke-licensing-agent-service\.exe') {
    # Collect bounded, sanitized installer/SCM evidence ONLY on failure.
    # Do not print an entire installer log or environment/issuer contents.
    $state=if ($svc) { [string]$svc.State } else { 'MISSING' }
    $identity=if ($svc) { [string]$svc.StartName } else { 'MISSING' }
    $imageMatches=if ($svc) {
      [bool]($svc.PathName -match 'service\\bke-licensing-agent-service\.exe')
    } else { $false }
    $pidValue=if ($svc) { [int]$svc.ProcessId } else { 0 }
    $registration=Test-Path -LiteralPath (
      'Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\' + $service)
    $canonicalServiceExe=Test-Path -LiteralPath (
      Join-Path $agentRoot 'service\bke-licensing-agent-service.exe')
    $canonicalRuntimeExe=Test-Path -LiteralPath $runtime
    $x86Root=Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) (
      'BKE Digital Solutions\Licensing Agent')
    $x86ServiceExe=Test-Path -LiteralPath (
      Join-Path $x86Root 'service\bke-licensing-agent-service.exe')
    $knownServices=@(Get-CimInstance Win32_Service |
      Where-Object { $_.Name -like 'BKE*' } |
      Select-Object -ExpandProperty Name)
    $logExists=Test-Path -LiteralPath $installLog
    $logFlags=@()
    if ($logExists) {
      $logLines=@(Get-Content -LiteralPath $installLog -ErrorAction Stop)
      foreach ($rule in @(
        @{Name='setup_started';Pattern='Starting the installation process'},
        @{Name='installer_exception';Pattern='(?i)exception|aborted|could not execute|fatal error'},
        @{Name='health_failed';Pattern='(?i)replacement runtime failed|did not recover local API health'},
        @{Name='service_create';Pattern='(?i)service registration|sc.exe.*create'},
        @{Name='service_start';Pattern='(?i)service startup|sc.exe.*start'},
        @{Name='service_removed';Pattern='(?i)failed fresh-install cleanup|sc.exe.*delete'},
        @{Name='setup_finished';Pattern='(?i)installation process succeeded|installation process finished|log closed'}
      )) {
        $count=@($logLines | Where-Object { $_ -match $rule.Pattern }).Count
        $logFlags+=($rule.Name + ':' + $count)
      }
    }
    # The canonical installer removed its failed fresh-install service.
    # Re-stage ONLY the exact source-built runtime and public test trust in
    # the protected ephemeral path and observe bootstrap. This is a diagnostic,
    # NOT installed SYSTEM service, signed commercial authorization or grant.
    $runtimeProbe='NOT_RUN'
    $runtimeProbeError='NONE'
    if (!$registration -and !$canonicalServiceExe -and !$canonicalRuntimeExe) {
      $probeProcess=$null
      $oldAgentData=$env:BKE_AGENT_DATA_DIR
      $oldServiceHosted=$env:BKE_AGENT_SERVICE_HOSTED
      try {
        $sourceRuntime=Join-Path $agentSource 'dist/windows/bke-licensing-agent-runtime'
        $sourceHostExe=Join-Path $sourceRuntime 'bke-licensing-agent-runtime.exe'
        $pub=Join-Path $fixture 'account-product-lease-keys\issuers.json'
        $policy=Join-Path $fixture 'standalone-startup-policies\bke-render-dock.json'
        if (!(Test-Path -LiteralPath $sourceHostExe -PathType Leaf) -or
            !(Test-Path -LiteralPath $pub -PathType Leaf) -or
            !(Test-Path -LiteralPath $policy -PathType Leaf)) {
          $runtimeProbe='SOURCE_PAYLOAD_MISSING'
        }
        else {
          $runtimeProbeDir=Join-Path $agentRoot 'runtime'
          $issuerDir=Join-Path $agentRoot 'trust\account-product-lease-keys'
          $policyDir=Join-Path $agentRoot 'trust\standalone-startup-policies'
          foreach ($dir in @($agentRoot,$runtimeProbeDir,$issuerDir,$policyDir)) {
            if (Test-Path -LiteralPath $dir) {
              if ((Get-Item -LiteralPath $dir).Attributes -band
                  [IO.FileAttributes]::ReparsePoint) {
                throw 'Redirected installed test directory: refuse probe.'
              }
            }
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
          }
          Get-ChildItem -LiteralPath $sourceRuntime -Force |
            Copy-Item -Destination $runtimeProbeDir -Recurse -Force
          Copy-Item -LiteralPath $pub -Destination (Join-Path $issuerDir 'issuers.json')
          Copy-Item -LiteralPath $policy -Destination (Join-Path $policyDir 'bke-render-dock.json')
          # No production DS, real credential or authorization request.
          $env:BKE_AGENT_DATA_DIR=$agentData
          $env:BKE_AGENT_SERVICE_HOSTED='1'
          $stdoutPath=Join-Path $env:RUNNER_TEMP 'bke-host-runtime-probe-stdout.txt'
          $stderrPath=Join-Path $env:RUNNER_TEMP 'bke-host-runtime-probe-stderr.txt'
          $probeProcess=Start-Process -FilePath (Join-Path $runtimeProbeDir 'bke-licensing-agent-runtime.exe') -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
          if ($probeProcess.WaitForExit(6500)) {
            $runtimeProbe='EXITED_' + $probeProcess.ExitCode
          }
          else { $runtimeProbe='HELD_OPEN_FOR_6S' }
          if (Test-Path -LiteralPath $stderrPath) {
            $lines=@(Get-Content -LiteralPath $stderrPath -ErrorAction SilentlyContinue |
              Where-Object { $_ -match '(?i)failed|invalid|unavailable|missing|refused' } |
              Select-Object -Last 3)
            $safe=@()
            foreach ($line in $lines) {
              $clean=$line -replace '(?i)https?://\S+','[URL]'
              $clean=$clean -replace '\b[0-9a-fA-F]{40,}\b','[HASH]'
              $clean=$clean -replace '[A-Za-z0-9+/]{64,}={0,2}','[OPAQUE]'
              $safe+=$clean.Substring(0,[Math]::Min(180,$clean.Length))
            }
            $runtimeProbeError=($safe -join ' | ')
          }
        }
      }
      catch { $runtimeProbe='PROBE_ERROR_' + $_.Exception.GetType().Name }
      finally {
        if ($probeProcess) {
          try {
            if (!$probeProcess.HasExited) {
              $probeProcess.Kill()
              $probeProcess.WaitForExit(5000) | Out-Null
            }
          } finally { $probeProcess.Dispose() }
        }
        $env:BKE_AGENT_DATA_DIR=$oldAgentData
        $env:BKE_AGENT_SERVICE_HOSTED=$oldServiceHosted
      }
    }
    $observed=[ordered]@{
      runtime_source_probe=$runtimeProbe
      runtime_source_probe_error=$runtimeProbeError
      scm_state=$state
      service_account=$identity
      canonical_service_image=$imageMatches
      service_pid_present=($pidValue -gt 0)
      scm_registry_key=$registration
      canonical_service_exe_exists=$canonicalServiceExe
      canonical_runtime_exe_exists=$canonicalRuntimeExe
      x86_service_exe_exists=$x86ServiceExe
      enumerated_bke_service_names=@($knownServices | Select-Object -First 5)
      inno_log_exists=$logExists
      inno_log_flags=@($logFlags)
    }
    $diagnostic=$observed | ConvertTo-Json -Compress -Depth 4
    throw ("Installed Agent SCM verification FAIL after installer returned 0; " +
      "sanitized installer/SCM diagnostic=" + $diagnostic + ". " +
      "Never accept missing, stopped, untrusted or redirected Agent.")
  }
  Write-Host 'PASS: installed canonical Agent SCM service running as LocalSystem.'
  $procs=@(Get-CimInstance Win32_Process -Filter "Name='bke-licensing-agent-runtime.exe'" |
    Where-Object { $_.ExecutablePath -and [string]::Equals(
      [IO.Path]::GetFullPath($_.ExecutablePath),[IO.Path]::GetFullPath($runtime),
      [StringComparison]::OrdinalIgnoreCase) })
  if ($procs.Count -ne 1) { throw 'Installed Agent runtime process missing or ambiguous.' }
  $owner=Invoke-CimMethod -InputObject $procs[0] -MethodName GetOwner
  if ($owner.User -cne 'SYSTEM' -or $owner.Domain -cne 'NT AUTHORITY') {
    throw 'Installed Agent runtime is not SYSTEM.'
  }
  $pipe='\\.\pipe\bke-licensing-agent-standalone-startup-v1'
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  while (!(Test-Path -LiteralPath $pipe) -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 500
  }
  if (!(Test-Path -LiteralPath $pipe)) { throw 'Installed nonproduction Agent startup pipe absent.' }
  foreach ($file in @('trust\account-product-lease-keys\issuers.json',
                      'trust\standalone-startup-policies\bke-render-dock.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $agentRoot $file))) {
      throw "Installed Agent trust missing: $file"
    }
  }
  $child=Start-Process -FilePath $realImage -PassThru
  try {
    $childProcessId=$child.Id
    if (!$child.WaitForExit(25000)) { throw 'Render Dock installed startup timed out.' }
    if ($child.ExitCode -ne 86) {
      throw "Expected signed-install verified Agent missing-credential DENY exit 86, got $($child.ExitCode). Other denials are NOT proof of an intact signed policy."
    }
    $audit=Join-Path $agentRoot 'startup-grant-audit\grants.db'
    if (Test-Path -LiteralPath $audit) {
      $pyCode='import sqlite3,sys; c=sqlite3.connect("file:"+sys.argv[1]+"?mode=ro",uri=True); n=c.execute("SELECT COUNT(*) FROM startup_grants WHERE pid=?",(int(sys.argv[2]),)).fetchone()[0]; c.close(); sys.exit(1 if n else 0)'
      & py -3 -c $pyCode $audit ([string]$childProcessId)
      if ($LASTEXITCODE -ne 0) { throw 'Denied Render Dock received a durable Agent grant.' }
    }
    $report=[ordered]@{schema='bke.renderdock-hosted-installed-denial.v1';status='PASS';
      classification='DISPOSABLE_PREPRODUCTION_DENIAL_ONLY';
      renderdock_source_sha=$ExpectedRenderDockSourceSha;agent_source_sha=$ExpectedAgentSourceSha;
      agent_installer_sha256=$installerHash;renderdock_exe_sha256=$imageSha;
      system_agent=$true;ephemeral_signed_test_issuer=$true;real_commercial_credential=$false;
      executable_exit_code=86;durable_grant=$false;genuine_ds_issuer='NOT_PROVEN';
      physical_tpm='NOT_PROVEN';production='LOCKED'}
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $env:RUNNER_TEMP 'bke-renderdock-installed-denial.json') -Encoding utf8
    Write-Host 'HOSTED_INSTALLED_AGENT_RENDER_DOCK_DENIAL_PASS: real SYSTEM Agent + Render Dock, test-signed policy validated, independent credential required, no durable grant.'
  } finally {
    if (!$child.HasExited) { $child.Kill(); $child.WaitForExit() }
    $child.Dispose()
  }
} finally {
  $env:BKE_ENVIRONMENT=$oldEnvironment
  $env:BKE_PLATFORM_BASE_URL=$oldOrigin
  if ($installed) {
    try {
      if (Get-Service -Name $service -ErrorAction SilentlyContinue) {
        Stop-Service -Name $service -Force -ErrorAction SilentlyContinue
        & sc.exe delete $service | Out-Null
      }
    } catch { Write-Warning 'Failed to fully clean disposable SCM service.' }
  }
  if ($createdProduct -and (Test-Path -LiteralPath $productRoot)) {
    Remove-Item -LiteralPath $productRoot -Recurse -Force -ErrorAction SilentlyContinue
  }
  if ($installed -and (Test-Path -LiteralPath $agentRoot)) {
    Remove-Item -LiteralPath $agentRoot -Recurse -Force -ErrorAction SilentlyContinue
  }
  if ($createdData -and (Test-Path -LiteralPath $agentData)) {
    Remove-Item -LiteralPath $agentData -Recurse -Force -ErrorAction SilentlyContinue
  }
  foreach ($dir in @($agentSource,$fixture)) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
  }
}
