$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $root 'BKE_RENDER_DOCK\RENDER DOCK.csproj'
$publish = Join-Path $PSScriptRoot 'publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $publish /p:Version=1.0.0
$manifest = Join-Path $root 'BKE_RENDER_DOCK\bke.manifest.json'
Copy-Item $manifest (Join-Path $publish 'bke.manifest.json') -Force
if (-not (Test-Path (Join-Path $publish 'RENDER DOCK.exe'))) { throw 'Published Render Dock entry point is missing.' }
if (-not (Test-Path (Join-Path $publish 'bke.manifest.json'))) { throw 'Canonical manifest is missing from publish output.' }
