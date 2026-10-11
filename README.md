# Lynx Financial Management

Self-hosted personal finance app: .NET 10 API + Angular frontend, SQLite, EU/UK open-banking sync
(Enable Banking) with CSV/OFX/CAMT import as a fallback. Planning features: budgets, savings goals,
recurring bills with a cash-flow forecast, and reports.

## Status
- [x] **UI mock-up** (`web/lynx-web`): all pages on fake data in `src/app/core/mock-data.ts`
- [ ] Backend scaffold, database, login
- [ ] Accounts, categories, transactions, file import
- [ ] Bank connector (Enable Banking)
- [ ] Budgets and goals
- [ ] Recurring detection and forecast
- [ ] Reports and dashboard
- [ ] Hosting prep

## Run the mock-up
Requires Node 22+ (24 LTS used here).

```
cd web/lynx-web
npm install
npm start        # http://localhost:4200
```
