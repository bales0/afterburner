$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path "xsvf" | Out-Null
$root = "https://raw.githubusercontent.com/bales0/afterburner/master/xsvf"
foreach ($name in @("erase_ATF1502AS.xsvf", "erase_ATF1504AS.xsvf", "id_ATF150X.xsvf")) {
    Invoke-WebRequest -Uri "$root/$name" -OutFile "xsvf/$name"
    Write-Host "Saved xsvf/$name"
}
