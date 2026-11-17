# PharmaOS — pharmacy business platform

Multi-tenant SaaS for Indian retail pharmacies and small chains: billing, batch-wise inventory, purchasing, supplier
returns & credit recovery, customer credit & CRM, cash control, double-entry books, multi-store, provider console,
offline billing and (gated) online ordering.

Stack: **.NET 9 modular monolith (EF Core, SQL Server)** + **React 19 (Vite, TypeScript)**.
Delivery plan and status: [docs/ROADMAP.md](docs/ROADMAP.md) · offline design: [docs/OFFLINE.md](docs/OFFLINE.md) · operations & security: [docs/OPERATIONS.md](docs/OPERATIONS.md) ·
non-retail tracks: [docs/SEPARATE_TRACKS.md](docs/SEPARATE_TRACKS.md)

## Run locally

Prerequisites: .NET 9 SDK, Node 20+, SQL Server (local default instance, Windows auth).
On this machine all caches/files are kept on **D:** (NuGet → `D:\DevCache\nuget`, uploads → `D:\PharmaOS\files`).

```powershell
# API → http://localhost:5101 (Development: migrates PharmaOS_Dev automatically)
cd backend
dotnet run --project src/PharmaOS.Api --launch-profile http

# Web → http://localhost:5173 (proxies /api to the API)
cd frontend
npm install
npm run dev
```

| Who | Where | How to get in |
|---|---|---|
| Pharmacy staff | http://localhost:5173 | "Create an account" → you are the Owner (30-day trial of the Small chain plan) |
| SaaS provider | http://localhost:5173/platform | Dev-only bootstrap admin from `appsettings.Development.json` (`Platform:Bootstrap*`) |
| Pharmacy customers | `/shop/<branchId>` and `/track/<token>` | No login; only when the `online` module is enabled for that pharmacy |

Tests: `cd backend; dotnet test` — 62 API/integration tests on real SQL Server (throw-away DB).
Browser tests: start the API with `$env:RateLimit__AuthPerMinute='1000'` (the tests register many test pharmacies; the
normal limit of 10 sign-ins/registrations per minute would block them), start the web app, then `cd frontend; npm run e2e`
(Playwright/Chromium, browsers in `D:\DevCache\ms-playwright`).

Production configuration (never commit): `ConnectionStrings__Pharma`, `Jwt__Key` (≥ 32 chars), `Platform__BootstrapEmail/Password`
(first run only), `Storage__Root` and `Security__KeysPath` (data drive, back both up), `Cors__Origins`.

## What's built

| Area | Highlights |
|---|---|
| Counter | Keyboard-first POS (F2/F8/F9, barcode), FEFO batches, strip + loose units, GST-inclusive MRP, split payments, held bills, idempotent saves, returns/cancel, cashier discount cap, cash shifts with petty cash and variance |
| Inventory | Immutable movement ledger, no negative stock, opening stock, adjustments, stock counts (variance vs system at time of count), batch block/recall trace, near-expiry |
| Purchasing | Purchase entry with free goods, duplicate-invoice check, purchase orders with partial receipts, reorder suggestions, CSV invoice import with learned supplier item codes (AI PDF reading is a stub until a provider is approved) |
| Money | Returns to supplier → credit note → write-off/reject, supplier payments & aging, customer credit with limits & receipts, double-entry ledger generated from documents, trial balance / P&L / balance sheet / cash book, informational GST summary, period lock, journal CSV + Tally XML export, control checks |
| Customers | Consent & opt-out history, customer-requested refill reminders via WhatsApp click-to-chat (no paid API), loyalty earn/redeem/reversal, duplicate merge, retention report |
| Multi-store | Branch-wise selling prices (never above MRP), branches & warehouses, branch-confined staff, transfers with in-transit and shortage, indents, branch comparison |
| Provider console | Plans & module gates, trials → read-only, manual subscription invoices & dunning, suspension, support tickets, owner-granted read-only audited support view, owner data export (ZIP), scheduled deletion & purge |
| Offline | Cached app + POS catalogue, offline bill queue with provisional numbers, safe sync, visible sync failures |
| Security | Account lockout, TOTP two-step sign-in (staff and provider), self-service password change, owner reset of staff passwords, API security headers |
| Online orders (gated) | Delivery charge with free-delivery threshold (GST rate configurable — CA to confirm), OTC-only public catalogue, Rx via prescription upload + pharmacist review, customer approval of changes, billing at packing, COD with handover code, delivery-agent role with limited data |

Roles: Owner · Pharmacist · Cashier · PurchaseClerk · Accountant · DeliveryAgent (server-enforced; the UI mirrors it).

## Must be verified by experts before real use

These are engineering defaults, **not** verified compliance:

- **CA:** GST maths and rounding, invoice contents/numbering, intra- vs inter-state tax, e-invoicing applicability,
  all accounting posting rules (see the comment at the top of `AccountingService`), loyalty accounting, write-offs,
  provisional receipts for offline bills.
- **Pharmacist / regulatory:** drug schedules and what must be recorded (prescriber/patient details are required here for
  H1, X and narcotic/psychotropic items), prescription retention, recall handling.
- **Online orders:** whether and how a licensed pharmacy may take orders online and deliver — the e-pharmacy framework was
  unsettled at last check. The `online` module is off for every plan until reviewed.
- **Data protection (DPDP):** consent wording, retention periods, breach process, processor agreements, data deletion.
- Licence and GSTIN fields are stored as records only; nothing is verified with authorities.

## Known limitations

- Browser tests cover onboarding → purchase → billing, offline billing + sync, all main screens, cashier permissions and the
  provider console; less-used screen actions are still only type-checked.
- No payment gateway, SMS/WhatsApp API, AI provider, or e-invoice/GSP integration is connected (none approved).
- The Tally XML export has not been imported into a live Tally yet; forgot-password by email/SMS needs a
  messaging provider (owners reset staff passwords instead).
