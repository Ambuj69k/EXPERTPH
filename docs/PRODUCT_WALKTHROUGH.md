# PharmaOS — Product Walkthrough

> **Run your pharmacy from the counter to the books — in one place.**
> Billing, batch-wise stock, purchases, customer credit and accounts that stay in step on their own.
> Built for Indian pharmacies, from one store to a chain — and for distributors, field forces and hospitals.

*Presenter notes are in the "🗣 Say" lines. Remove them before sending this file to a client.*

---

## 1. The problem: small leaks add up in every pharmacy

| Leak | What happens today |
|---|---|
| **Expired stock nobody returned** | Near-expiry strips are noticed too late, and supplier credit notes are never chased. |
| **Shelf and system disagree** | Stock on paper is not stock on the shelf. Nobody knows which batch went where. |
| **Credit given, never collected** | Regular customers run up dues with no limit and no reminder. |
| **Month-end accounts chaos** | Bills are re-typed into Tally; GST figures are pieced together for the CA. |
| **Internet down, counter down** | Online-only software stops billing exactly when the queue is longest. |
| **Too many tools** | Billing software, Excel, Tally, a notebook and WhatsApp — none of them talk. |

🗣 Say: Ask which of these six hurts them most, and come back to it when you reach that section.

---

## 2. One system for the counter, the stockroom and the books

- Runs in the browser on any PC, tablet or phone — nothing to install, no server in the shop.
- Billing keeps working when the internet drops.
- Every bill, purchase and payment lands in the accounts by itself.

---

## 3. At the counter — billing in seconds, even offline

| Feature | What it means |
|---|---|
| Keyboard-first billing | Barcode or name search, F-keys to save and print. |
| Strips and loose tablets | Sell a full strip or 3 tablets; stock is kept to the tablet. |
| GST inside MRP | Tax split worked out from MRP; bills rounded to the rupee. |
| Split payments, returns, held bills | Cash + UPI + card on one bill; cashier discount limits. |
| Prescriber details for H1, X, narcotics | The bill will not save without patient and doctor details. |
| Works without internet | Bills queue on the PC and sync safely when the line returns. |

🗣 Say: Show the billing screen live — search, F9 to save, print. Then disconnect the network and bill again.

---

## 4. Inventory — stock that tells the truth

- **Earliest expiry sells first (FEFO):** the system picks the batch, so old stock moves before it expires.
- **Stock never goes negative:** you cannot sell what the system says you do not have.
- **Stock counts with variance:** count a rack or the whole shop; differences post with a reason.
- **Batch block and recall trace:** block a recalled batch in one click and see who bought it.
- **Near-expiry and reorder lists:** what expires in 90 days, and what to buy based on real sales.

---

## 5. Purchases & suppliers — buy right, and get your money back

- **Purchase entry with free goods:** schemes, discounts and duplicate-invoice checks built in.
- **Import the supplier's invoice file:** CSV import that remembers each supplier's item codes.
- **Purchase orders:** order from reorder suggestions; receive in parts against the order.
- **Expiry and damage returns:** return to supplier → credit note → settled or written off.
- **Recovery report:** what every supplier still owes you, and how old it is.

🗣 Say: Most owners have never seen how much money is stuck in unreturned expiry claims — the recovery report shows it.

---

## 6. Customers — keep regulars coming back, and paying

- **Customer credit with limits:** udhaar stops at the limit; receipts settle the oldest bills first.
- **Statements and dues:** who owes what, since when, in one screen.
- **Refill reminders:** for customers who ask; sent in one tap via WhatsApp.
- **Loyalty points:** earn on bills, redeem at the counter, reversed on returns.
- **Consent on record:** opt-in and opt-out kept with the date.

🗣 Say: Reminders open WhatsApp with the message ready. Fully automatic sending needs a paid WhatsApp API, which can be added.

---

## 7. Accounts — books without the month-end panic

