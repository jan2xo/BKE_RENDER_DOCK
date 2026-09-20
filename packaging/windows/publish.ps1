[CmdletBinding()]
param(
    [ValidateSet('x64','arm64')]
    [string]$Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repositoryRoot 'BKE_RENDER_DOCK\RENDER DOCK.csproj'
$manifestSource = Join-Path $repositoryRoot 'BKE_RENDER_DOCK\bke.manifest.json'
$runtime = "win-$Architecture"
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\$runtime"

if (Test-Path $publishDirectory) {
    Remove-Item $publishDirectory -Recurse -Force
}

dotnet publish $project `
    --configuration Release `
    --runtime $runtime `
    --self-contained true `
    --output $publishDirectory `
    /p:Version=1.0.2

Copy-Item $manifestSource (Join-Path $publishDirectory 'bke.manifest.json') -Force

$entryPoint = Join-Path $publishDirectory 'RENDER DOCK.exe'
$publishedManifest = Join-Path $publishDirectory 'bke.manifest.json'
if (-not (Test-Path $entryPoint)) {
    throw 'Published Render Dock entry point is missing.'
}
if (-not (Test-Path $publishedManifest)) {
    throw 'Canonical manifest is missing from publish output.'
}

$manifest = Get-Content $publishedManifest -Raw | ConvertFrom-Json
$manifest.architecture = $Architecture
$manifest | ConvertTo-Json -Depth 8 | Set-Content $publishedManifest -Encoding UTF8

$verifiedManifest = Get-Content $publishedManifest -Raw | ConvertFrom-Json
if ($verifiedManifest.productId -ne 'bke-render-dock' -or
    $verifiedManifest.displayName -ne 'Render Dock' -or
    $verifiedManifest.version -ne '1.0.2' -or
    $verifiedManifest.entryPoint -ne 'RENDER DOCK.exe' -or
    $verifiedManifest.platform -ne 'windows' -or
    $verifiedManifest.architecture -ne $Architecture) {
    throw "Published manifest is not canonical for $Architecture."
}

Write-Host "Render Dock publish PASS: runtime=$runtime"
