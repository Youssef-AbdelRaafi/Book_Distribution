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

data_dir="${APP_DATA_DIR:-/app/data}"
database_file_name="${DATABASE_FILE_NAME:-new_database.db}"
database_path="$data_dir/$database_file_name"

# A named Docker volume is empty on its first run. Seed it once from the
# packaged client data without ever overwriting an existing client database.
if [ ! -f "$database_path" ] && [ -f "/app/seed/$database_file_name" ]; then
    echo "Initializing persistent data volume from the packaged client data..."
    mkdir -p "$data_dir"
    cp "/app/seed/$database_file_name" "$database_path"
    export DATABASE_INITIALIZED_FROM_PACKAGE=true

    if [ -d "/app/seed/uploads" ]; then
        mkdir -p "$data_dir/uploads"
        cp -a /app/seed/uploads/. "$data_dir/uploads/"
    fi
fi

run_backup_loop() {
    while true; do
        now=$(date +%s)
        next=$(date -d "today 02:00" +%s)
        if [ "$next" -le "$now" ]; then
            next=$(date -d "tomorrow 02:00" +%s)
        fi
        sleep_seconds=$((next - now))
        sleep "$sleep_seconds" || true
        if ! /app/backup-db.sh >> /app/backups/backup.log 2>&1; then
            echo "Database backup failed; see /app/backups/backup.log" >&2
        fi
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
