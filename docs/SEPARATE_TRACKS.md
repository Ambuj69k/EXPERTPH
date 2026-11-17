# Separate product tracks — scoping blueprints

These were deliberately **not** built into the retail product. Each has a different buyer, daily users, workflows and
regulatory surface; bundling them would recreate the "oversized initial product" the blueprint warns against.
Start a track only when the evidence in its "Go when" line exists.

What every track can reuse from the current platform: multi-tenant core, auth/roles/branch access, audit log, product &
batch masters, the inventory ledger (`InventoryService`), GST-inclusive/exclusive maths (`Money`), document numbering,
double-entry posting engine, payables/receivables, SaaS provider console, plans & module gates, export/purge.

---

## L. Distributor / stockist

**Buyer:** distributor owner. **Users:** billing staff, godown/pickers, salesmen, collection agents.

| Reuse as-is | Needs a different design |
|---|---|
| Purchases from manufacturers/C&F, batch/expiry stock, supplier payables, returns to supplier, accounting | Selling is B2B: **retailer accounts** with drug-licence & GSTIN records, credit limits and terms per retailer |
| Receivables & receipts, aging | **Rate structures**: PTR/PTS, customer-specific price lists, **schemes** (free qty, % off, slab), trade discount — retail MRP-based pricing does not apply |
| Transfers between godowns | **Order capture** (salesman app / retailer portal / phone) → allocation → picking → packing → dispatch → delivery with route planning |
| | Inter-state supply (IGST), e-way bill and e-invoicing thresholds — **verify current rules** |
| | Expiry/breakage **claims from retailers** (the mirror image of the retail returns module) and claims to manufacturers |

**MVP slice:** retailer master + B2B sales invoice with schemes and credit control + salesman order app + collections.
**Prerequisites:** a distributor design partner; CA review of B2B GST/e-invoicing; scheme rules from real price lists.
**Go when:** ≥ 3 paying retail customers ask their distributor to connect, or a distributor commits to a paid pilot.

## M. Medical representative CRM / SFA

**Buyer:** pharma marketing company sales head. **Users:** MRs, area/regional managers.

| New domain | Notes |
|---|---|
| Territories, MR hierarchy, doctor & institution master | Doctor data is personal data: consent, purpose limitation and retention under the DPDP framework — **legal review** |
| Visit plans (tour programme), DCR / activity reporting, geo check-in | Location tracking of employees needs consent and policy — **employment law review** |
| Sample & gift distribution tracking | Rules on promotion, samples and gifts to doctors (e.g. pharma marketing codes) — **verify current code/law** |
| Expenses & approvals, targets & incentives | Payroll/incentive calculation should integrate with HR software, not be rebuilt |

**MVP slice:** doctor master + tour plan + daily call report + manager approval, mobile-first, offline-capable.
**Prerequisites:** a marketing-company design partner; compliance review of sampling/gifting.
**Go when:** a company with ≥ 20 MRs agrees to a paid pilot. This is effectively a different product and sales motion.

## N. Hospital pharmacy

**Buyer:** hospital administration / IT. **Users:** pharmacists, nurses, ward staff, billing.

| Reuse | New / different |
|---|---|
| Stock, batches, expiry, purchases, transfers (store → sub-store/ward) | **HIS integration** (patient, encounter/admission, orders) — usually HL7 v2/FHIR or vendor APIs; each hospital differs |
| Indents (ward requests) map to existing indents | **Inpatient dispensing** against doctor orders, returns from wards, **department cost allocation** |
| Accounting export | Formulary management, high-alert/narcotic controls with dual sign-off |
| | Medication administration records belong to clinical systems — **do not** extend retail billing to cover MAR |

**MVP slice:** central pharmacy store + ward indents/issues + returns + department consumption reports, with patient
billing staying in the HIS.
**Prerequisites:** a hospital partner and its HIS vendor's interface specs; clinical pharmacist input.
**Go when:** a hospital commits budget for integration work. Sales cycles and support expectations are enterprise-grade.

## O. Manufacturing & quality

**Buyer:** plant head / QA head of a manufacturer. **Users:** production, QC lab, QA, stores, regulatory affairs.

Why this is much heavier:
- Bill of materials and recipes with potency/assay adjustments, yield, and batch genealogy.
- **Batch manufacturing records**, in-process checks, QC testing against specifications, **QA batch release**.
- Deviations, **CAPA**, change control, document control with versioning and training records.
- Electronic records and signatures must meet GMP expectations (e.g. audit trails, signature meaning, data integrity) —
  software used in GMP processes typically needs **computer system validation (IQ/OQ/PQ)** by specialists.
- Equipment calibration and maintenance, environmental monitoring, stability studies.

**MVP slice (if ever):** materials & batch genealogy + QC sample/result recording + batch release workflow — only with a
QA consultant and a validation partner.
**Prerequisites:** GMP/regulatory expert on the team, validation documentation process, a manufacturer design partner.
**Go when:** a funded engagement exists. Not suitable for a solo founder.

---

### Recommendation
Keep the next 6–12 months on retail (single store → small chains) with paid pilots. If an adjacent track is needed,
**distributor** is the closest fit: it reuses the most code and many retail customers already depend on distributors.
