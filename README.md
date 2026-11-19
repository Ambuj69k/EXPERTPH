# PharmaOS — pharmacy business platform

Multi-tenant SaaS for Indian retail pharmacies and small chains: billing, batch-wise inventory, purchasing, supplier
returns & credit recovery, customer credit & CRM, cash control, double-entry books, multi-store, provider console,
offline billing and (gated) online ordering.

Stack: **.NET 9 modular monolith (EF Core, SQL Server)** + **React 19 (Vite, TypeScript)**.
Delivery plan and status: [docs/ROADMAP.md](docs/ROADMAP.md) · offline design: [docs/OFFLINE.md](docs/OFFLINE.md) · operations & security: [docs/OPERATIONS.md](docs/OPERATIONS.md) ·
non-retail tracks: [docs/SEPARATE_TRACKS.md](docs/SEPARATE_TRACKS.md)

## Deploy

One server with Docker: `cp .env.example .env`, fill it, `docker compose up -d --build`. Full guide (HTTPS, backups, upgrades, CI): [docs/DEPLOY.md](docs/DEPLOY.md).

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
| Distributor (gated) | Retailer accounts with GSTIN/licence expiry/credit limits, trade rates (default + per-retailer PTR), quantity schemes and trade discounts, salesman order booking (phone-friendly), FEFO trade invoices with free goods and IGST vs CGST+SGST, credit-limit/overdue block with owner override + reason, credit notes (saleable restock / write-off), collections with FIFO allocation, aging and salesman report, pick list & vehicle dispatch with delivery confirmation, posted to the books (trade receivables) |
| Field force / MR (gated) | Territories (one MR, one area manager), doctor master with class A/B/C, consent date and on-request anonymisation, monthly tour plan with manager approval, daily call report (phone-friendly, 7-day window, calls saved on the phone when offline and sent later, optional per-call location), sample bag per MR (issue / give / return, no negative), report approval, coverage vs visit norms and plan adherence, expense claims with approval, monthly targets & scorecard |
| Hospital pharmacy (gated) | Departments/wards as cost centres, ward staff confined to their department, formulary with high-alert flags, ward indents (urgent first, HIS encounter ref without patient names), FEFO issues with a mandatory second pharmacist check for high-alert and narcotic/Schedule X items, partial issues and close-short, unused/expired/damaged returns, department consumption and indent turnaround, cost posted to the books; narcotic & Schedule X running register per batch (also for retail) |
| Manufacturing & QA (gated, pilot) | Versioned recipes and QC specs approved by QA with an e-signature, production batches that consume material lots FEFO (only QC-passed lots where a spec exists), output booked as quarantined stock, signed and immutable QC results judged against limits, QA release/reject with e-signature and tester ≠ releaser, batch genealogy both ways, WIP accounting, deviations & CAPA (open Major/Critical blocks release, QA closes with e-signature) |

Roles: Owner · Pharmacist · Cashier · PurchaseClerk · Accountant · DeliveryAgent · Salesman · MedicalRep · AreaManager · WardStaff · Production · QcAnalyst · QaManager (server-enforced; the UI mirrors it).

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
- **Distributor:** B2B invoice format, scheme/free-goods tax treatment, credit-note GST, e-way bill and e-invoicing thresholds must be checked by a CA before a distributor uses it; e-way bill / IRN generation is not built.
- **Field force:** doctor data is personal data (DPDP) — legal basis, consent wording and retention need review; employee location capture needs an employment-law/policy review (it is opt-in per call here); sample and gift rules (e.g. the pharma marketing code) need compliance review; visit norms (A 4, B 2, C 1) are placeholders.
- **Hospital:** high-alert list and dual-check rules must come from the hospital's pharmacy & therapeutics committee; the narcotic register is built from the stock ledger but its statutory format must be confirmed with the drug controller; HIS integration (HL7/FHIR) is not built — patient-level dispensing, billing and medication administration stay in the HIS.
- **Manufacturing:** a pilot slice only — **not validated for GMP decisions**. Computer system validation (IQ/OQ/PQ), document control, training records, change control, equipment and stability are not built; a QA/GMP consultant and validation partner are required before real use.

## Known limitations

- Browser tests cover onboarding → purchase → billing, offline billing + sync, all main screens, cashier permissions, the distributor order → invoice → collection flow, the MR call report (incl. offline save) → approval flow, the hospital indent → issue → second-check flow, the manufacturing QC → QA e-signature release flow and the
  provider console; less-used screen actions are still only type-checked.
- No payment gateway, SMS/WhatsApp API, AI provider, or e-invoice/GSP integration is connected (none approved).
- The Tally XML export has not been imported into a live Tally yet; forgot-password by email/SMS needs a
  messaging provider (owners reset staff passwords instead).
