#!/bin/bash
# Backup script for SQLite database
# Usage: ./backup-db.sh [data_dir] [backup_dir] [retention_days]
DATA_DIR="${1:-/app/data}"
BACKUP_DIR="${2:-/app/backups}"
DB_NAME="app.db"
RETENTION_DAYS="${3:-30}"

mkdir -p "$BACKUP_DIR"

if [ ! -f "$DATA_DIR/$DB_NAME" ]; then
    echo "Error: Database not found at $DATA_DIR/$DB_NAME"
    exit 1
fi

TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="$BACKUP_DIR/bookdistribution_${TIMESTAMP}.db"

# Use sqlite3 .backup for safe online backup
if command -v sqlite3 &> /dev/null; then
    sqlite3 "$DATA_DIR/$DB_NAME" ".backup '$BACKUP_FILE'"
    echo "Backup created: $BACKUP_FILE ($(du -h "$BACKUP_FILE" | cut -f1))"
else
    echo "sqlite3 not found. Falling back to file copy."
    cp "$DATA_DIR/$DB_NAME" "$BACKUP_FILE"
fi

# Clean up old backups
find "$BACKUP_DIR" -name "bookdistribution_*.db" -mtime +$RETENTION_DAYS -delete
echo "Cleaned up backups older than $RETENTION_DAYS days"
