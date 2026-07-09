# نسخ احتياطي أسبوعي لقاعدة بيانات العميل
# شغّله من Task Scheduler كل أسبوع، أو يدوياً:
#   powershell -ExecutionPolicy Bypass -File backup-database.ps1

param(
    [string]$Server = "localhost",
    [string]$Database = "BookDistributionDB",
    [string]$BackupDir = "$env:USERPROFILE\Documents\BookDistributionBackups"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupFile = Join-Path $BackupDir "$Database-$timestamp.bak"

Write-Host "جاري النسخ الاحتياطي إلى: $backupFile"

sqlcmd -Q "BACKUP DATABASE [$Database] TO DISK = N'$backupFile' WITH INIT, COMPRESSION, STATS = 10"

if ($LASTEXITCODE -ne 0) {
    Write-Error "فشل النسخ الاحتياطي. تأكد من تثبيت SQL Server وتشغيله."
    exit 1
}

# احتفظ بآخر 8 نسخ فقط
Get-ChildItem $BackupDir -Filter "$Database-*.bak" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -Skip 8 |
    Remove-Item -Force

Write-Host "تم النسخ الاحتياطي بنجاح."
