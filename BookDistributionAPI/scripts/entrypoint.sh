#!/bin/bash
# Start cron daemon for automated backups
if command -v cron &> /dev/null; then
    echo "Starting cron daemon..."
    cron
fi

# Run the .NET application
exec dotnet BookDistributionAPI.dll
