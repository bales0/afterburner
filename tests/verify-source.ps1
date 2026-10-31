$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$gui = Join-Path $root 'AfterburnerStudio'
$checks = 0
function Assert-Source([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
Get-ChildItem $gui -Recurse -File | Where-Object { $_.Extension -in '.axaml','.svg','.csproj' } | ForEach-Object {
    [xml]$document = Get-Content $_.FullName -Raw
    Assert-Source ($null -ne $document.DocumentElement) "Invalid XML: $($_.Name)"
}
$view = Get-Content (Join-Path $gui 'MainWindow.axaml') -Raw
$code = Get-Content (Join-Path $gui 'MainWindow.axaml.cs') -Raw
foreach($match in [regex]::Matches($view,'(?:Click|SelectionChanged|TextChanged|IsCheckedChanged)="([A-Za-z][A-Za-z0-9_]*)"')) {
    $handler = $match.Groups[1].Value
    Assert-Source ([regex]::IsMatch($code, '\b'+$handler+'\s*\(')) "Missing handler: $handler"
}
[xml]$xml = $view
$names = @($xml.SelectNodes('//*[@Name]') | ForEach-Object { $_.Name })
Assert-Source (($names | Sort-Object -Unique).Count -eq $names.Count) 'Duplicate named controls'
foreach($name in @('PortList','PortBox','JedPath','EraseXsvfPath','WriteButton','WriteReadiness','CancelButton')) {
    Assert-Source ($names -contains $name) "Missing control: $name"
}
Assert-Source ($view.Contains('Button.mainaction:pointerover /template/ ContentPresenter#PART_ContentPresenter')) 'Missing template hover style'
Assert-Source ($view.Contains('Button.mainaction:pressed /template/ ContentPresenter#PART_ContentPresenter')) 'Missing pressed style'
Assert-Source ($view.Contains('Button:disabled /template/ ContentPresenter#PART_ContentPresenter')) 'Missing disabled style'
Assert-Source (-not $view.Contains('IsReadOnly="True" Watermark="Select a JEDEC')) 'Path fallback is read-only'
Assert-Source (-not ($view -match '\bCLI\b|multiplatform|Projekt:|normaliz')) 'Project goals remain in UI'
$allCode = (Get-ChildItem $gui -Recurse -File | Where-Object { $_.Extension -in '.cs','.axaml' } | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Assert-Source (-not ($allCode -match 'Process\.Start|CliRunner|CliPath')) 'External CLI usage found'
Assert-Source (-not ($allCode -match '[áčďéěíňóřšťúůýžÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ]|\bnebo\b')) 'Czech text remains in application source'
$model = Get-Content (Join-Path $gui 'Models/ChipProfile.cs') -Raw
Assert-Source ([regex]::Matches($model,'new\("([A-Z0-9]+)"').Count -eq 18) 'Device catalog changed unexpectedly'
$backend = Get-Content (Join-Path $gui 'Services/NativeAfterburner.cs') -Raw
Assert-Source ($backend.Contains('"Q-0,OK"')) 'Actual firmware XSVF success response missing'
Assert-Source ($backend.Contains('ReadPrompt(TimeSpan.FromSeconds(5)')) 'No wait for JTAG cleanup'
Assert-Source ($code.Contains('_interactionBusy = true') -and $code.Contains('_interactionBusy = false')) 'Missing operation lock'
$layout = Get-Content (Join-Path $gui 'Models/SocketLayout.cs') -Raw
Assert-Source ($layout.Contains('12 - column') -and $layout.Contains('13 + column')) 'Wrong ZIF orientation'
foreach($pins in @(20,24)) {
    $first = 12-$pins/2
    $numbers = @()
    foreach($column in $first..11) { $numbers += 12-$column; $numbers += $pins-11+$column }
    Assert-Source ((($numbers | Sort-Object) -join ',') -eq ((1..$pins) -join ',')) "DIP$pins numbering mismatch"
}
foreach($device in @('ATF1502AS','ATF1504AS')) {
    Assert-Source (Test-Path (Join-Path $gui "xsvf/erase_$device.xsvf")) "Missing bundled erase data: $device"
}
"PASS: $checks source checks. XML, handlers, language, styles, protocol guards, pin model and bundled files."
'This is a static check; it does not compile XAML or run the application.'

