#!/bin/bash
set -e

backup_pid=""
app_pid=""

cleanup() {
    echo "Shutting down..."
    if [ -n "$backup_pid" ]; then
        kill "$backup_pid" 2>/dev/null || true
    fi
    if [ -n "$app_pid" ]; then
        kill "$app_pid" 2>/dev/null || true
        wait "$app_pid" 2>/dev/null || true
    fi
    exit 0
}
trap cleanup SIGTERM SIGINT

run_backup_loop() {
    while true; do
        now=$(date +%s)
        next=$(date -d "today 02:00" +%s)
        if [ "$next" -le "$now" ]; then
            next=$(date -d "tomorrow 02:00" +%s)
        fi
        sleep_seconds=$((next - now))
        sleep "$sleep_seconds" || true
        /app/backup-db.sh >> /app/backups/backup.log 2>&1 || true
    done
}

if [ "${BACKUP_ENABLED:-true}" != "false" ]; then
    echo "Starting backup scheduler..."
    run_backup_loop &
    backup_pid=$!
fi

# Run the .NET application
dotnet BookDistributionAPI.dll &
app_pid=$!
wait "$app_pid"
