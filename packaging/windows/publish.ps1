$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repositoryRoot 'BKE_RENDER_DOCK\RENDER DOCK.csproj'
$manifestSource = Join-Path $repositoryRoot 'BKE_RENDER_DOCK\bke.manifest.json'
$publishDirectory = Join-Path $repositoryRoot 'artifacts\publish\win-x64'

if (Test-Path $publishDirectory) {
    Remove-Item $publishDirectory -Recurse -Force
}

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    /p:Version=1.0.0

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
if ($manifest.productId -ne 'bke-render-dock' -or
    $manifest.displayName -ne 'Render Dock' -or
    $manifest.version -ne '1.0.0' -or
    $manifest.entryPoint -ne 'RENDER DOCK.exe' -or
    $manifest.platform -ne 'windows' -or
    $manifest.architecture -ne 'x64') {
    throw 'Published manifest is not canonical.'
}
