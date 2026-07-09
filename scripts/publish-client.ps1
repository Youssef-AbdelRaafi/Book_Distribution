# نشر نسخة العميل الكاملة (API + الواجهة)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish\BookDistributionAPI"

Write-Host "1/3 نشر الباك اند..."
dotnet publish (Join-Path $root "BookDistributionAPI\BookDistributionAPI.csproj") -c Release -o $publishDir

Write-Host "2/3 بناء الواجهة..."
Push-Location (Join-Path $root "booking")
npm run build -- --configuration production
Pop-Location

Write-Host "3/3 نسخ الواجهة إلى wwwroot..."
$wwwroot = Join-Path $publishDir "wwwroot"
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
$distDir = Join-Path $root "booking\dist\booking\browser"
if (-not (Test-Path $distDir)) {
    $distDir = Join-Path $root "booking\dist\booking"
}
Copy-Item -Path (Join-Path $distDir "*") -Destination $wwwroot -Recurse -Force

Write-Host "تم النشر في: $publishDir"
Write-Host "شغّل: dotnet BookDistributionAPI.dll من مجلد النشر"
