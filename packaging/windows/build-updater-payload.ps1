[CmdletBinding()]
param(
    [string]$PublishDirectory,
    [string]$OutputDirectory,
    [string]$ProductId = 'bke-render-dock',
    [string]$Version = '1.0.1',
    [string]$EntryPoint = 'RENDER DOCK.exe'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) { throw 'Publish directory is missing.' }
if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory $EntryPoint) -PathType Leaf)) { throw 'Updater payload entry point is missing.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$payloadName = "Render-Dock-$Version-Windows-x64.update.zip"
$payloadPath = Join-Path $OutputDirectory $payloadName
$metadataPath = Join-Path $OutputDirectory "Render-Dock-$Version-Windows-x64.update.json"
Remove-Item -LiteralPath $payloadPath -Force -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.IO.Compression
$stream = [System.IO.File]::Open($payloadPath, [System.IO.FileMode]::CreateNew)
try {
    $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $files = Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File | Sort-Object { $_.FullName.Substring($PublishDirectory.Length).Replace('\','/') }
        foreach ($file in $files) {
            $relative = ($file.FullName.Substring($PublishDirectory.Length) -replace '^[\\/]+', '').Replace('\','/')
            if ([string]::IsNullOrWhiteSpace($relative) -or $relative.Contains('..')) { throw "Unsafe updater payload path: $relative" }
            $entry = $archive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $input = [System.IO.File]::OpenRead($file.FullName)
            try {
                $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose() }
            } finally { $input.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }

$payload = Get-Item -LiteralPath $payloadPath
$hash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
$metadata = [ordered]@{
    schema = 'bke.update-package.v1'
    productId = $ProductId
    version = $Version
    platform = 'windows'
    architecture = 'x64'
    entryPoint = $EntryPoint
    filename = $payload.Name
    contentType = 'application/vnd.bke.update-package+zip'
    bytes = $payload.Length
    sha256 = $hash
}
$metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding UTF8
$metadata | ConvertTo-Json
