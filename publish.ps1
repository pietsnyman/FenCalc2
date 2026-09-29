# Publishes self-contained builds (no .NET runtime needed on the target machine):
#   publish/win-x64/FenCalc2.exe     Windows x64
#   publish/linux-x64/FenCalc2       Linux x64
#
#   ./publish.ps1                 # both
#   ./publish.ps1 -WindowsOnly    # Windows only
#   ./publish.ps1 -LinuxOnly      # Linux only
param(
    [switch]$WindowsOnly,
    [switch]$LinuxOnly
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$runtimes = @()
if (-not $LinuxOnly)  { $runtimes += 'win-x64' }
if (-not $WindowsOnly) { $runtimes += 'linux-x64' }

foreach ($rid in $runtimes) {
    $out = "publish/$rid"
    Write-Host "Publishing $rid -> $out" -ForegroundColor Cyan
    dotnet publish FenCalc2.csproj -c Release -r $rid --self-contained true -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }
    $size = "{0:N1} MB" -f ((Get-ChildItem $out -Recurse | Measure-Object Length -Sum).Sum / 1MB)
    Write-Host "  done ($size)" -ForegroundColor Green
}

Write-Host "`nRun:" -ForegroundColor Yellow
Write-Host "  Windows: publish\win-x64\FenCalc2.exe"
Write-Host "  Linux:   publish/linux-x64/FenCalc2"
