#!/bin/bash
# Start cron daemon for automated backups (runs as root)
if command -v cron &> /dev/null; then
    echo "Starting cron daemon..."
    cron
fi

# Drop privileges and run the .NET application as the non-root user
exec su-exec $APP_UID dotnet BookDistributionAPI.dll
