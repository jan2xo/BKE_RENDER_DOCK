[CmdletBinding()]
param(
    [string]$SdkCommit = 'be79a1d3e055353183622ed6676498e685475495'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packageDirectory = Join-Path $repositoryRoot 'packages'
$workDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("bke-sdk-" + $SdkCommit.Substring(0, 12))

if (Test-Path -LiteralPath $workDirectory) {
    Remove-Item -LiteralPath $workDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $workDirectory -Force | Out-Null
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

    Remove-Item -LiteralPath (Join-Path $packageDirectory 'BKE.Desktop.Licensing.2.0.0.nupkg') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $packageDirectory 'BKE.Updater.0.4.0.nupkg') -Force -ErrorAction SilentlyContinue

    dotnet pack (Join-Path $workDirectory 'src/BKE.Desktop.Licensing/BKE.Desktop.Licensing.csproj') `
        --configuration Release `
        --output $packageDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build BKE.Desktop.Licensing 2.0.0 from canonical SDK source.' }

    dotnet pack (Join-Path $workDirectory 'src/BKE.Updater/BKE.Updater.csproj') `
        --configuration Release `
        --output $packageDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build BKE.Updater 0.4.0 from canonical SDK source.' }

    $licensing = Join-Path $packageDirectory 'BKE.Desktop.Licensing.2.0.0.nupkg'
    $updater = Join-Path $packageDirectory 'BKE.Updater.0.4.0.nupkg'
    if (-not (Test-Path -LiteralPath $licensing -PathType Leaf)) {
        throw 'BKE.Desktop.Licensing 2.0.0 package was not produced.'
    }
    if (-not (Test-Path -LiteralPath $updater -PathType Leaf)) {
        throw 'BKE.Updater 0.4.0 package was not produced.'
    }

    Write-Host "Prepared canonical BKE SDK packages from $SdkCommit"
    Write-Host " - $licensing"
    Write-Host " - $updater"
}
finally {
    if (Test-Path -LiteralPath $workDirectory) {
        Remove-Item -LiteralPath $workDirectory -Recurse -Force
    }
}
