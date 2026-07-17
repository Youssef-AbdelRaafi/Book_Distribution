# Stage 1: Build Angular Frontend
FROM node:22-alpine AS build-frontend
WORKDIR /app/frontend
COPY booking/package*.json ./
RUN npm ci
COPY booking/ ./
RUN npm run build

# Stage 2: Build .NET Backend
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-backend
WORKDIR /app/backend
COPY BookDistributionAPI/BookDistributionAPI.csproj ./BookDistributionAPI/
RUN dotnet restore ./BookDistributionAPI/BookDistributionAPI.csproj
COPY BookDistributionAPI/ ./BookDistributionAPI/
WORKDIR /app/backend/BookDistributionAPI
RUN dotnet publish -c Release --no-restore -o /app/publish

# Copy Angular build to .NET wwwroot
COPY --from=build-frontend /app/frontend/dist/booking/browser /app/publish/wwwroot

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl sqlite3 \
    && rm -rf /var/lib/apt/lists/*

# Create data and backup directories
RUN mkdir -p /app/data /app/backups

# Create non-root user for security
RUN groupadd --system appgroup && useradd --system --gid appgroup --home-dir /app --shell /usr/sbin/nologin appuser && \
    chown -R appuser:appgroup /app/data /app/backups

COPY --from=build-backend /app/publish .
COPY --chown=appuser:appgroup BookDistributionAPI/scripts/backup-db.sh /app/backup-db.sh
COPY --chown=appuser:appgroup BookDistributionAPI/scripts/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/backup-db.sh /app/entrypoint.sh

# Set environment variables for production
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080
ENV APP_DATA_DIR=/app/data

EXPOSE 8080

# Switch to non-root user
USER appuser

ENTRYPOINT ["/app/entrypoint.sh"]
