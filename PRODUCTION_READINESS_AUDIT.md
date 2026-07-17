# Production Readiness Audit

Date: 2026-07-17

Scope audited: ASP.NET Core 9 backend, Angular frontend, SQLite data layer, Docker/Docker Compose packaging, offline single-PC runtime deployment.

Important scope note: the application currently uses SQLite, not SQL Server. Database findings below are for the implemented SQLite/EF Core design.

## Critical Issues

| Status | Area | File:line | Issue | Why it matters | Resolution |
| --- | --- | --- | --- | --- | --- |
| Fixed | Angular/API contract | `BookDistributionAPI/Features/Auth/AuthController.cs:56`, `BookDistributionAPI/Features/Auth/AuthModels.cs:14`, `booking/src/app/core/services/auth.service.ts:57` | Login API returns `ApiResponse<LoginResponse>`, but the Angular client expected a bare login response. | Users could receive a successful API response but fail to store the token, making production login unreliable. | Updated the Angular auth service to read `res.data` and store `payload.token`/`payload.expiresAt` at `booking/src/app/core/services/auth.service.ts:64`. |
| Fixed | Docker image build | `Dockerfile:32` | Runtime image is Debian-based, but user creation used Alpine-only `addgroup -S`/`adduser -S`. | The production image could fail to build before deployment. | Replaced with Debian-compatible `groupadd`/`useradd` and kept the app running as non-root. |
| Fixed | Invoice data integrity | `BookDistributionAPI/Features/Invoices/InvoiceService.cs:534` | Batch invoice delete only soft-deleted invoices and did not execute the stock/payment reversal logic used by single invoice deletion. | Inventory and account balances could become wrong after batch deletes. | Batch delete now validates requested IDs, orders dependent invoice types, and calls `DeleteInvoiceAsync` for each invoice at `InvoiceService.cs:552`. |
| Fixed | Historical invoice visibility | `BookDistributionAPI/Data/AppDbContext.cs:273` | `InvoiceItem` was filtered through `Book.IsActive`; soft-deleting a book could hide old invoice line items. | Historical invoices and reports could silently lose details. | Removed the `InvoiceItem` query filter while keeping the `Book` filter. |
| Fixed | Number reuse after soft delete | `BookDistributionAPI/Features/Invoices/InvoiceService.cs:38`, `BookDistributionAPI/Features/ReceiptVouchers/ReceiptVoucherService.cs:27` | Next invoice/voucher numbers ignored soft-deleted rows. | Number reuse can hit unique constraints and create audit confusion. | Added `IgnoreQueryFilters()` when calculating the next invoice and voucher numbers. |

## High Priority

| Status | Area | File:line | Issue | Why it matters | Resolution |
| --- | --- | --- | --- | --- | --- |
| Fixed | Offline runtime | `booking/src/index.html:8`, `booking/src/styles.css:2`, `booking/package.json:22` | UI depended on Google Fonts and Material Symbols CDN. | Offline customer PCs would lose icons/fonts and CSP allowed external font sources. | Removed external font links/CSP hosts, imported local `material-symbols`, and switched to system UI fonts. |
| Fixed | Frontend dependency security | `booking/package.json:16` | The Angular 19 production dependency set had known vulnerabilities in `@angular/*`. | Shipping known vulnerable runtime dependencies is not production-ready. | Updated Angular packages to `^22.0.7` and TypeScript to `~6.0.2`; production `npm audit` now reports zero vulnerabilities. |
| Fixed | Logo persistence | `BookDistributionAPI/Features/Libraries/LibrariesController.cs:173`, `BookDistributionAPI/Program.cs:144` | Uploaded logos were written under `wwwroot`, which is not persisted by the Docker volume. | Customer-uploaded logos could disappear after container rebuild/recreate. | Logos now store under the configured app data uploads directory and are served from `/uploads`. |
| Fixed | Logo upload validation | `BookDistributionAPI/Features/Libraries/LibrariesController.cs:178`, `LibrariesController.cs:191` | Oversized file validation depended only on request size limits, short files could throw, and WEBP validation only checked `RIFF`. | Malformed uploads could produce errors or bypass weak content validation. | Added explicit 5 MB check, safe header reads, stricter magic-byte validation, and old-logo cleanup. |
| Fixed | Deleted library listing | `BookDistributionAPI/Features/Libraries/LibrariesController.cs:30` | `includeDeleted=true` still used the global library query filter. | Recycle/restore views could not reliably show deleted libraries. | Switched deleted-inclusive query to `IgnoreQueryFilters()` at `LibrariesController.cs:33`. |
| Fixed | Soft-deleted book uniqueness | `BookDistributionAPI/Features/Books/BooksController.cs:81`, `BooksController.cs:151`, `BooksController.cs:217` | Duplicate checks ignored inactive books while the database unique index still sees them. | Creating or bulk-importing a book matching a deleted row could fail at save time or produce confusing conflicts. | Create restores inactive duplicates; bulk and update checks now include query-filtered rows. |
| Fixed | Seed duplication | `BookDistributionAPI/Data/SeedData.cs:104` | Seeded books checked only active books. | A soft-deleted seeded book could be reinserted and conflict with database uniqueness. | Seed checks now use `IgnoreQueryFilters()`. |
| Fixed | Health check | `docker-compose.yml:20`, `BookDistributionAPI/Program.cs:164` | Docker healthcheck used an authenticated settings endpoint. | Healthy containers could be marked unhealthy. | Added anonymous `/api/health` and pointed Compose healthcheck to it. |
| Partially fixed | Admin bootstrap | `BookDistributionAPI/Data/SeedData.cs:163`, `.env.example:15` | Default admin password fallback was hardcoded as `admin@123`. | Default credentials are unsafe if left unchanged. | Seed now honors `ADMIN_PASSWORD_HASH` when provided. Remaining risk: if omitted, fallback still creates `admin/admin@123` for compatibility. |

