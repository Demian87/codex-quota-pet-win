param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path -LiteralPath $dotnet)) { throw ".NET 8 SDK was not found." }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot ".dotnet-cli"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$buildRoot = Join-Path $projectRoot ".build"
$buildOutput = Join-Path $buildRoot "bin\"
$buildIntermediate = Join-Path $buildRoot "obj\"
$publish = Join-Path $projectRoot "artifacts\portable"
$zip = Join-Path $projectRoot "artifacts\QuotaWisp-win-x64.zip"
& $dotnet restore (Join-Path $projectRoot "QuotaWisp.csproj") --runtime win-x64 --configfile (Join-Path $projectRoot "NuGet.Config") `
    -p:BaseOutputPath=$buildOutput -p:BaseIntermediateOutputPath=$buildIntermediate
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }
& $dotnet publish (Join-Path $projectRoot "QuotaWisp.csproj") --configuration $Configuration --runtime win-x64 --self-contained true --output $publish --no-restore `
    -p:BaseOutputPath=$buildOutput -p:BaseIntermediateOutputPath=$buildIntermediate
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -CompressionLevel Optimal
Get-FileHash -LiteralPath $zip -Algorithm SHA256
