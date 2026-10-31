# Run from the repository root or any directory:
# powershell -ExecutionPolicy Bypass -File .\tests\verify_studio_xaml_net10.ps1
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectDir = Join-Path $root 'AfterburnerStudio'
$project = Join-Path $projectDir 'AfterburnerStudio.csproj'

Write-Host 'Afterburner Studio - AXAML/.NET 10 verification' -ForegroundColor Cyan
Write-Host "Project: $project"
if (!(Test-Path $project)) { throw "Project not found: $project" }
if (!(Test-Path (Join-Path $projectDir 'MainWindow.axaml'))) { throw 'MainWindow.axaml missing.' }
if (!(Test-Path (Join-Path $projectDir 'App.axaml'))) { throw 'App.axaml missing.' }

# The XAML name generator can create .g.cs without the compiled XAML resource.
# Check the actual XAML input item and compilation setting instead.
Write-Host "`nMSBuild XAML inputs/properties:" -ForegroundColor Cyan
$inputs = & dotnet msbuild $project -getItem:AvaloniaXaml -getProperty:EnableAvaloniaXamlCompilation -getProperty:TargetFramework 2>&1
$inputs | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'MSBuild item query failed.' }
$inputsText = $inputs | Out-String
if ($inputsText -notmatch 'MainWindow\.axaml' -or $inputsText -notmatch 'App\.axaml') {
    throw 'App.axaml/MainWindow.axaml are not in AvaloniaXaml input items.'
}

Write-Host "`nRemoving obsolete Debug outputs..." -ForegroundColor Cyan
Get-Process -Name AfterburnerStudio -ErrorAction SilentlyContinue | Stop-Process -Force
foreach ($dir in @('bin', 'obj')) {
    $target = Join-Path $projectDir $dir
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
}

& dotnet restore $project --force
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

$log = Join-Path $root 'afterburner_axaml_build.log'
Write-Host "`nBuilding, saving log to: $log" -ForegroundColor Cyan
& dotnet build $project -c Debug --no-restore --no-incremental -v:normal 2>&1 | Tee-Object -FilePath $log
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed; see $log" }

$dll = Join-Path $projectDir 'bin/Debug/net10.0/AfterburnerStudio.dll'
if (!(Test-Path $dll)) { throw "Built DLL missing: $dll" }
$resource = Join-Path $projectDir 'obj/Debug/net10.0/Avalonia/resources'
if (Test-Path $resource) {
    Write-Host "Avalonia resource bundle exists: $resource" -ForegroundColor Green
} else {
    Write-Warning "Avalonia resource bundle not found at expected path: $resource. Check $log for CompileAvaloniaXaml."
}
Write-Host "`nBuild succeeded. The runtime loader has not been tested yet." -ForegroundColor Green
Write-Host 'Start with: dotnet run --project .\AfterburnerStudio\AfterburnerStudio.csproj -c Debug --no-build'
