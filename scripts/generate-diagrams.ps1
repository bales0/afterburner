# Regenerate technical SVGs. Same column mapping as Models/SocketLayout.cs.
$ErrorActionPreference = 'Stop'
$dest = Join-Path $PSScriptRoot '../AfterburnerStudio/Assets/Diagrams'
foreach ($pins in @(20,24)) {
    $svg = [System.Text.StringBuilder]::new()
    [void]$svg.Append('<svg xmlns="http://www.w3.org/2000/svg" width="840" height="420" viewBox="0 0 840 420"><rect width="840" height="420" fill="#F4F8FC"/>')
    [void]$svg.Append('<g font-family="Arial,sans-serif" fill="#17304A"><text x="20" y="30" font-size="21">Afterburner 3.x — top view, USB on the left</text><rect x="87" y="76" width="660" height="267" fill="#158B96"/><rect x="110" y="100" width="610" height="219" fill="#D2EEEE"/><path d="M734 122 L801 80" stroke="#17304A" stroke-width="8"/><rect x="784" y="61" width="36" height="33" fill="#158B96"/><text x="738" y="53">LEVER</text>')
    $first = 12 - $pins/2
    $left = 135 + $first*50 - 18
    $width = 703 - $left
    [void]$svg.Append("<rect x='$left' y='163' width='$width' height='95' fill='#19252C'/>")
    for ($column = 0; $column -lt 12; $column++) {
        $x = 135+$column*50
        $top = 12-$column
        $bottom = 13+$column
        [void]$svg.Append("<rect x='$($x-8)' y='123' width='16' height='30' fill='#DAA739'/><rect x='$($x-8)' y='268' width='16' height='30' fill='#DAA739'/><text x='$($x-8)' y='115'>$top</text><text x='$($x-8)' y='315'>$bottom</text>")
        if ($column -ge $first) {
            $icBottom = $pins-11+$column
            [void]$svg.Append("<text x='$($x-7)' y='180' fill='white'>$top</text><text x='$($x-7)' y='250' fill='white'>$icBottom</text>")
        }
    }
    [void]$svg.Append("<text x='$($left+50)' y='225' font-size='24' fill='white'>DIP$pins</text><ellipse cx='703' cy='210' rx='10' ry='13' fill='#D2EEEE'/><circle cx='681' cy='184' r='5' fill='#E34B52'/><text x='723' y='182' fill='#B42318'>PIN 1</text><text x='20' y='375'>DIP20: ZIF 11, 12, 13, 14 unused at the left. DIP24: all contacts.</text></g></svg>")
    [System.IO.File]::WriteAllText((Join-Path $dest "DIP${pins}_direct.svg"), $svg.ToString())
}

