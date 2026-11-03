<#
.SYNOPSIS
Publishes the self-contained app and packs it into artifacts\installer\IsConnected-Setup.exe.

.PARAMETER ExpectedVersion
Fails the build unless <Version> in IsConnected.csproj equals this. The release workflow passes the tag here.
#>
[CmdletBinding()]
param(
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'IsConnected.csproj'
$publishDir = Join-Path $root 'artifacts\publish'
$installerDir = Join-Path $root 'artifacts\installer'

$version = ([xml](Get-Content -LiteralPath $project -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if (-not $version) {
    throw "No <Version> found in $project."
}
if ($ExpectedVersion -and $ExpectedVersion -ne $version) {
    throw "Tag version '$ExpectedVersion' does not match <Version>$version</Version> in IsConnected.csproj."
}

$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $iscc) {
    throw 'Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup'
}

# Only build output lives here. Clearing it keeps files of an earlier publish out of the installer.
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

& dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

& $iscc /Q "/DAppVersion=$version" "/DPublishDir=$publishDir" "/DOutputDir=$installerDir" (Join-Path $PSScriptRoot 'IsConnected.iss')
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installer = Join-Path $installerDir 'IsConnected-Setup.exe'
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$installer.sha256" -Value "$hash  IsConnected-Setup.exe" -Encoding ascii -NoNewline

$sizeMb = (Get-Item -LiteralPath $installer).Length / 1MB
Write-Host ('Built {0} ({1:N1} MB), version {2}, SHA-256 {3}' -f $installer, $sizeMb, $version, $hash)