## Medium Priority

| Status | Area | File:line | Issue | Why it matters | Resolution |
| --- | --- | --- | --- | --- | --- |
| Remaining | Docker verification | `Dockerfile:1` | Full `docker build` could not be executed because Docker Desktop/Linux engine was not running on this machine. | Dockerfile syntax and local builds are partly validated, but the final image was not built end to end here. | `docker compose config --quiet` passed. Run `docker build -t book-distribution:local .` on a machine with Docker running before release. |
| Remaining | Angular bundle size | `booking/angular.json:44` | Production Angular build exceeds the 700 kB initial warning budget by about 9.95 kB. | Not a release blocker, but slower first load on old/offline PCs. | Build succeeds. Consider subsetting Material Symbols or raising the budget after performance sign-off. |
| Remaining | Local developer install | `booking/package-lock.json:1` | Existing local `booking/node_modules` appears locked/corrupt from a failed install on this workstation. | Local frontend commands may fail until dependencies are reinstalled cleanly. | Verified build from a clean temp copy with `npm ci`. Do a clean reinstall when convenient. |
| Remaining | Clearance invoice presentation | `BookDistributionAPI/Features/Invoices/InvoiceService.cs:399` | Clearance invoices can use net total after paid vouchers while item lines still represent gross order/refund detail. | Printed totals can be confusing if prior receipts exist. | Not changed because it needs product/accounting design: either show prior receipts as adjustment lines or keep total gross plus separate paid/net fields. |
| Remaining | Batch delete transaction scope | `BookDistributionAPI/Features/Invoices/InvoiceService.cs:534` | Batch delete now preserves per-invoice business rules, but each invoice deletion owns its own transaction. | A very rare mid-batch failure could leave earlier invoice deletes committed and later ones untouched. | Acceptable improvement for now; a future shared transaction should be designed carefully around invoice locks. |
| Remaining | Automated tests | repository | No test project was found. | Production readiness depends on manual verification only. | Add backend service tests for invoices/books/uploads/auth and frontend auth/service tests before larger releases. |
| Remaining | Offline build from source | `Dockerfile:4`, `Dockerfile:12` | Docker builds still require NuGet/npm packages unless the builder has cache or internet. | Runtime can be offline after an image is built, but source builds are not guaranteed offline. | Ship a prebuilt image tar or provide an offline package cache/mirror for customer rebuilds. |

## Low Priority

| Status | Area | File:line | Issue | Why it matters | Resolution |
| --- | --- | --- | --- | --- | --- |
| Fixed | Security headers | `BookDistributionAPI/Program.cs:117` | Basic browser hardening headers were missing. | Headers reduce common browser-side risks. | Added `X-Content-Type-Options`, `Referrer-Policy`, and `X-Frame-Options`. |
| Fixed | Production CORS | `BookDistributionAPI/Program.cs:160` | Development CORS policy was applied unconditionally. | Production single-origin deployment does not need broad dev CORS. | CORS is now enabled only in Development. |
| Fixed | Uploaded static files | `BookDistributionAPI/Program.cs:150` | Persisted uploads were not served from the persisted data directory. | Saved logos needed a stable runtime URL. | Added a `/uploads` static file provider backed by the configured upload directory. |
| Remaining | CSP strictness | `booking/src/index.html:8` | CSP still allows inline styles/scripts. | Current Angular output and inline startup styles need it, but stricter CSP would be better. | External hosts were removed. Consider nonce/hash-based CSP in a later hardening pass. |

## Code Improvements

