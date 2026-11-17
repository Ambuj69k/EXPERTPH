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
| Distributor (L) | B2B catalog, retailer accounts, schemes, order capture, picking/dispatch, collections — scoped in SEPARATE_TRACKS.md | 🧭 |
| MR / SFA (M), Hospital (N), Manufacturing & QA (O) | Separate products; scoped in SEPARATE_TRACKS.md with go/no-go evidence; not built on the retail roadmap | 🧭 |

Items that always require expert sign-off before real-world use are listed in the main README.
