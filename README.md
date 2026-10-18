# Lynx Financial Management

Self-hosted personal finance app: .NET 10 API + Angular frontend, SQLite, EU/UK open-banking sync
(Enable Banking) with CSV/OFX/CAMT import as a fallback. Planning features: budgets, savings goals,
recurring bills with a cash-flow forecast, and reports.

## Status
- [x] **UI mock-up** (`web/lynx-web`), since replaced by real data on every page
- [x] Backend scaffold (.NET 10, EF Core + SQLite), single-user login (cookie auth, first-run setup)
- [ ] Two-factor login (TOTP)
- [x] Accounts, categories, transactions, file import (CSV incl. ING Romania, OFX, CAMT.053, Banca Transilvania PDF; duplicate-safe re-import)
- [ ] Bank connector (Enable Banking)
- [x] Budgets and goals (monthly category budgets per currency; goals tracked manually or via an account balance)
- [x] Recurring detection and forecast (detected items are suggestions; confirmed ones drive a per-currency balance forecast)
- [x] Reports and dashboard (per currency, no conversion)
- [x] Hosting prep: Windows service + Tailscale Serve, auto-deploy from the `prod` branch (see [deploy/README.md](deploy/README.md))

## Run (development)
Requires the .NET 10 SDK and Node 22+ (24 LTS used here). Run these in two terminals:

```
dotnet run --project src/Lynx.Api --launch-profile http   # API on http://localhost:5180
cd web/lynx-web && npm install && npm start               # UI on http://localhost:4200 (proxies /api to the API)
```
Open http://localhost:4200. On first run you create your account; the SQLite database is created at
`src/Lynx.Api/App_Data/lynx.db`. Every page runs on real data.

Tests: `dotnet test Lynx.sln`
