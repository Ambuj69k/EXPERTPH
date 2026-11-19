# Operations & security runbook (draft)

Targets below are **proposals to validate**, not proven capabilities.

## Service targets (proposed)
| Item | Target |
|---|---|
| Bill save (p95, online) | < 1 s for a 10-line bill |
| Availability (business hours) | 99.5 % monthly; offline billing covers short outages |
| Recovery point objective (RPO) | ≤ 15 min (full nightly + log backups every 15 min) |
| Recovery time objective (RTO) | ≤ 4 h |

## What must be backed up — together
1. SQL Server database (`PharmaOS_*`): full nightly, transaction-log every 15 min, copies kept off the server.
2. **Data Protection keys** (`Security:KeysPath`, dev: `D:\PharmaOS\keys`). Without them, enabled two-step sign-in seeds
   cannot be decrypted and those users must be reset. Back up with the database; restrict access like a secret.
3. Uploaded files (`Storage:Root`, dev: `D:\PharmaOS\files`) — prescriptions; sensitive.

## Restore drill (monthly)
1. Restore the latest full + logs to a scratch server; start the API pointing at it with the backed-up keys and files.
2. Check: sign-in (incl. a two-step user), today's bills visible, `/api/v1/accounting/control-checks` all zero,
   trial balance balanced, one prescription file opens.
3. Record time taken vs RTO and any gaps.

## Secrets
Never in the repo: `ConnectionStrings__Pharma`, `Jwt__Key` (≥ 32 random chars), `Platform__Bootstrap*` (first run, then remove),
any provider API keys. Rotating `Jwt__Key` signs everyone out (acceptable).

- Outside Development the API refuses to start if `Jwt:Key` or `Platform:BootstrapPassword` is still the sample value from `appsettings.Development.json` (those samples are public in the repository).

## Access controls already enforced
Tenant isolation (query filters + write guard), role policies, branch confinement for staff, immediate effect of user
deactivation / branch reassignment / tenant suspension, account lockout (5 failures → 15 min), optional TOTP two-step
sign-in (strongly recommended for owners, accountants and all provider staff), owner-granted read-only audited support
sessions, provider tokens can't call tenant APIs and vice versa, audit log for business and sensitive actions
(prescription views, support requests, exports).

## Incident response (outline — confirm legal duties with counsel)
1. Contain: suspend affected tenant(s) or rotate `Jwt__Key`; revoke support access; disable compromised users.
2. Preserve: snapshot DB, logs and audit events before changes.
3. Assess: which tenants/data categories; use audit events and DB history.
4. Notify: affected customers, and authorities/individuals **as required under the DPDP framework and contracts —
   timelines and content to be confirmed by legal counsel**.
5. Recover and review: root cause, fix, test, document.

## Data deletion & retention
- Customers can export everything (Owner → Settings → Download all my data).
- Provider schedules deletion (read-only meanwhile) then purges; tenant row kept as an anonymised tombstone, provider
  invoices kept as the provider's own records.
- **Retention periods for invoices, prescriptions and audit logs must be confirmed (tax and drug rules) before purging
  real customers.**

## Not yet in place
Automated backups and monitoring/alerting (hosting-dependent), centralised log shipping, WAF/DDoS protection,
penetration test, CAPTCHA on public ordering pages, mandatory (enforced) MFA policy.
