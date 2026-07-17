# Book Distribution System

نظام إدارة توزيع الكتب — لإدارة مبيعات الكتب الدراسية، المخازن، والمخالصات للمكتبات.

## Tech Stack

- **Frontend:** Angular 19, TypeScript 5.7, Tailwind CSS 4, Signals
- **Backend:** .NET 9, EF Core 9 + SQLite, JWT Auth
- **Infra:** Docker, multi-stage build

## Quick Start

```bash
# 1. Copy env file
cp .env.example .env
# Edit .env — set Auth__JwtSigningKey (min 32 chars)

# 2. Run with Docker
docker compose up --build

# Or run locally:
# Backend
cd BookDistributionAPI
dotnet run

# Frontend (separate terminal)
cd booking
npm install
npm start
```

- Frontend: `http://localhost:4200`
- Backend API: `http://localhost:5291`
- Default login: `admin` / `admin@123`

## Project Structure

```
BookDistribution_Project/
├── BookDistributionAPI/   # .NET 9 backend
│   ├── Features/          # Feature modules (Auth, Books, Invoices, etc.)
│   ├── Data/              # EF Core DbContext, migrations, seed
│   ├── Common/            # Shared DTOs, helpers, middleware
│   └── scripts/           # entrypoint, backup, crontab
├── booking/               # Angular 19 frontend
│   └── src/app/
│       ├── pages/         # Page components (dashboard, invoices, etc.)
│       ├── core/          # Services, models, interceptors, utils
│       └── layout/        # Header, app shell
├── Dockerfile
├── docker-compose.yml
└── .env.example
```

## Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `Auth__JwtSigningKey` | Yes | — | JWT signing key (min 32 chars) |
| `ConnectionStrings__DefaultConnection` | No | `Data Source=app.db` | SQLite connection string |

## Backup

Automatic daily backup at 2:00 AM via cron inside the container. Backups stored in `/app/backups/` volume. Retention: 30 days.
