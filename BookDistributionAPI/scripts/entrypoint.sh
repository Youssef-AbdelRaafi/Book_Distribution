#!/bin/bash
set -e

cleanup() {
    echo "Shutting down cron..."
    kill $(cat /var/run/crond.pid 2>/dev/null) 2>/dev/null || true
    exit 0
}
trap cleanup SIGTERM SIGINT

# Start cron daemon for automated backups
if command -v cron &> /dev/null; then
    echo "Starting cron daemon..."
    cron
fi

# Run the .NET application
exec dotnet BookDistributionAPI.dll
