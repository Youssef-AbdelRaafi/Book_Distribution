param(
    [string]$DataDir = "/app/data",
    [string]$BackupDir = "/app/backups",
    [string]$DbName = "app.db",
    [int]$RetentionDays = 30
)

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$dbPath = Join-Path $DataDir $DbName
$backupFile = Join-Path $BackupDir "bookdistribution_$timestamp.db"

if (-not (Test-Path $BackupDir)) {
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
}

if (-not (Test-Path $dbPath)) {
    Write-Error "Database not found at $dbPath"
    exit 1
}

# Use sqlite3 .backup command for safe online backup
try {
    $backupFileAbs = (Resolve-Path $BackupDir).Path + "\bookdistribution_$timestamp.db"
    & "sqlite3" $dbPath ".backup '$backupFileAbs'"
    Write-Output "Backup created: $backupFileAbs ($((Get-Item $backupFileAbs).Length / 1MB -as [int]) MB)"
}
catch {
    Write-Error "sqlite3 not found. Falling back to file copy."
    Copy-Item -Path $dbPath -Destination $backupFile -Force
}

# Compress old backups
$oldBackups = Get-ChildItem -Path $BackupDir -Filter "*.db" | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$RetentionDays) }
foreach ($old in $oldBackups) {
    Remove-Item -Path $old.FullName -Force
    Write-Output "Removed old backup: $($old.Name)"
}
