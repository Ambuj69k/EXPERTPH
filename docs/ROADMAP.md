# PharmaOS delivery roadmap

Each phase ships working code + integration tests + UI, and is verified before the next starts.
Status: ✅ done · 🔨 in progress · ⏳ planned · 🧭 separate product track (scoped later)

| Phase | Scope (blueprint modules) | Status |
|---|---|---|
| 1. Retail core | Onboarding (A), product master (B), POS billing (C), batch inventory ledger (D), purchase entry (E), sales returns/cancel, near-expiry + daily sales (R), users/roles, audit (Q) | ✅ |
| 2. Money owed & recovery | Expiry/damage returns to supplier with dispatch → credit note → write-off/reject (F); supplier payments, outstanding, aging, statements (E/G); customer credit, receipts, credit limits, statements (G/H); return-recovery report (R) | ✅ |
| 3. Counter controls | Cash shifts & drawer reconciliation with petty cash (C), stock count / cycle count with variance posting (D), batch block/quarantine + recall trace (D), counter settings | ✅ |
| 4. Customer & CRM | Customer profiles, consent & opt-out, purchase history, refill reminders (customer-requested), loyalty earn/redeem/reversal, duplicate merge (H), notification log + templates (S), retention report (R) | ✅ |
| 5. Purchasing automation | Reorder suggestions, purchase orders + partial GRN, supplier item-code mapping, CSV invoice import with mapping, AI-assisted PDF extraction with mandatory human review (E, U) — AI extractor is a pluggable stub until a provider is approved | ✅ |
| 6. Accounting | Chart of accounts, double-entry journals posted from documents, cash/bank book, trial balance, P&L, balance sheet, period close, journal CSV export, control checks, informational GST summary, Accountant role, Tally XML export (not yet verified in a live Tally) (G) | ✅ |
| 7. Multi-store | Multiple branches/warehouse, branch switching & access, transfers with in-transit & discrepancy, indents, branch comparison, branch-wise selling prices capped at MRP (K) — inter-entity transactions out of scope | ✅ |
| 8. SaaS provider admin | Tenant provisioning, plans & entitlements, trials, subscription billing, audited support access, data export & offboarding (V) — payments recorded manually (no gateway approved) | ✅ |
| 9. Offline billing | Browser offline mode: cached app + POS catalogue, offline bill queue with idempotent sync, provisional numbers, visible sync failures (decision: no local server agent — see OFFLINE.md) | ✅ |
| 10. Online orders & delivery | Ordering portal, Rx upload + pharmacist review, reservation, delivery, COD reconciliation (I, J) — built behind the online module, OFF for every plan; enable per customer only after a compliance review. Delivery charges added; online payments deferred (no gateway approved) | ✅ |
| 11. Security hardening | Account lockout, TOTP two-step sign-in (tenant + provider), password change/reset, security headers, operations runbook (backups, restore drill, incident outline) | ✅ |
| 12. Retail gaps | Browser (Playwright) test suite, Tally XML export, delivery charges, branch prices | ✅ |
| Distributor (L) | Retailer accounts, trade rates & schemes, salesman orders, FEFO B2B invoices (IGST / CGST+SGST), credit control with override, credit notes, collections, aging & salesman reports, picking list & vehicle dispatch with delivery confirmation, accounting — behind the `distributor` module. Not built: e-way bill / e-invoice (needs an approved GSP), retailer portal | ✅ |
| MR / SFA (M) | Territories, doctors (consent, anonymise), tour plan + approval, daily call reports (offline save on phone), sample bag, coverage, expense claims with approval, monthly targets & scorecard — behind the `sfa` module (plan `fieldforce`). Not built: incentive payout (belongs in HR/payroll), native app | ✅ |
| Hospital (N) | Departments, formulary + high-alert, ward indents, FEFO issues with second-pharmacist check, ward returns, consumption & turnaround, accounting — behind the `hospital` module. Narcotic & Schedule X running register (retail too). Not built: HIS integration (needs the vendor's interface spec), ward sub-store stock | ✅ |
| Manufacturing & QA (O) | Pilot slice: versioned recipes & QC specs, production batches with genealogy, QC-gated materials, quarantine, signed QC results, QA release with e-signature and separation of duties, deviations & CAPA (open Major/Critical blocks release), WIP accounting — behind the `manufacturing` module. NOT GMP-validated; change control, document/training records, equipment, stability not built | ✅ (pilot) |

Items that always require expert sign-off before real-world use are listed in the main README.
