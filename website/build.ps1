$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$assets = Join-Path $dist 'assets'

New-Item -ItemType Directory -Path $assets -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'index.html') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'styles.css') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'app.js') -Destination $dist -Force
Copy-Item -Path (Join-Path $root 'assets\*.png') -Destination $assets -Force

$archive = Join-Path $root 'QuotaWisp-landing.zip'
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $archive -CompressionLevel Optimal
Get-FileHash -LiteralPath $archive -Algorithm SHA256
