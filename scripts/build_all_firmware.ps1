param([switch]$IncludeExperimental)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
if (-not (Get-Command pio -ErrorAction SilentlyContinue)) {
    throw 'PlatformIO CLI (pio) was not found. Install PlatformIO in VS Code.'
}
$envs = @('uno','nano','mega2560','leonardo','micro','yun')
if ($IncludeExperimental) { $envs += @('uno_r4_minima','uno_r4_wifi','esp32s2_saola1') }
$output = Join-Path $root 'firmware_builds'
New-Item -ItemType Directory -Force $output | Out-Null
$failed = @()
foreach ($envName in $envs) {
    Write-Host "========== Building $envName ==========" -ForegroundColor Cyan
    & pio run -e $envName
    if ($LASTEXITCODE -ne 0) {
        $failed += $envName
        continue
    }
    $build = Join-Path $root ".pio/build/$envName"
    $target = Join-Path $output $envName
    New-Item -ItemType Directory -Force $target | Out-Null
    foreach ($extension in @('hex','bin','elf')) {
        $source = Join-Path $build "firmware.$extension"
        if (Test-Path $source) { Copy-Item $source $target -Force }
    }
}
if ($failed.Count -gt 0) {
    Write-Host "Failed environments: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "All requested build targets passed; output in $output" -ForegroundColor Green
