# Lynx Financial Management

Self-hosted personal finance app: .NET 10 API + Angular frontend, SQLite, EU/UK open-banking sync
(Enable Banking) with CSV/OFX/CAMT import as a fallback. Planning features: budgets, savings goals,
recurring bills with a cash-flow forecast, and reports.

## Status
- [x] **UI mock-up** (`web/lynx-web`): all pages on fake data in `src/app/core/mock-data.ts`
- [x] Backend scaffold (.NET 10, EF Core + SQLite), single-user login (cookie auth, first-run setup)
- [ ] Two-factor login (TOTP)
- [ ] Accounts, categories, transactions, file import
- [ ] Bank connector (Enable Banking)
- [ ] Budgets and goals
- [ ] Recurring detection and forecast
- [ ] Reports and dashboard
- [ ] Hosting prep

## Run (development)
Requires the .NET 10 SDK and Node 22+ (24 LTS used here). Run these in two terminals:

```
dotnet run --project src/Lynx.Api --launch-profile http   # API on http://localhost:5180
cd web/lynx-web && npm install && npm start               # UI on http://localhost:4200 (proxies /api to the API)
```
Open http://localhost:4200. On first run you create your account; the SQLite database is created at
`src/Lynx.Api/App_Data/lynx.db`. Everything except login/auth is still mock data.

Tests: `dotnet test Lynx.sln`