| Status | File:line | Improvement | Why it matters |
| --- | --- | --- | --- |
| Fixed | `BookDistributionAPI/Features/Auth/AuthOptions.cs:43` | Password verification now catches malformed hash data instead of throwing. | Bad/stale data should not produce a 500 during login or password change. |
| Fixed | `BookDistributionAPI/Features/Auth/AuthOptions.cs:78` | Added `IsSupportedHashFormat` for seed-time admin hash validation. | Prevents invalid `ADMIN_PASSWORD_HASH` from breaking seeded login. |
| Fixed | `BookDistributionAPI/Features/Libraries/LibrariesController.cs:398` | Old logo files are deleted when a replacement is uploaded. | Avoids unbounded orphaned file growth. |
| Fixed | `BookDistributionAPI/scripts/entrypoint.sh:20` | Replaced cron dependency with an app-owned backup loop. | Keeps backups working under a non-root runtime container without a system daemon. |

## Performance Improvements

| Status | File:line | Issue | Recommendation |
| --- | --- | --- | --- |
| Remaining | `dist/booking/browser/media/material-symbols-outlined-*.woff2` | Local Material Symbols font is about 3.96 MB in the production build. | For older customer machines, subset the icon font or replace with a smaller local icon set. |
| Remaining | `booking/angular.json:44` | Initial bundle warning budget is slightly exceeded. | Measure on the target PC; either optimize lazy loading/assets or adjust the budget deliberately. |
| Remaining | `BookDistributionAPI/Features/Invoices/InvoiceService.cs:534` | Batch deletes now favor correctness over speed by reusing single-invoice deletion logic. | If operators delete large batches often, add a service-level batch transaction and targeted integration tests. |

## Security Improvements

| Status | File:line | Improvement/Risk | Notes |
| --- | --- | --- | --- |
| Fixed | `docker-compose.yml:12` | JWT signing key is required through Compose environment. | Compose fails fast when `JWT_SIGNING_KEY` is absent. |
| Fixed | `BookDistributionAPI/Program.cs:117` | Added basic hardening headers. | Helps reduce MIME sniffing, referrer leakage, and clickjacking exposure. |
| Fixed | `BookDistributionAPI/Features/Libraries/LibrariesController.cs:191` | Uploads now verify image signatures more defensively. | Extension checks alone are not trusted. |
| Remaining | `.env.example:7` | Example JWT key is intentionally non-production. | Customer install docs should require generating a unique key. |
| Remaining | `BookDistributionAPI/Data/SeedData.cs:166` | Fallback admin password still exists. | Set `ADMIN_PASSWORD_HASH` during deployment or force password change on first login in a future release. |
| Verified | `BookDistributionAPI/BookDistributionAPI.csproj` | NuGet vulnerability audit found no vulnerable packages. | Verified with `dotnet list ... package --vulnerable --include-transitive`. |
| Verified | `booking/package.json:16` | Production npm vulnerability audit found zero vulnerabilities. | Verified with `npm audit --omit=dev --audit-level=moderate`. |

## Database Improvements

| Status | File:line | Issue | Resolution |
| --- | --- | --- | --- |
| Fixed | `BookDistributionAPI/Data/AppDbContext.cs:273` | Soft-deleted books should not hide historical invoice items. | Removed `InvoiceItem` query filter tied to `Book.IsActive`. |
| Fixed | `BookDistributionAPI/Features/Invoices/InvoiceService.cs:38` | Invoice number generation ignored soft-deleted invoices. | Uses `IgnoreQueryFilters()` for the max-number query. |
| Fixed | `BookDistributionAPI/Features/ReceiptVouchers/ReceiptVoucherService.cs:27` | Voucher number generation ignored soft-deleted vouchers. | Uses `IgnoreQueryFilters()` for the max-number query. |
| Fixed | `BookDistributionAPI/Features/Books/BooksController.cs:81` | Soft-deleted unique book rows blocked clean recreate. | Inactive duplicate rows are restored rather than reinserted. |
| Remaining | Database platform | repository | User context mentioned SQL Server, but app uses SQLite. | SQLite is reasonable for a single-PC offline app. If multi-user LAN concurrency is expected, revisit the database choice. |

## Docker Improvements

| Status | File:line | Issue | Resolution |
| --- | --- | --- | --- |
| Fixed | `Dockerfile:32` | Invalid Debian user/group commands. | Replaced with `groupadd`/`useradd`. |
| Fixed | `Dockerfile:43` | Data directory environment was implicit. | Set `APP_DATA_DIR=/app/data` in the runtime image. |
| Fixed | `BookDistributionAPI/scripts/entrypoint.sh:33` | Cron was unsuitable for a non-root app container. | Added a built-in daily backup loop controlled by `BACKUP_ENABLED`. |
| Fixed | `docker-compose.yml:16` | App data and backups are persisted to named volumes. | Confirmed `book-data` and `book-backups` volumes remain configured. |
| Fixed | `docker-compose.yml:20` | Healthcheck used an authenticated API route. | Uses anonymous `/api/health`. |
| Remaining | Full image build | `Dockerfile:1` | Docker daemon was unavailable during audit. | Build the image on a Docker-enabled host before release. |

