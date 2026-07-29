# Book Distribution System

نظام لإدارة مخزون الكتب، أوامر الصرف، المرتجعات، المخالصات وسندات القبض.

## Stack

- Frontend: Angular 22, TypeScript 6, Tailwind CSS 4
- Backend: .NET 10, EF Core 10, SQLite, JWT
- Deployment: Docker Compose (single-client / single-server deployment)

## Production startup

1. Install and start Docker Desktop.
2. Copy `.env.example` to `.env`.
3. Generate a unique JWT key in PowerShell and place it in `JWT_SIGNING_KEY`:

   ```powershell
   $bytes = New-Object byte[] 48
   $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
   $rng.GetBytes($bytes)
   $rng.Dispose()
   [Convert]::ToBase64String($bytes)
   ```

4. Run `./BookDistributionAPI/scripts/generate-admin-password-hash.ps1`, choose the first admin password, and place its output in `ADMIN_PASSWORD_HASH`.
5. Start the application:

   ```powershell
   docker compose up -d --build
   ```

6. Open `http://localhost:8080` and sign in with the password chosen in step 4.

The application will refuse a first production startup without both secrets. Do not use or distribute a default password.

## Data and backup

- On the first Docker startup, the packaged client database and uploaded logos are copied into the persistent `book-data` volume once. Existing data is never overwritten.
- A consistent SQLite backup runs every day at 2:00 AM and is retained for 30 days in `book-backups`.
- To create a manual backup:

  ```powershell
  docker exec book_distribution_app /app/backup-db.sh
  ```

- `docker compose down -v` permanently removes the database and backups. Only use it after confirming a usable external backup.

## Development

```powershell
dotnet build BookDistributionAPI/BookDistributionAPI.csproj
Set-Location booking
npm ci
npm run build
```

See [DEPLOYMENT.md](DEPLOYMENT.md) for the delivery checklist.
