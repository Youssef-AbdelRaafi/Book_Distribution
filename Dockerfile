# Stage 1: Build Angular Frontend
FROM node:22-alpine AS build-frontend
WORKDIR /app/frontend
COPY booking/package*.json ./
RUN npm ci
COPY booking/ ./
RUN npm run build

# Stage 2: Build .NET Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-backend
WORKDIR /app/backend
COPY BookDistributionAPI/BookDistributionAPI.csproj ./BookDistributionAPI/
RUN dotnet restore ./BookDistributionAPI/BookDistributionAPI.csproj
COPY BookDistributionAPI/ ./BookDistributionAPI/
WORKDIR /app/backend/BookDistributionAPI
RUN dotnet publish -c Release -o /app/publish

# Copy Angular build to .NET wwwroot
COPY --from=build-frontend /app/frontend/dist/booking/browser /app/publish/wwwroot

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl sqlite3 cron \
    && rm -rf /var/lib/apt/lists/*

# Create data directory with proper permissions
RUN mkdir -p /app/data /app/backups \
    && chown -R $APP_UID:$APP_UID /app/data /app/backups

COPY --from=build-backend /app/publish .
COPY BookDistributionAPI/scripts/backup-db.sh /app/backup-db.sh
COPY BookDistributionAPI/scripts/crontab /etc/cron.d/backup-cron
COPY BookDistributionAPI/scripts/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/backup-db.sh /app/entrypoint.sh \
    && chmod 0644 /etc/cron.d/backup-cron \
    && crontab /etc/cron.d/backup-cron

# Set environment variables for production
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080

USER $APP_UID
EXPOSE 8080

HEALTHCHECK --start-period=15s --interval=30s --timeout=10s --retries=3 \
    CMD curl -f http://localhost:8080/ || exit 1

ENTRYPOINT ["/app/entrypoint.sh"]