- **Ledger kept for you:** journals are created from every bill, purchase, return and payment.
- **P&L, balance sheet, cash book:** trial balance and reports any day, not just at year end.
- **GST summary for your CA:** sales and purchase tax by rate, ready to hand over.
- **Tally export:** send the month to Tally if your CA works there.
- **Period lock and control checks:** closed months stay closed; stock and dues tie back to the books.

🗣 Say: The numbers are prepared for the CA, who still reviews and files. Try the Tally export once with their CA before relying on it.

---

## 8. More than one store — grow to a chain without losing control

- **Branches and a central warehouse:** staff work only in their branch; the owner sees everything.
- **Branch-wise selling prices:** set a lower price per branch — never above MRP.
- **Transfers and indents:** stock in transit is tracked; shortages on arrival are recorded.
- **Branch comparison:** sales, margin and stock side by side for every store.
- **Online orders (when you are ready):** order page, prescription upload, pharmacist review, delivery.

🗣 Say: Online orders are built but switched off by default — enabled only after confirming it is allowed under current e-pharmacy rules.

---

## 9. Beyond retail — one platform for the whole pharma chain

| Distributors | Field force (MR) | Hospital pharmacy | Manufacturers *(pilot)* |
|---|---|---|---|
| Retailer accounts and credit limits | Doctors and territories | Ward indents and issues | Recipes and batch genealogy |
| PTR rates and schemes (10+1) | Tour plans and daily call reports | High-alert double check | QC results, signed |
| Salesman orders on the phone | Works offline in the field | Narcotic register | QA release by e-signature |
| B2B GST invoices (IGST / CGST+SGST) | Samples and expenses | Ward returns | Deviations and CAPA |
| Dispatch and collections | Targets and coverage | Department-wise cost | |

🗣 Say: Show this only to prospects in these segments. Manufacturing is a pilot and is not validated for GMP decisions — say so plainly if asked.

---

## 10. Trust & security — your data stays yours, and stays safe

- **Walled off from others:** each pharmacy's data is kept separate; no other customer can see it.
- **The right access per role:** owner, pharmacist, cashier, purchase clerk, accountant — each sees only their screens.
- **Two-step sign-in:** authenticator-app codes for owners and accountants; lockout after wrong passwords.
- **Every action recorded:** cancellations, price changes and overrides are logged with who and when.
- **Export everything, any time:** download all your data as files — no lock-in.
- **Nightly backups:** database and keys backed up together, with regular restore drills.

🗣 Say: Support staff can view a customer's data only when the owner grants time-limited access, and every screen they open is logged for the owner.

---

## 11. Plans — start small, add modules as you grow

| Plan | Best for | Stores / users | Per month* |
|---|---|---|---|
| Single store | One pharmacy, billing to books | 1 / 5 | ₹999 |
| Small chain | Multi-store with accounts | 10 / 30 | ₹2,499 |
| Distributor | B2B selling, salesmen, dispatch | 3 / 25 | ₹4,999 |
| Field force | Medical reps and managers | 3 / 60 | ₹2,999 |
| Hospital pharmacy | Central store and wards | 5 / 80 | ₹7,999 |
| Enterprise | Everything, custom limits | Custom | Quoted |

\* Indicative pilot pricing, excluding GST. Every plan starts with a **30-day free trial**.

🗣 Say: These are the current working prices in the product. Confirm the final price before quoting.

---

## 12. Getting started — live in your store in four steps

1. **Free trial account** — we set up your store, branches and staff logins.
2. **Bring your data** — product list, opening stock and suppliers imported from your files.
3. **Train the team** — each role learns only its own screens, at your counter.
4. **Go live, review together** — we check stock, dues and books with you after the first month.

No hardware to buy — the PC, printer and barcode scanner you have today will do.

---

## Next step

**Let's run a 30-day pilot in your store** — see your own expiry, stock and dues in PharmaOS before you decide.

**[Your name]** · [Phone] · [Email]