## Angular Improvements

| Status | File:line | Issue | Resolution |
| --- | --- | --- | --- |
| Fixed | `booking/src/app/core/services/auth.service.ts:57` | Login response type did not match backend wrapper. | Uses `ApiResponse<LoginPayload>`. |
| Fixed | `booking/src/app/core/services/auth.service.ts:64` | Token handling read wrapper fields instead of payload fields. | Reads and validates `res.data`. |
| Fixed | `booking/src/index.html:8` | CSP allowed external font hosts. | Restricted font/style loading to local sources. |
| Fixed | `booking/src/styles.css:2` | Icons now load from an npm package. | Supports offline runtime. |
| Fixed | `booking/package.json:16` | Angular packages were upgraded to a clean production vulnerability state. | Build succeeds with Angular 22. |
| Remaining | `booking/angular.json:44` | Initial bundle budget warning. | Optimize/subset assets or adjust the budget after target-PC testing. |

## Backend Improvements

| Status | File:line | Issue | Resolution |
| --- | --- | --- | --- |
| Fixed | `BookDistributionAPI/Program.cs:164` | No anonymous health endpoint. | Added `/api/health`. |
| Fixed | `BookDistributionAPI/Program.cs:144` | Uploads had no persisted static file root. | Added configurable upload root and static serving. |
| Fixed | `BookDistributionAPI/Features/Libraries/LibrariesController.cs:173` | Upload binding was implicit. | Marked file input with `[FromForm]`. |
| Fixed | `BookDistributionAPI/Features/Auth/AuthOptions.cs:43` | Malformed password hashes could throw. | Verification now fails closed. |
| Fixed | `BookDistributionAPI/Data/SeedData.cs:163` | Admin hash setting existed in docs but was not applied by seed. | Seed now consumes `ADMIN_PASSWORD_HASH`. |

## Offline Deployment Improvements

| Status | Area | Finding | Resolution/Recommendation |
| --- | --- | --- | --- |
| Fixed | Runtime UI assets | Built Angular output no longer references Google Fonts/CDN hosts. | Verified by scanning the production build for `fonts.googleapis`, `fonts.gstatic`, `cdn`, `unpkg`, and `jsdelivr`. |
| Fixed | Runtime uploads | Logos are stored under persisted app data and served from `/uploads`. | Survives container recreation when the data volume is preserved. |
| Fixed | Runtime backups | Backups run inside the app container without system cron. | Uses `/app/backups/backup.log` and existing backup script. |
| Remaining | Source builds | NuGet/npm restore still need package cache or internet. | For truly offline customer installs, ship a tested image tar plus backup/restore instructions, or provide a local package mirror/cache. |
| Remaining | Docker verification | Docker Desktop was not running during this audit. | Compose config passed, but full build/run smoke test must be done on a Docker-enabled machine. |

## Verification Results

| Check | Result |
| --- | --- |
| `dotnet build BookDistributionAPI/BookDistributionAPI.csproj --no-restore` | Passed, 0 warnings, 0 errors. |
| `dotnet list BookDistributionAPI/BookDistributionAPI.csproj package --vulnerable --include-transitive` | Passed, no vulnerable packages. |
| Clean temp `npm ci --no-audit --no-fund` | Passed. |
| Clean temp `npm run build` | Passed with initial bundle budget warning only. |
| `npm audit --omit=dev --audit-level=moderate` | Passed, found 0 vulnerabilities. |
| Production build scan for Google/CDN font hosts | Passed, no matches. |
| `docker compose config --quiet` | Passed. |
| `docker build -t book-distribution-audit:local .` | Not completed because Docker Desktop/Linux engine was not running. |

## Remaining Risks

1. Full Docker image build/run must be verified on a Docker-enabled machine.
2. Set `ADMIN_PASSWORD_HASH` and a unique `JWT_SIGNING_KEY` for every customer deployment.
3. Decide how clearance invoices should present gross items, prior receipts, and net totals before relying on printed financial totals.
4. Add automated backend and frontend tests around the fixed business rules.
5. For strict offline support, distribute a prebuilt image tar or package caches; do not rely on live npm/NuGet restores at the customer site.
6. Consider icon-font subsetting to reduce the first-load cost on older PCs.
