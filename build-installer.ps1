# Builds dist\BatteryTray-Setup.exe on THIS PC. Only needed if you are not using the GitHub route.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Find-Dotnet {
  $d = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
  if ((Test-Path $d) -and ((& $d --list-sdks) -match '^8\.')) { return $d }
  return $null
}
function Find-Iscc {
  foreach ($p in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
    if (Test-Path $p) { return $p }
  }
  return $null
}

$dotnet = Find-Dotnet
if (-not $dotnet) {
  Write-Host 'Installing the .NET 8 SDK (build tool, one time)...'
  winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements
  $dotnet = Find-Dotnet
  if (-not $dotnet) { throw 'Could not find the .NET 8 SDK after installing it. Reopen this script.' }
}
$iscc = Find-Iscc
if (-not $iscc) {
  Write-Host 'Installing Inno Setup (installer builder, one time)...'
  winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
  $iscc = Find-Iscc
  if (-not $iscc) { throw 'Could not find Inno Setup after installing it. Reopen this script.' }
}

& $dotnet publish BatteryTray.csproj -c Release -o publish
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
& $iscc installer\BatteryTray.iss
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
Write-Host "`nDone: $PSScriptRoot\dist\BatteryTray-Setup.exe"
