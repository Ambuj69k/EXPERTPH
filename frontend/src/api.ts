// Thin typed client for the PharmaOS API. All money/qty values arrive as JSON numbers (decimals on the server).

export type Role = 'Owner' | 'Pharmacist' | 'Cashier' | 'PurchaseClerk' | 'Accountant' | 'DeliveryAgent' | 'Salesman' | 'MedicalRep' | 'AreaManager' | 'WardStaff' | 'Production' | 'QcAnalyst' | 'QaManager';
export type Schedule = 'None' | 'H' | 'H1' | 'X' | 'NarcoticPsychotropic' | 'G';
export type PaymentMode = 'Cash' | 'Card' | 'Upi' | 'Credit' | 'Loyalty';

export interface Session {
  tenantId: string; tenantName: string; userId: string; displayName: string; email: string;
  role: Role; branchId: string; branchName: string; branchCode: string;
}
export interface AuthResponse { token: string; expiresAt: string; session: Session }
export interface MfaPending { mfaRequired: true; mfaToken: string }
export const isMfaPending = (r: object): r is MfaPending => 'mfaRequired' in r;
export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number }

export interface Product {
  id: string; name: string; composition?: string | null; manufacturer?: string | null; hsnCode?: string | null;
  gstRate: number; packSize: number; packUnit: string; baseUnit: string; allowLooseSale: boolean; schedule: Schedule;
  barcode?: string | null; reorderLevelUnits: number; isActive: boolean; version: number; onlineListed?: boolean;
}
export interface Supplier {
  id: string; name: string; gstin?: string | null; phone?: string | null; email?: string | null; address?: string | null;
  drugLicenseNo?: string | null; paymentTermsDays: number; isActive: boolean; version: number;
}
export interface PosBatch { batchId: string; batchNo: string; expiryDate: string; mrp: number; qtyUnits: number }
export interface PosProduct {
  productId: string; name: string; composition?: string | null; manufacturer?: string | null; packSize: number;
  packUnit: string; baseUnit: string; allowLooseSale: boolean; gstRate: number; schedule: Schedule;
  availableUnits: number; batches: PosBatch[]; barcode?: string | null; branchPricePerPack?: number | null;
}
export interface SaleLine {
  id: string; lineNo: number; productId: string; productName: string; hsnCode?: string | null; schedule: Schedule;
  packSize: number; packUnit: string; baseUnit: string; batchId: string; batchNo: string; expiryDate: string;
  qtyUnits: number; mrpPerUnit: number; grossAmount: number; discountPercent: number; discountAmount: number;
  gstRate: number; taxableAmount: number; taxAmount: number; netAmount: number; returnedQtyUnits: number;
}
export interface Sale {
  id: string; invoiceNo: string; invoiceAt: string; businessDate: string; status: 'Posted' | 'Cancelled';
  customerId?: string | null; customerName?: string | null; customerPhone?: string | null; doctorName?: string | null;
  grossAmount: number; discountAmount: number; taxableAmount: number; taxAmount: number; roundOff: number; netAmount: number;
  cancelReason?: string | null; capturedOffline?: boolean; offlineRef?: string | null;
  deliveryCharge?: number; deliveryChargeTaxable?: number; deliveryChargeTax?: number; deliveryChargeGstRate?: number; lines: SaleLine[]; payments: { mode: PaymentMode; amount: number; reference?: string | null }[];
  header: { businessName: string; branchName: string; address?: string | null; phone?: string | null; gstin?: string | null; drugLicenseNo?: string | null; stateCode: string };
}
export interface SaleListItem {
  id: string; invoiceNo: string; invoiceAt: string; status: string; customerName?: string | null;
  customerPhone?: string | null; netAmount: number; lineCount: number;
}
export interface SalesReturn {
  id: string; returnNo: string; salesInvoiceId: string; invoiceNo: string; returnAt: string; reason: string;
  refundMode: PaymentMode; refundAmount: number;
  lines: { salesInvoiceLineId: string; productName: string; batchNo: string; qtyUnits: number; amount: number }[];
}
export interface PurchaseLine {
  id?: string; lineNo?: number; productId: string; productName?: string; packSize?: number; batchNo: string;
  expiryDate: string; mrp: number; rate: number; qtyPacks: number; freeQtyPacks: number; discountPercent: number;
  gstRate: number | null; taxableAmount?: number; taxAmount?: number; lineTotal?: number;
}
export interface Purchase {
  id: string; supplierId: string; supplierName: string; supplierInvoiceNo: string; invoiceDate: string;
  status: 'Draft' | 'Posted' | 'Cancelled'; grossAmount: number; discountAmount: number; taxableAmount: number;
  taxAmount: number; otherCharges: number; roundOff: number; netAmount: number; notes?: string | null;
  createdAt: string; postedAt?: string | null; cancelReason?: string | null; version: number; lines: PurchaseLine[];
  purchaseOrderId?: string | null;
}
export interface PurchaseListItem {
  id: string; supplierName: string; supplierInvoiceNo: string; invoiceDate: string; status: string; netAmount: number; lineCount: number;
}
export interface StockRow {
  productId: string; name: string; manufacturer?: string | null; packSize: number; packUnit: string; baseUnit: string;
  qtyUnits: number; valueAtCost: number; reorderLevelUnits: number; belowReorder: boolean;
  batches: { batchId: string; batchNo: string; expiryDate: string; mrp: number; costPerUnit: number; qtyUnits: number; isExpired: boolean }[];
}
export interface NearExpiryRow {
  productId: string; productName: string; manufacturer?: string | null; batchId: string; batchNo: string; expiryDate: string;
  daysToExpiry: number; qtyUnits: number; packSize: number; valueAtCost: number; valueAtMrp: number; isExpired: boolean;
}
export interface DailySales {
  date: string; invoiceCount: number; cancelledCount: number; grossAmount: number; discountAmount: number;
  taxableAmount: number; taxAmount: number; netSales: number; returns: number; netAfterReturns: number;
  costOfGoods: number; grossMargin: number;
  paymentsByMode: { mode: PaymentMode; amount: number }[]; refundsByMode: { mode: PaymentMode; amount: number }[];
}
export interface User { id: string; email: string; displayName: string; role: Role; defaultBranchId: string; isActive: boolean; departmentId?: string | null }
export interface Movement { at: string; type: string; batchNo: string; qtyUnits: number; sourceType: string; sourceId: string; note?: string | null }

export type SettlementMode = 'Cash' | 'BankTransfer' | 'Upi' | 'Cheque' | 'Card';
export type ReturnReason = 'Expired' | 'NearExpiry' | 'Damaged' | 'Recall' | 'Other';
export type ReturnStatus = 'Draft' | 'Dispatched' | 'Settled' | 'Rejected';

export interface ReturnCandidate {
  batchId: string; productId: string; productName: string; manufacturer?: string | null; batchNo: string; expiryDate: string;
  daysToExpiry: number; qtyOnHand: number; qtyInOpenReturns: number; costPerUnit: number; gstRate: number; claimValue: number;
  suggestedSupplierId?: string | null; suggestedSupplierName?: string | null;
}
export interface PurchaseReturn {
  id: string; returnNo: string; supplierId: string; supplierName: string; reason: ReturnReason; status: ReturnStatus;
  claimAmount: number; acceptedAmount: number; writtenOffAmount: number; supplierCreditNoteNo?: string | null;
  creditNoteDate?: string | null; notes?: string | null; rejectReason?: string | null; createdAt: string;
  dispatchedAt?: string | null; settledAt?: string | null; version: number;
  lines: { id: string; lineNo: number; productId: string; productName: string; batchId: string; batchNo: string; expiryDate: string;
    qtyUnits: number; costPerUnit: number; gstRate: number; claimAmount: number }[];
}
export interface PurchaseReturnListItem {
  id: string; returnNo: string; supplierName: string; reason: ReturnReason; status: ReturnStatus; claimAmount: number;
  acceptedAmount: number; createdAt: string; dispatchedAt?: string | null; ageDays: number;
}
export interface AgingBucket { label: string; amount: number; count: number }
export interface RecoveryReport {
  pendingClaims: number; pendingCount: number; claimed: number; accepted: number; writtenOff: number; recoveryPercent: number;
  pendingAging: AgingBucket[];
  bySupplier: { supplierId: string; supplierName: string; pending: number; claimed: number; accepted: number; writtenOff: number }[];
}
export interface PartyBalance {
  partyId: string; name: string; phone?: string | null; billed: number; paid: number; credits: number; balance: number;
  overdue: number; oldestDue?: string | null; creditLimit?: number | null;
}
export interface OpenDocument { id: string; documentNo: string; date: string; dueDate: string; daysOverdue: number; total: number; settled: number; outstanding: number }
export interface Statement {
  partyId: string; name: string; balance: number; unallocatedCredit: number;
  lines: { date: string; type: string; documentNo: string; debit: number; credit: number; balance: number; note?: string | null }[];
  openDocuments: OpenDocument[];
}
export interface Aging { buckets: AgingBucket[]; unallocatedCredit: number; netOutstanding: number }
export interface LedgerDoc {
  id: string; number: string; partyId: string; partyName: string; date: string; mode: SettlementMode; amount: number;
  allocated: number; reference?: string | null; status: 'Posted' | 'Cancelled'; cancelReason?: string | null;
}
export interface Customer { id: string; name: string; phone?: string | null; creditLimit?: number | null }

export interface ShiftSummary {
  id: string; shiftNo: string; userId: string; userName: string; status: 'Open' | 'Closed'; openedAt: string; closedAt?: string | null;
  billCount: number; openingCash: number; cashSales: number; cancelledCash: number; cashReceipts: number; cashRefunds: number;
  cashSupplierPayments: number; payIns: number; payOuts: number; expectedCash: number; countedCash?: number | null; variance?: number | null;
  upiSales: number; cardSales: number; creditSales: number; closeNotes?: string | null;
  movements: { kind: 'PayIn' | 'PayOut'; amount: number; reason: string; at: string }[];
}
export interface ShiftListItem {
  id: string; shiftNo: string; userName: string; status: string; openedAt: string; closedAt?: string | null;
  expectedCash?: number | null; countedCash?: number | null; variance?: number | null;
}
export interface Settings { cashierMaxDiscountPercent: number; requireOpenShift: boolean }
export interface CountLine {
  id: string; productId: string; productName: string; baseUnit: string; batchId: string; batchNo: string; expiryDate: string;
  systemQtyNow: number; systemQtyAtCount?: number | null; countedQty?: number | null; variance?: number | null; costPerUnit: number;
}
export interface StockCount {
  id: string; countNo: string; title: string; status: 'InProgress' | 'Posted' | 'Cancelled'; createdAt: string; postedAt?: string | null;
  lineCount: number; countedLines: number; gainValue: number; lossValue: number; lines: CountLine[];
}
export interface StockCountListItem { id: string; countNo: string; title: string; status: string; createdAt: string; lineCount: number; countedLines: number }
export interface BatchRow {
  batchId: string; productId: string; productName: string; manufacturer?: string | null; batchNo: string; expiryDate: string;
  mrp: number; isBlocked: boolean; blockReason?: string | null; qtyOnHand: number;
}
export interface BatchTrace {
  batch: BatchRow; qtySold: number; qtyReturnedToSupplier: number;
  purchases: { purchaseId: string; supplierName: string; supplierInvoiceNo: string; invoiceDate: string; qtyUnits: number }[];
  sales: { invoiceId: string; invoiceNo: string; at: string; customerName?: string | null; customerPhone?: string | null; qtyUnits: number; returnedQtyUnits: number; status: string }[];
}

export type Channel = 'Sms' | 'WhatsApp' | 'Email' | 'Call';
export type Purpose = 'Reminders' | 'Promotions';
export interface ConsentState { channel: Channel; purpose: Purpose; granted: boolean; source: string; at: string }
export interface CustomerProfile {
  id: string; name: string; phone?: string | null; creditLimit?: number | null; isActive: boolean; mergedIntoId?: string | null; createdAt: string;
  billCount: number; totalSpend: number; firstPurchaseAt?: string | null; lastPurchaseAt?: string | null; balance: number;
  loyaltyPoints: number; loyaltyValue: number; consents: ConsentState[]; activeReminders: number;
}
export interface CustomerLookup { id: string; name: string; phone?: string | null; loyaltyPoints: number; loyaltyValue: number; balance: number; creditLimit?: number | null }
export interface PurchaseHistoryItem { invoiceId: string; invoiceNo: string; at: string; status: string; netAmount: number; items: string[] }
export interface Reminder {
  id: string; customerId: string; customerName: string; customerPhone?: string | null; productId: string; productName: string;
  intervalDays: number; nextDueDate: string; daysUntilDue: number; status: 'Active' | 'Paused' | 'Cancelled'; notes?: string | null;
  lastContactedAt?: string | null; whatsAppConsent: boolean; smsConsent: boolean;
}
export interface PreparedMessage { logId: string; status: string; recipient?: string | null; message: string; reason?: string | null; clickToChatUrl?: string | null }
export interface NotificationRow {
  id: string; customerId?: string | null; customerName?: string | null; channel: Channel; purpose: Purpose; templateKey: string;
  recipient?: string | null; message: string; status: string; provider: string; error?: string | null; createdAt: string; sentAt?: string | null;
}
export interface Template { key: string; channel: Channel; body: string; isActive: boolean; isDefault: boolean }
export interface LoyaltySettings { enabled: boolean; rupeesPerPoint: number; pointValue: number; minRedeemPoints: number }
export interface LoyaltyAccount {
  customerId: string; points: number; value: number; settings: LoyaltySettings;
  entries: { type: string; points: number; invoiceNo?: string | null; note?: string | null; at: string }[];
}
export interface RetentionReport {
  from: string; to: string; totalBills: number; identifiedBills: number; identifiedSharePercent: number; uniqueCustomers: number;
  newCustomers: number; returningCustomers: number; repeatWithinPeriod: number; repeatRatePercent: number;
  topCustomers: { customerId: string; name: string; phone?: string | null; bills: number; spend: number }[];
}

export interface ReorderRow {
  productId: string; productName: string; manufacturer?: string | null; packSize: number; packUnit: string; soldUnits: number;
  avgDailyUnits: number; onHandUnits: number; onOrderUnits: number; reorderLevelUnits: number; targetUnits: number;
  suggestedPacks: number; lastSupplierId?: string | null; lastSupplierName?: string | null; lastRate?: number | null;
}
export type PoStatus = 'Draft' | 'Sent' | 'PartiallyReceived' | 'Received' | 'Closed' | 'Cancelled';
export interface PurchaseOrder {
  id: string; poNo: string; supplierId: string; supplierName: string; supplierPhone?: string | null; orderDate: string;
  expectedDate?: string | null; status: PoStatus; notes?: string | null; createdAt: string; sentAt?: string | null; version: number;
  estimatedValue: number; shareText: string; receipts: PurchaseListItem[];
  lines: { id: string; lineNo: number; productId: string; productName: string; packSize: number; qtyPacks: number;
    receivedQtyPacks: number; pendingQtyPacks: number; expectedRate?: number | null; gstRate: number }[];
}
export interface PurchaseOrderListItem {
  id: string; poNo: string; supplierName: string; orderDate: string; expectedDate?: string | null; status: PoStatus;
  lineCount: number; orderedPacks: number; receivedPacks: number;
}
export interface ImportMapping {
  itemCode?: string | null; itemName: string; batch?: string | null; expiry?: string | null; qty: string; free?: string | null;
  mrp?: string | null; rate?: string | null; discount?: string | null; gst?: string | null; pack?: string | null;
}
export interface ImportRow {
  rowNo: number; itemCode?: string | null; itemName: string; productId?: string | null; productName?: string | null; matchedBy?: string | null;
  suggestions: { productId: string; name: string; packSize: number }[]; batchNo?: string | null; expiryDate?: string | null;
  qtyPacks: number; freeQtyPacks: number; mrp?: number | null; rate?: number | null; discountPercent: number; gstRate?: number | null; issues: string[];
}
export interface ImportPreview { supplierId: string; headers: string[]; mapping: ImportMapping; rows: ImportRow[]; matched: number; unmatched: number }

export type AccountType = 'Asset' | 'Liability' | 'Equity' | 'Income' | 'Expense';
export interface LedgerAccount { id: string; code: string; name: string; type: AccountType; systemKey?: string | null; isActive: boolean }
export interface Journal {
  id: string; entryNo: string; date: string; sourceType: string; event: string; sourceNo?: string | null; sourceId: string; narration: string;
  reversesEntryId?: string | null; total: number; lines: { accountId: string; accountCode: string; accountName: string; debit: number; credit: number }[];
}
export interface TrialBalance { asOf: string; totalDebit: number; totalCredit: number; balanced: boolean;
  rows: { accountId: string; code: string; name: string; type: AccountType; debit: number; credit: number; balance: number }[] }
export interface StatementRow { code: string; name: string; amount: number }
export interface ProfitAndLoss { from: string; to: string; income: StatementRow[]; totalIncome: number; expenses: StatementRow[]; totalExpenses: number; netProfit: number }
export interface BalanceSheet { asOf: string; assets: StatementRow[]; totalAssets: number; liabilities: StatementRow[]; totalLiabilities: number;
  equity: StatementRow[]; currentEarnings: number; totalEquity: number; balanced: boolean }
export interface AccountLedger { accountId: string; code: string; name: string; from: string; to: string; opening: number; closing: number;
  rows: { date: string; entryNo: string; sourceType: string; sourceNo?: string | null; narration: string; debit: number; credit: number; balance: number }[] }
export interface GstSummary { from: string; to: string; outputTax: number; inputTax: number; returnsToSupplierTax: number; netTax: number;
  outwardSupplies: { rate: number; taxable: number; tax: number }[]; inwardSupplies: { rate: number; taxable: number; tax: number }[] }
export interface ControlCheck { name: string; ledger: number; subLedger: number; difference: number; note: string }

export interface Branch {
  id: string; name: string; code: string; address?: string | null; phone?: string | null; gstin?: string | null; stateCode: string;
  drugLicenseNo?: string | null; drugLicenseValidTo?: string | null; isWarehouse: boolean; isActive: boolean;
  onlineOrdersEnabled?: boolean; deliveryPincodes?: string | null; onlineNotice?: string | null;
  deliveryCharge?: number; freeDeliveryAbove?: number | null; deliveryChargeGstRate?: number;
}
export type TransferStatus = 'Draft' | 'Dispatched' | 'Received' | 'Cancelled';
export interface Transfer {
  id: string; transferNo: string; fromBranchId: string; fromBranch: string; toBranchId: string; toBranch: string; status: TransferStatus;
  notes?: string | null; receiveNotes?: string | null; createdAt: string; dispatchedAt?: string | null; receivedAt?: string | null;
  value: number; shortageValue: number; direction: 'in' | 'out';
  lines: { id: string; productId: string; productName: string; baseUnit: string; batchId: string; batchNo: string; expiryDate: string;
    qtyUnits: number; receivedQtyUnits?: number | null; shortageUnits?: number | null; costPerUnit: number }[];
}
export interface TransferListItem { id: string; transferNo: string; fromBranch: string; toBranch: string; status: TransferStatus; createdAt: string; dispatchedAt?: string | null; lineCount: number; direction: 'in' | 'out' }
export interface Indent {
  id: string; indentNo: string; requestingBranchId: string; requestingBranch: string; supplyingBranchId: string; supplyingBranch: string;
  status: 'Requested' | 'Fulfilled' | 'Rejected'; notes?: string | null; rejectReason?: string | null; createdAt: string;
  direction: 'raised' | 'received'; transferId?: string | null;
  lines: { productId: string; productName: string; baseUnit: string; qtyUnits: number; availableAtSupplier: number }[];
}
export interface BranchComparisonRow {
  branchId: string; branch: string; isWarehouse: boolean; bills: number; netSales: number; returns: number; grossMargin: number; marginPercent: number;
  stockValue: number; nearExpiryValue: number; customerDues: number; inTransitOut: number;
}

export interface PlanDef { code: string; name: string; priceNote: string; monthlyPriceHypothesis?: number | null; maxBranches?: number | null; maxUsers?: number | null; modules: string[] }
export interface Entitlements {
  planCode: string; planName: string; status: 'Trial' | 'Active' | 'PastDue' | 'Cancelled'; readOnly: boolean; notice?: string | null;
  trialEndsAt?: string | null; paidUntil?: string | null; modules: string[]; maxBranches?: number | null; maxUsers?: number | null;
  branches: number; users: number; supportAccessUntil?: string | null; deletionScheduledAt?: string | null;
}
export interface Ticket {
  id: string; tenantId: string; tenantName?: string | null; subject: string; status: 'Open' | 'WaitingOnCustomer' | 'Closed'; priority: string;
  createdAt: string; updatedAt: string; messages: { fromProvider: boolean; authorName: string; body: string; at: string }[];
}
export interface TenantRow {
  id: string; name: string; isActive: boolean; createdAt: string; planCode: string; status: string; trialEndsAt?: string | null; paidUntil?: string | null;
  branches: number; users: number; billsLast30Days: number; lastBillAt?: string | null; overdue: number; openTickets: number; migration: string;
  deletionScheduledAt?: string | null; deletedAt?: string | null;
}
export interface SubscriptionInput {
  planCode: string; status: string; trialEndsAt?: string | null; extraModules?: string | null; disabledModules?: string | null;
  maxBranchesOverride?: number | null; maxUsersOverride?: number | null; migration: string; notes?: string | null;
}
export interface PlatformInvoice {
  id: string; tenantId: string; tenantName: string; invoiceNo: string; periodFrom: string; periodTo: string; amount: number; dueDate: string;
  status: 'Due' | 'Paid' | 'Void'; paidAt?: string | null; paymentReference?: string | null; daysOverdue: number;
}
export interface TenantDetail {
  tenant: TenantRow; subscription: SubscriptionInput; modules: string[]; supportAccessUntil?: string | null; invoices: PlatformInvoice[]; tickets: Ticket[];
  recentAudit: { at: string; action: string; summary?: string | null }[];
}
export interface PlatformDashboard { tenants: number; active: number; trials: number; trialsExpired: number; pastDue: number; suspended: number; overdueAmount: number; openTickets: number; billsLast24h: number; plans: PlanDef[] }

export type OrderStatus = 'Submitted' | 'NeedsCustomerApproval' | 'Accepted' | 'Packed' | 'ReadyForPickup' | 'OutForDelivery' | 'Completed' | 'DeliveryFailed' | 'Rejected' | 'Cancelled';
export interface OnlineOrder {
  id: string; orderNo: string; status: OrderStatus; fulfilment: 'Pickup' | 'Delivery'; customerName: string; customerPhone?: string | null;
  address?: string | null; pincode?: string | null; requestText?: string | null; customerNotes?: string | null; pharmacistNotes?: string | null;
  prescriberName?: string | null; rejectReason?: string | null; salesInvoiceId?: string | null; invoiceNo?: string | null; amountDue?: number | null;
  deliveryAgentId?: string | null; deliveryAgentName?: string | null; deliveryAttempts: number; deliveryFailureReason?: string | null;
  createdAt: string; updatedAt: string;
  lines: { productId: string; productName: string; schedule: Schedule; requestedQtyUnits: number; approvedQtyUnits?: number | null; addedByPharmacist: boolean; availableUnits: number }[];
  files: { id: string; fileName: string; contentType: string; sizeBytes: number; uploadedAt: string }[];
}
export interface StoreInfo { branchId: string; businessName: string; branchName: string; address?: string | null; phone?: string | null; drugLicenseNo?: string | null; deliveryAvailable: boolean; deliveryPincodes: string[]; notice?: string | null; deliveryCharge: number; freeDeliveryAbove?: number | null }
export interface PublicProduct { productId: string; name: string; composition?: string | null; manufacturer?: string | null; packSize: number; packUnit: string; baseUnit: string; allowLooseSale: boolean; mrpPerPack: number; inStock: boolean; pricePerPack?: number | null }
export interface TrackView {
  orderNo: string; status: OrderStatus; fulfilment: string; storeName: string; storePhone?: string | null; createdAt: string; pharmacistNotes?: string | null;
  rejectReason?: string | null; handoverCode: string; amountDue?: number | null; prescriptionsUploaded: number;
  lines: { productName: string; requestedQtyUnits: number; approvedQtyUnits?: number | null; addedByPharmacist: boolean }[];
}

export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string) { super(message); }
}

const TOKEN_KEY = 'pharmaos.token';
export const tokenStore = {
  get: (): string | null => { try { return localStorage.getItem(TOKEN_KEY); } catch { return null; } },
  set: (t: string | null) => { try { t ? localStorage.setItem(TOKEN_KEY, t) : localStorage.removeItem(TOKEN_KEY); } catch { /* ignore */ } },
};

let onUnauthorized: () => void = () => {};
let onPlatformUnauthorized: () => void = () => {};
export const setPlatformUnauthorizedHandler = (fn: () => void) => { onPlatformUnauthorized = fn; };
const PLATFORM_KEY = 'pharmaos.platformToken';
export const platformTokenStore = {
  get: (): string | null => { try { return localStorage.getItem(PLATFORM_KEY); } catch { return null; } },
  set: (t: string | null) => { try { t ? localStorage.setItem(PLATFORM_KEY, t) : localStorage.removeItem(PLATFORM_KEY); } catch { /* ignore */ } },
};
export const setUnauthorizedHandler = (fn: () => void) => { onUnauthorized = fn; };

async function request<T>(method: string, path: string, body?: unknown, platform = false): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const token = platform ? platformTokenStore.get() : tokenStore.get();
  if (token) headers.Authorization = `Bearer ${token}`;

  let res: Response;
  try {
    res = await fetch(`/api/v1${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch {
    throw new ApiError(0, 'network', 'Cannot reach the server. Check your internet connection.');
  }
  if (res.status === 401 && token) (platform ? onPlatformUnauthorized : onUnauthorized)();
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  const data = text ? JSON.parse(text) : undefined;
  if (!res.ok) {
    const message = data?.detail
      ?? (res.status === 403 ? 'You do not have permission for this action.'
        : res.status === 429 ? 'Too many attempts in a short time. Please wait a minute and try again.'
        : `Request failed (${res.status}).`);
    throw new ApiError(res.status, data?.code ?? String(res.status), message);
  }
  return data as T;
}

/** Public ordering pages: no login, no token. */
export const publicApi = {
  store: (branchId: string) => request<StoreInfo>('GET', `/public/stores/${branchId}`),
  catalog: (branchId: string, q: string) => request<PublicProduct[]>('GET', `/public/stores/${branchId}/catalog${qs({ q })}`),
  placeOrder: (branchId: string, b: unknown) => request<{ orderNo: string; trackingToken: string; handoverCode: string }>('POST', `/public/stores/${branchId}/orders`, b),
  track: (token: string) => request<TrackView>('GET', `/public/orders/${token}`),
  approve: (token: string) => request<TrackView>('POST', `/public/orders/${token}/approve`, {}),
  cancel: (token: string) => request<TrackView>('POST', `/public/orders/${token}/cancel`, {}),
  upload: async (token: string, file: File) => {
    const fd = new FormData(); fd.append('file', file);
    const res = await fetch(`/api/v1/public/orders/${token}/prescriptions`, { method: 'POST', body: fd });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new ApiError(res.status, data.code ?? 'upload', data.detail ?? 'Upload failed.');
    return data as TrackView;
  },
};

/** SaaS provider console API (separate token; tenant pages never use it). */
export const platformApi = {
  login: (email: string, password: string) => request<{ token: string; expiresAt: string } | MfaPending>('POST', '/platform/auth/login', { email, password }, true),
  loginMfa: (mfaToken: string, code: string) => request<{ token: string; expiresAt: string }>('POST', '/platform/auth/mfa', { mfaToken, code }, true),
  mfaStatus: () => request<{ enabled: boolean }>('GET', '/platform/me/mfa', undefined, true),
  mfaSetup: () => request<{ secret: string; otpAuthUri: string }>('POST', '/platform/me/mfa/setup', {}, true),
  mfaEnable: (code: string) => request<void>('POST', '/platform/me/mfa/enable', { code }, true),
  dashboard: () => request<PlatformDashboard>('GET', '/platform/dashboard', undefined, true),
  tenants: () => request<TenantRow[]>('GET', '/platform/tenants', undefined, true),
  tenant: (id: string) => request<TenantDetail>('GET', `/platform/tenants/${id}`, undefined, true),
  updateSubscription: (id: string, b: SubscriptionInput) => request<TenantDetail>('PUT', `/platform/tenants/${id}/subscription`, b, true),
  setActive: (id: string, active: boolean) => request<void>('POST', `/platform/tenants/${id}/${active ? 'reactivate' : 'suspend'}`, {}, true),
  createInvoice: (id: string, b: unknown) => request<PlatformInvoice>('POST', `/platform/tenants/${id}/invoices`, b, true),
  markPaid: (id: string, reference: string) => request<PlatformInvoice>('POST', `/platform/invoices/${id}/paid`, { reference }, true),
  voidInvoice: (id: string) => request<void>('POST', `/platform/invoices/${id}/void`, {}, true),
  overdue: () => request<PlatformInvoice[]>('GET', '/platform/invoices/overdue', undefined, true),
  tickets: () => request<Ticket[]>('GET', '/platform/tickets', undefined, true),
  reply: (id: string, body: string, status: string) => request<Ticket>('POST', `/platform/tickets/${id}/reply`, { body, status }, true),
  supportSession: (id: string) => request<{ token: string; expiresAt: string }>('POST', `/platform/tenants/${id}/support-session`, {}, true),
  scheduleDeletion: (id: string, daysFromNow: number) => request<void>('POST', `/platform/tenants/${id}/schedule-deletion`, { daysFromNow }, true),
  cancelDeletion: (id: string) => request<void>('POST', `/platform/tenants/${id}/cancel-deletion`, {}, true),
  purge: (id: string) => request<void>('POST', `/platform/tenants/${id}/purge`, {}, true),
};

const qs = (params: Record<string, string | number | boolean | undefined | null>) => {
  const p = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => { if (v !== undefined && v !== null && v !== '') p.set(k, String(v)); });
  const s = p.toString();
  return s ? `?${s}` : '';
};

export const api = {
  register: (b: unknown) => request<AuthResponse>('POST', '/auth/register', b),
  login: (email: string, password: string) => request<AuthResponse | MfaPending>('POST', '/auth/login', { email, password }),
  loginMfa: (mfaToken: string, code: string) => request<AuthResponse>('POST', '/auth/mfa', { mfaToken, code }),
  mfaStatus: () => request<{ enabled: boolean }>('GET', '/me/mfa'),
  mfaSetup: () => request<{ secret: string; otpAuthUri: string }>('POST', '/me/mfa/setup', {}),
  mfaEnable: (code: string) => request<void>('POST', '/me/mfa/enable', { code }),
  mfaDisable: (password: string) => request<void>('POST', '/me/mfa/disable', { password }),
  changePassword: (currentPassword: string, newPassword: string) => request<void>('POST', '/me/password', { currentPassword, newPassword }),
  resetUserPassword: (id: string, newPassword: string) => request<void>('POST', `/users/${id}/reset-password`, { newPassword }),
  me: () => request<Session>('GET', '/me'),

  users: () => request<User[]>('GET', '/users'),
  createUser: (b: unknown) => request<User>('POST', '/users', b),
  setUserActive: (id: string, isActive: boolean) => request<void>('PUT', `/users/${id}/active`, { isActive }),

  products: (q = '', page = 1, includeInactive = false) => request<Paged<Product>>('GET', `/products${qs({ q, page, pageSize: 50, includeInactive })}`),
  createProduct: (b: unknown) => request<Product>('POST', '/products', b),
  updateProduct: (id: string, b: unknown) => request<Product>('PUT', `/products/${id}`, b),

  suppliers: (q = '', page = 1) => request<Paged<Supplier>>('GET', `/suppliers${qs({ q, page, pageSize: 50 })}`),
  createSupplier: (b: unknown) => request<Supplier>('POST', '/suppliers', b),
  updateSupplier: (id: string, b: unknown) => request<Supplier>('PUT', `/suppliers/${id}`, b),

  posSearch: (q: string) => request<PosProduct[]>('GET', `/pos/search${qs({ q })}`),
  posCatalog: () => request<PosProduct[]>('GET', '/pos/catalog'),
  branchPrices: (q: string, onlyWithPrice: boolean) => request<{ productId: string; productName: string; packSize: number; packUnit: string; currentMrp?: number | null; pricePerPack?: number | null; discountVsMrpPercent?: number | null; updatedAt?: string | null }[]>('GET', `/prices${qs({ q, onlyWithPrice })}`),
  setBranchPrice: (productId: string, pricePerPack: number | null) => request<void>('PUT', `/prices/${productId}`, { pricePerPack }),
  createSale: (b: unknown) => request<Sale>('POST', '/sales', b),
  sales: (p: { from?: string; to?: string; q?: string; page?: number }) => request<Paged<SaleListItem>>('GET', `/sales${qs({ ...p, pageSize: 50 })}`),
  sale: (id: string) => request<Sale>('GET', `/sales/${id}`),
  saleReturns: (id: string) => request<SalesReturn[]>('GET', `/sales/${id}/returns`),
  cancelSale: (id: string, reason: string) => request<Sale>('POST', `/sales/${id}/cancel`, { reason }),
  createReturn: (b: unknown) => request<SalesReturn>('POST', '/sales-returns', b),

  purchases: (p: { status?: string; page?: number }) => request<Paged<PurchaseListItem>>('GET', `/purchases${qs({ ...p, pageSize: 50 })}`),
  purchase: (id: string) => request<Purchase>('GET', `/purchases/${id}`),
  createPurchase: (b: unknown) => request<Purchase>('POST', '/purchases', b),
  updatePurchase: (id: string, b: unknown) => request<Purchase>('PUT', `/purchases/${id}`, b),
  deletePurchase: (id: string) => request<void>('DELETE', `/purchases/${id}`),
  postPurchase: (id: string) => request<Purchase>('POST', `/purchases/${id}/post`, {}),
  cancelPurchase: (id: string, reason: string) => request<Purchase>('POST', `/purchases/${id}/cancel`, { reason }),

  stock: (p: { q?: string; inStock?: boolean; belowReorder?: boolean; page?: number }) => request<Paged<StockRow>>('GET', `/stock${qs({ ...p, pageSize: 50 })}`),
  movements: (productId: string) => request<Movement[]>('GET', `/stock/movements/${productId}?take=100`),
  adjust: (b: unknown) => request<{ id: string; adjustmentNo: string; lineCount: number }>('POST', '/stock/adjustments', b),

  customers: (q = '') => request<Customer[]>('GET', `/customers${qs({ q })}`),
  createCustomer: (b: unknown) => request<Customer>('POST', '/customers', b),
  updateCustomer: (id: string, b: unknown) => request<Customer>('PUT', `/customers/${id}`, b),

  returnCandidates: (days: number) => request<ReturnCandidate[]>('GET', `/purchase-returns/candidates${qs({ days })}`),
  purchaseReturns: (p: { status?: string; page?: number }) => request<Paged<PurchaseReturnListItem>>('GET', `/purchase-returns${qs({ ...p, pageSize: 50 })}`),
  purchaseReturn: (id: string) => request<PurchaseReturn>('GET', `/purchase-returns/${id}`),
  createPurchaseReturn: (b: unknown) => request<PurchaseReturn>('POST', '/purchase-returns', b),
  deletePurchaseReturn: (id: string) => request<void>('DELETE', `/purchase-returns/${id}`),
  dispatchPurchaseReturn: (id: string) => request<PurchaseReturn>('POST', `/purchase-returns/${id}/dispatch`, {}),
  settlePurchaseReturn: (id: string, b: unknown) => request<PurchaseReturn>('POST', `/purchase-returns/${id}/settle`, b),
  rejectPurchaseReturn: (id: string, reason: string) => request<PurchaseReturn>('POST', `/purchase-returns/${id}/reject`, { reason }),
  recovery: () => request<RecoveryReport>('GET', '/reports/return-recovery'),

  payables: () => request<PartyBalance[]>('GET', '/payables'),
  payablesAging: () => request<Aging>('GET', '/payables/aging'),
  supplierStatement: (id: string) => request<Statement>('GET', `/payables/suppliers/${id}`),
  supplierPayments: (supplierId?: string) => request<Paged<LedgerDoc>>('GET', `/supplier-payments${qs({ supplierId, pageSize: 100 })}`),
  paySupplier: (b: unknown) => request<LedgerDoc>('POST', '/supplier-payments', b),
  cancelSupplierPayment: (id: string, reason: string) => request<LedgerDoc>('POST', `/supplier-payments/${id}/cancel`, { reason }),

  receivables: () => request<PartyBalance[]>('GET', '/receivables'),
  receivablesAging: () => request<Aging>('GET', '/receivables/aging'),
  customerStatement: (id: string) => request<Statement>('GET', `/receivables/customers/${id}`),
  customerReceipts: (customerId?: string) => request<Paged<LedgerDoc>>('GET', `/customer-receipts${qs({ customerId, pageSize: 100 })}`),
  receiveFromCustomer: (b: unknown) => request<LedgerDoc>('POST', '/customer-receipts', b),
  cancelCustomerReceipt: (id: string, reason: string) => request<LedgerDoc>('POST', `/customer-receipts/${id}/cancel`, { reason }),

  settings: () => request<Settings>('GET', '/settings'),
  updateSettings: (b: Settings) => request<Settings>('PUT', '/settings', b),

  currentShift: () => request<ShiftSummary | undefined>('GET', '/shifts/current'),
  openShift: (openingCash: number) => request<ShiftSummary>('POST', '/shifts/open', { openingCash }),
  cashMovement: (b: unknown) => request<ShiftSummary>('POST', '/shifts/current/movements', b),
  closeShift: (countedCash: number, notes: string) => request<ShiftSummary>('POST', '/shifts/current/close', { countedCash, notes }),
  shift: (id: string) => request<ShiftSummary>('GET', `/shifts/${id}`),
  shifts: (from?: string, to?: string) => request<ShiftListItem[]>('GET', `/shifts${qs({ from, to })}`),

  stockCounts: () => request<StockCountListItem[]>('GET', '/stock-counts'),
  stockCount: (id: string) => request<StockCount>('GET', `/stock-counts/${id}`),
  createStockCount: (b: unknown) => request<StockCount>('POST', '/stock-counts', b),
  saveCountEntries: (id: string, entries: { lineId: string; countedQty: number | null }[]) => request<StockCount>('PUT', `/stock-counts/${id}/entries`, entries),
  postStockCount: (id: string) => request<StockCount>('POST', `/stock-counts/${id}/post`, {}),
  cancelStockCount: (id: string) => request<void>('POST', `/stock-counts/${id}/cancel`, {}),

  searchBatches: (q: string) => request<BatchRow[]>('GET', `/batches${qs({ q })}`),
  traceBatch: (id: string) => request<BatchTrace>('GET', `/batches/${id}/trace`),
  blockBatch: (id: string, reason: string) => request<void>('POST', `/batches/${id}/block`, { reason }),
  unblockBatch: (id: string) => request<void>('POST', `/batches/${id}/unblock`, {}),

  customerLookup: (phone: string) => request<CustomerLookup | undefined>('GET', `/customers/lookup${qs({ phone })}`),
  customerProfile: (id: string) => request<CustomerProfile>('GET', `/customers/${id}/profile`),
  customerPurchases: (id: string, page = 1) => request<Paged<PurchaseHistoryItem>>('GET', `/customers/${id}/purchases${qs({ page })}`),
  recordConsent: (id: string, b: unknown) => request<ConsentState[]>('POST', `/customers/${id}/consents`, b),
  loyaltyAccount: (id: string) => request<LoyaltyAccount>('GET', `/customers/${id}/loyalty`),
  adjustPoints: (id: string, points: number, reason: string) => request<LoyaltyAccount>('POST', `/customers/${id}/loyalty/adjust`, { points, reason }),
  mergeCustomers: (sourceCustomerId: string, targetCustomerId: string) => request<CustomerProfile>('POST', '/customers/merge', { sourceCustomerId, targetCustomerId }),

  reminders: (p: { dueWithinDays?: number; status?: string; customerId?: string }) => request<Reminder[]>('GET', `/reminders${qs(p)}`),
  createReminder: (b: unknown) => request<Reminder>('POST', '/reminders', b),
  updateReminder: (id: string, b: unknown) => request<Reminder>('PUT', `/reminders/${id}`, b),
  notifyReminder: (id: string, channel: Channel) => request<PreparedMessage>('POST', `/reminders/${id}/notify`, { channel }),
  notifications: (customerId?: string) => request<Paged<NotificationRow>>('GET', `/notifications${qs({ customerId })}`),
  markSent: (id: string) => request<NotificationRow>('POST', `/notifications/${id}/sent`, {}),
  markFailed: (id: string, reason: string) => request<NotificationRow>('POST', `/notifications/${id}/failed`, { reason }),
  templates: () => request<Template[]>('GET', '/notification-templates'),
  saveTemplate: (b: unknown) => request<Template>('PUT', '/notification-templates', b),
  loyaltySettings: () => request<LoyaltySettings>('GET', '/loyalty/settings'),
  saveLoyaltySettings: (b: LoyaltySettings) => request<LoyaltySettings>('PUT', '/loyalty/settings', b),
  retention: (from: string, to: string) => request<RetentionReport>('GET', `/reports/customer-retention${qs({ from, to })}`),

  reorder: (lookbackDays: number, coverDays: number) => request<ReorderRow[]>('GET', `/purchases/reorder${qs({ lookbackDays, coverDays })}`),
  purchaseOrders: (status?: string) => request<Paged<PurchaseOrderListItem>>('GET', `/purchase-orders${qs({ status })}`),
  purchaseOrder: (id: string) => request<PurchaseOrder>('GET', `/purchase-orders/${id}`),
  createPurchaseOrder: (b: unknown) => request<PurchaseOrder>('POST', '/purchase-orders', b),
  poAction: (id: string, action: 'send' | 'close' | 'cancel') => request<PurchaseOrder>('POST', `/purchase-orders/${id}/${action}`, {}),
  importPreview: (b: unknown) => request<ImportPreview>('POST', '/purchases/import/preview', b),
  saveItemMap: (supplierId: string, items: unknown[]) => request<number>('POST', `/suppliers/${supplierId}/item-map`, items),

  accSync: () => request<{ entriesCreated: number; at: string }>('POST', '/accounting/sync', {}),
  accounts: () => request<LedgerAccount[]>('GET', '/accounting/accounts'),
  saveAccount: (id: string | null, b: unknown) => request<LedgerAccount>(id ? 'PUT' : 'POST', id ? `/accounting/accounts/${id}` : '/accounting/accounts', b),
  journals: (from: string, to: string, sourceType?: string) => request<Journal[]>('GET', `/accounting/journals${qs({ from, to, sourceType })}`),
  createJournal: (b: unknown) => request<Journal>('POST', '/accounting/journals', b),
  reverseJournal: (id: string) => request<Journal>('POST', `/accounting/journals/${id}/reverse`, {}),
  trialBalance: (asOf: string) => request<TrialBalance>('GET', `/accounting/trial-balance${qs({ asOf })}`),
  profitAndLoss: (from: string, to: string) => request<ProfitAndLoss>('GET', `/accounting/profit-and-loss${qs({ from, to })}`),
  balanceSheet: (asOf: string) => request<BalanceSheet>('GET', `/accounting/balance-sheet${qs({ asOf })}`),
  accountLedger: (id: string, from: string, to: string) => request<AccountLedger>('GET', `/accounting/ledger/${id}${qs({ from, to })}`),
  gstSummary: (from: string, to: string) => request<GstSummary>('GET', `/accounting/gst-summary${qs({ from, to })}`),
  controlChecks: () => request<ControlCheck[]>('GET', '/accounting/control-checks'),
  lockDate: () => request<{ lockedUntil?: string | null }>('GET', '/accounting/lock'),
  closeBooks: (upTo: string) => request<{ lockedUntil?: string | null }>('POST', '/accounting/close', { upTo }),
  reopenBooks: (upTo: string | null) => request<{ lockedUntil?: string | null }>('POST', '/accounting/reopen', upTo ? { upTo } : {}),
  downloadTallyXml: async (from: string, to: string) => {
    const res = await fetch(`/api/v1/accounting/export.tally.xml${qs({ from, to })}`, { headers: { Authorization: `Bearer ${tokenStore.get() ?? ''}` } });
    if (!res.ok) throw new ApiError(res.status, 'export_failed', 'Export failed.');
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement('a'); a.href = url; a.download = `tally_${from}_${to}.xml`; a.click();
    URL.revokeObjectURL(url);
  },
  downloadJournalCsv: async (from: string, to: string) => {
    const res = await fetch(`/api/v1/accounting/export.csv${qs({ from, to })}`, { headers: { Authorization: `Bearer ${tokenStore.get() ?? ''}` } });
    if (!res.ok) throw new ApiError(res.status, 'export_failed', 'Export failed.');
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement('a');
    a.href = url; a.download = `journal_${from}_${to}.csv`; a.click();
    URL.revokeObjectURL(url);
  },

  switchBranch: (branchId: string) => request<AuthResponse>('POST', '/auth/switch-branch', { branchId }),
  branches: () => request<Branch[]>('GET', '/branches'),
  branchDirectory: () => request<Branch[]>('GET', '/branches/directory'),
  saveBranch: (id: string | null, b: unknown) => request<Branch>(id ? 'PUT' : 'POST', id ? `/branches/${id}` : '/branches', b),
  assignUserBranch: (userId: string, branchId: string) => request<void>('PUT', `/users/${userId}/branch`, { branchId }),
  transfers: (status?: string) => request<TransferListItem[]>('GET', `/transfers${qs({ status })}`),
  transfer: (id: string) => request<Transfer>('GET', `/transfers/${id}`),
  createTransfer: (b: unknown) => request<Transfer>('POST', '/transfers', b),
  dispatchTransfer: (id: string) => request<Transfer>('POST', `/transfers/${id}/dispatch`, {}),
  receiveTransfer: (id: string, b: unknown) => request<Transfer>('POST', `/transfers/${id}/receive`, b),
  cancelTransfer: (id: string) => request<void>('POST', `/transfers/${id}/cancel`, {}),
  indents: () => request<Indent[]>('GET', '/indents'),
  createIndent: (b: unknown) => request<Indent>('POST', '/indents', b),
  fulfilIndent: (id: string) => request<Transfer>('POST', `/indents/${id}/fulfil`, {}),
  rejectIndent: (id: string, reason: string) => request<Indent>('POST', `/indents/${id}/reject`, { reason }),
  branchComparison: (from: string, to: string) => request<{ from: string; to: string; rows: BranchComparisonRow[]; total: BranchComparisonRow }>('GET', `/reports/branch-comparison${qs({ from, to })}`),

  subscription: () => request<{ current: Entitlements; plans: PlanDef[] }>('GET', '/subscription'),
  setSupportAccess: (hours: number) => request<{ supportAccessUntil?: string | null }>('PUT', '/settings/support-access', { hours }),
  tickets: () => request<Ticket[]>('GET', '/support/tickets'),
  createTicket: (b: unknown) => request<Ticket>('POST', '/support/tickets', b),
  replyTicket: (id: string, body: string) => request<Ticket>('POST', `/support/tickets/${id}/reply`, { body }),
  downloadExport: async () => {
    const res = await fetch('/api/v1/export/all.zip', { headers: { Authorization: `Bearer ${tokenStore.get() ?? ''}` } });
    if (!res.ok) throw new ApiError(res.status, 'export_failed', 'Export failed.');
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement('a'); a.href = url; a.download = `pharmaos_export_${new Date().toISOString().slice(0, 10)}.zip`; a.click();
    URL.revokeObjectURL(url);
  },

  onlineOrders: (status?: string) => request<OnlineOrder[]>('GET', `/online/orders${qs({ status })}`),
  onlineOrder: (id: string) => request<OnlineOrder>('GET', `/online/orders/${id}`),
  reviewOrder: (id: string, b: unknown) => request<OnlineOrder>('POST', `/online/orders/${id}/review`, b),
  packOrder: (id: string) => request<OnlineOrder>('POST', `/online/orders/${id}/pack`, {}),
  assignOrder: (id: string, agentUserId: string) => request<OnlineOrder>('POST', `/online/orders/${id}/assign`, { agentUserId }),
  completeOrder: (id: string, b: unknown) => request<OnlineOrder>('POST', `/online/orders/${id}/complete`, b),
  failOrder: (id: string, reason: string) => request<OnlineOrder>('POST', `/online/orders/${id}/fail`, { reason }),
  cancelOrder: (id: string, reason: string) => request<OnlineOrder>('POST', `/online/orders/${id}/cancel`, { reason }),
  deliveryAgents: () => request<User[]>('GET', '/online/agents'),
  myDeliveries: () => request<OnlineOrder[]>('GET', '/online/deliveries'),
  onlineCollections: (date: string) => request<{ orderNo: string; agentName?: string | null; mode: string; amount: number; at: string }[]>('GET', `/online/collections${qs({ date })}`),
  saveOnlineSettings: (branchId: string, b: unknown) => request<void>('PUT', `/online/settings/${branchId}`, b),
  openOrderFile: async (orderId: string, fileId: string) => {
    const res = await fetch(`/api/v1/online/orders/${orderId}/files/${fileId}`, { headers: { Authorization: `Bearer ${tokenStore.get() ?? ''}` } });
    if (!res.ok) throw new ApiError(res.status, 'file', 'Could not open file.');
    window.open(URL.createObjectURL(await res.blob()), '_blank', 'noopener');
  },

  nearExpiry: (days: number) => request<NearExpiryRow[]>('GET', `/reports/near-expiry${qs({ days })}`),
  dailySales: (date: string) => request<DailySales>('GET', `/reports/daily-sales${qs({ date })}`),
};

// ---------- Distributor (B2B) ----------
export type TradeStatus = 'Posted' | 'Cancelled';
export type OrderState = 'Open' | 'Invoiced' | 'Cancelled';
export type TradeReturnReason = 'Saleable' | 'Expired' | 'Damaged' | 'Other';
export interface Retailer {
  id: string; name: string; gstin?: string | null; stateCode: string; drugLicenseNo?: string | null; drugLicenseValidTo?: string | null; licenceExpired: boolean;
  phone?: string | null; address?: string | null; route?: string | null; creditLimit: number; creditDays: number; salesmanUserId?: string | null;
  salesmanName?: string | null; isActive: boolean; version: number; outstanding: number; overdue: number;
}
export interface RetailerInput {
  name: string; gstin?: string | null; stateCode: string; drugLicenseNo?: string | null; drugLicenseValidTo?: string | null; phone?: string | null;
  address?: string | null; route?: string | null; creditLimit: number; creditDays: number; salesmanUserId?: string | null; isActive: boolean; version?: number | null;
}
export interface TradeRateRow { productId: string; productName: string; packSize: number; gstRate: number; defaultRate?: number | null; retailerRate?: number | null }
export interface TradeScheme {
  id: string; productId: string; productName: string; name: string; buyQtyPacks: number; freeQtyPacks: number; discountPercent: number;
  validFrom: string; validTo: string; isActive: boolean;
}
export interface SalesOrder {
  id: string; orderNo: string; retailerId: string; retailerName: string; status: OrderState; notes?: string | null; takenBy?: string | null;
  createdAt: string; tradeInvoiceId?: string | null; lines: { productId: string; productName: string; qtyPacks: number; availablePacks: number }[];
}
export interface TradeLine {
  id: string; lineNo: number; productId: string; productName: string; hsnCode?: string | null; batchNo: string; expiryDate: string; qtyPacks: number;
  freeQtyPacks: number; ratePerPack: number; discountPercent: number; taxableAmount: number; gstRate: number; taxAmount: number; lineTotal: number; returnedPacks: number;
}
export interface TradeInvoice {
  id: string; invoiceNo: string; invoiceDate: string; dueDate: string; status: TradeStatus; retailerId: string; retailerName: string;
  retailerGstin?: string | null; retailerLicence?: string | null; interState: boolean; grossAmount: number; discountAmount: number; taxableAmount: number;
  taxAmount: number; roundOff: number; netAmount: number; outstanding: number; cancelReason?: string | null; lines: TradeLine[]; seller: Sale['header'];
}
export interface TradeInvoiceListItem { id: string; invoiceNo: string; invoiceDate: string; dueDate: string; retailerName: string; status: TradeStatus; netAmount: number; outstanding: number }
export interface CreditNote { id: string; creditNoteNo: string; date: string; invoiceNo: string; reason: TradeReturnReason; taxableAmount: number; taxAmount: number; amount: number }
export interface TradeReceipt {
  id: string; receiptNo: string; date: string; retailerId: string; retailerName: string; mode: SettlementMode; amount: number; allocated: number;
  reference?: string | null; status: TradeStatus; collectedBy?: string | null;
}
export interface RetailerStatement {
  retailerId: string; name: string; outstanding: number; overdue: number; creditLimit: number; availableCredit: number;
  openInvoices: OpenDocument[]; lines: Statement['lines'];
}
export interface TradeAging { buckets: AgingBucket[]; total: number; topOverdue: Retailer[] }
export interface SalesmanRow { userId?: string | null; name: string; invoices: number; sales: number; collections: number; orders: number }
export interface TradeInvoiceRequest { clientRequestId: string; retailerId: string; lines: { productId: string; qtyPacks: number; discountPercent?: number }[]; salesOrderId?: string | null; creditOverrideReason?: string | null }

export const tradeApi = {
  retailers: (q = '') => request<Retailer[]>('GET', `/distribution/retailers${qs({ q })}`),
  saveRetailer: (id: string | null, b: RetailerInput) => request<Retailer>(id ? 'PUT' : 'POST', `/distribution/retailers${id ? `/${id}` : ''}`, b),
  statement: (id: string) => request<RetailerStatement>('GET', `/distribution/retailers/${id}/statement`),
  rates: (q = '', retailerId?: string | null) => request<TradeRateRow[]>('GET', `/distribution/rates${qs({ q, retailerId })}`),
  setRate: (productId: string, retailerId: string | null, ratePerPack: number | null) => request<void>('PUT', '/distribution/rates', { productId, retailerId, ratePerPack }),
  schemes: () => request<TradeScheme[]>('GET', '/distribution/schemes'),
  saveScheme: (id: string | null, b: Omit<TradeScheme, 'id' | 'productName'>) => request<TradeScheme>(id ? 'PUT' : 'POST', `/distribution/schemes${id ? `/${id}` : ''}`, b),
  orders: (status?: string) => request<SalesOrder[]>('GET', `/distribution/orders${qs({ status })}`),
  order: (id: string) => request<SalesOrder>('GET', `/distribution/orders/${id}`),
  createOrder: (b: { retailerId: string; notes?: string | null; lines: { productId: string; qtyPacks: number }[] }) => request<SalesOrder>('POST', '/distribution/orders', b),
  cancelOrder: (id: string) => request<void>('POST', `/distribution/orders/${id}/cancel`, {}),
  invoiceOrder: (id: string, creditOverrideReason?: string | null) => request<TradeInvoice>('POST', `/distribution/orders/${id}/invoice`, { creditOverrideReason }),
  invoices: (retailerId?: string) => request<TradeInvoiceListItem[]>('GET', `/distribution/invoices${qs({ retailerId })}`),
  invoice: (id: string) => request<TradeInvoice>('GET', `/distribution/invoices/${id}`),
  createInvoice: (b: TradeInvoiceRequest) => request<TradeInvoice>('POST', '/distribution/invoices', b),
  cancelInvoice: (id: string, reason: string) => request<TradeInvoice>('POST', `/distribution/invoices/${id}/cancel`, { reason }),
  createCreditNote: (b: { clientRequestId: string; tradeInvoiceId: string; reason: TradeReturnReason; notes?: string | null; lines: { tradeInvoiceLineId: string; qtyPacks: number }[] }) =>
    request<CreditNote>('POST', '/distribution/credit-notes', b),
  receipts: (retailerId?: string) => request<TradeReceipt[]>('GET', `/distribution/receipts${qs({ retailerId })}`),
  createReceipt: (b: { retailerId: string; mode: SettlementMode; amount: number; reference?: string | null }) => request<TradeReceipt>('POST', '/distribution/receipts', b),
  cancelReceipt: (id: string, reason: string) => request<TradeReceipt>('POST', `/distribution/receipts/${id}/cancel`, { reason }),
  aging: () => request<TradeAging>('GET', '/distribution/reports/aging'),
  salesmen: (from: string, to: string) => request<SalesmanRow[]>('GET', `/distribution/reports/salesmen${qs({ from, to })}`),
};

// ---------- Field force (MR) ----------
export type SfaStatus = 'Draft' | 'Submitted' | 'Approved' | 'Rejected';
export type DoctorClass = 'A' | 'B' | 'C';
export interface Territory { id: string; name: string; repUserId?: string | null; repName?: string | null; managerUserId?: string | null; managerName?: string | null; isActive: boolean; doctors: number }
export interface TeamMember { userId: string; name: string; role: Role; territories: string[] }
export interface Doctor {
  id: string; name: string; speciality?: string | null; qualification?: string | null; clinic?: string | null; area?: string | null; phone?: string | null;
  territoryId: string; territoryName: string; class: DoctorClass; consentRecordedOn?: string | null; isActive: boolean; anonymised: boolean; version: number; lastVisit?: string | null;
}
export interface DoctorInput {
  name: string; speciality?: string | null; qualification?: string | null; clinic?: string | null; area?: string | null; phone?: string | null;
  territoryId: string; class: DoctorClass; consentRecordedOn?: string | null; isActive: boolean; version?: number | null;
}
export interface TourEntry { date: string; doctorId: string; doctorName: string; class: DoctorClass }
export interface TourPlan { id: string; repUserId: string; repName: string; month: string; status: SfaStatus; reviewComment?: string | null; reviewedBy?: string | null; version: number; entries: TourEntry[] }
export interface DoctorCall {
  id: string; doctorId: string; doctorName: string; class: DoctorClass; callAt: string; productsDetailed?: string | null; feedback?: string | null;
  jointWorkWith?: string | null; hasLocation: boolean; wasPlanned: boolean; samples: { productId: string; productName: string; qty: number }[];
}
export interface DailyReport {
  id: string; repUserId: string; repName: string; date: string; status: SfaStatus; notes?: string | null; reviewComment?: string | null; reviewedBy?: string | null;
  version: number; calls: DoctorCall[]; planned: TourEntry[];
}
export interface CallInput {
  clientRequestId: string; doctorId: string; productsDetailed?: string | null; feedback?: string | null; jointWorkUserId?: string | null;
  latitude?: number | null; longitude?: number | null; samples: { productId: string; qty: number }[];
}
export interface ReportListItem { id: string; repUserId: string; repName: string; date: string; status: SfaStatus; calls: number; submittedAt?: string | null }
export interface SampleBalance { repUserId: string; repName: string; productId: string; productName: string; issued: number; given: number; returned: number; balance: number }
export interface SampleLedgerRow { at: string; repName: string; productName: string; kind: string; qty: number; doctorName?: string | null; note?: string | null }
export interface CoverageRow {
  repUserId: string; repName: string; doctors: number; requiredVisits: number; visits: number; doctorsMet: number; coveragePercent: number;
  planned: number; plannedMet: number; reportsPending: number; missed: { doctorId: string; name: string; class: DoctorClass; required: number; visits: number }[];
}

export const sfaApi = {
  team: () => request<TeamMember[]>('GET', '/sfa/team'),
  territories: () => request<Territory[]>('GET', '/sfa/territories'),
  saveTerritory: (id: string | null, b: { name: string; repUserId?: string | null; managerUserId?: string | null; isActive: boolean }) =>
    request<Territory>(id ? 'PUT' : 'POST', `/sfa/territories${id ? `/${id}` : ''}`, b),
  doctors: (q = '', territoryId?: string, includeInactive = false) => request<Doctor[]>('GET', `/sfa/doctors${qs({ q, territoryId, includeInactive })}`),
  saveDoctor: (id: string | null, b: DoctorInput) => request<Doctor>(id ? 'PUT' : 'POST', `/sfa/doctors${id ? `/${id}` : ''}`, b),
  anonymiseDoctor: (id: string) => request<void>('POST', `/sfa/doctors/${id}/anonymise`, {}),
  myTourPlan: (month: string) => request<TourPlan>('GET', `/sfa/tour-plans/mine${qs({ month })}`),
  tourPlans: (month: string) => request<TourPlan[]>('GET', `/sfa/tour-plans${qs({ month })}`),
  saveTourPlan: (id: string, version: number, entries: { date: string; doctorId: string }[]) => request<TourPlan>('PUT', `/sfa/tour-plans/${id}`, { version, entries }),
  submitTourPlan: (id: string) => request<TourPlan>('POST', `/sfa/tour-plans/${id}/submit`, {}),
  reviewTourPlan: (id: string, approve: boolean, comment?: string) => request<TourPlan>('POST', `/sfa/tour-plans/${id}/${approve ? 'approve' : 'reject'}`, { comment }),
  openDay: (date: string) => request<DailyReport>('POST', '/sfa/reports/open', { date }),
  report: (id: string) => request<DailyReport>('GET', `/sfa/reports/${id}`),
  reports: (from: string, to: string, status?: string, repUserId?: string) => request<ReportListItem[]>('GET', `/sfa/reports${qs({ from, to, status, repUserId })}`),
  addCall: (reportId: string, b: CallInput) => request<DailyReport>('POST', `/sfa/reports/${reportId}/calls`, b),
  removeCall: (reportId: string, callId: string) => request<DailyReport>('DELETE', `/sfa/reports/${reportId}/calls/${callId}`),
  submitReport: (id: string, version: number, notes?: string | null) => request<DailyReport>('POST', `/sfa/reports/${id}/submit`, { version, notes }),
  reviewReport: (id: string, approve: boolean, comment?: string) => request<DailyReport>('POST', `/sfa/reports/${id}/${approve ? 'approve' : 'reject'}`, { comment }),
  samples: (repUserId?: string) => request<SampleBalance[]>('GET', `/sfa/samples${qs({ repUserId })}`),
  sampleLedger: (repUserId: string) => request<SampleLedgerRow[]>('GET', `/sfa/samples/ledger${qs({ repUserId })}`),
  moveSamples: (kind: 'issue' | 'return', b: { repUserId: string; productId: string; qty: number; note?: string | null }) => request<void>('POST', `/sfa/samples/${kind}`, b),
  coverage: (month: string) => request<CoverageRow[]>('GET', `/sfa/coverage${qs({ month })}`),
};

// ---------- Hospital pharmacy ----------
export type IndentStatus = 'Open' | 'PartlyIssued' | 'Closed' | 'Cancelled';
export type WardIssueStatus = 'PendingCheck' | 'Issued' | 'Cancelled';
export type WardReturnReason = 'Unused' | 'Expired' | 'Damaged';
export interface Department { id: string; name: string; code: string; isActive: boolean; staff: number }
export interface FormularyRow { productId: string; productName: string; composition?: string | null; schedule: Schedule; baseUnit: string; isHighAlert: boolean; needsSecondCheck: boolean; notes?: string | null; stockUnits: number }
export interface WardIndent {
  id: string; indentNo: string; departmentId: string; departmentName: string; status: IndentStatus; priority: 'Routine' | 'Urgent'; encounterRef?: string | null;
  notes?: string | null; requestedBy?: string | null; createdAt: string; firstIssuedAt?: string | null; cancelReason?: string | null; version: number;
  lines: { id: string; productId: string; productName: string; baseUnit: string; qtyUnits: number; issuedUnits: number; pendingUnits: number; stockUnits: number }[];
}
export interface WardIssue {
  id: string; issueNo: string; departmentId: string; departmentName: string; indentNo?: string | null; status: WardIssueStatus; needsSecondCheck: boolean;
  preparedBy: string; preparedAt: string; checkedBy?: string | null; issuedAt?: string | null; receivedBy?: string | null; costValue: number; cancelReason?: string | null; version: number;
  lines: { id: string; productId: string; productName: string; baseUnit: string; qtyUnits: number; highAlert: boolean;
    batches: { id: string; batchNo: string; expiryDate: string; qtyUnits: number; returnedUnits: number; costPerUnit: number }[] }[];
}
export interface WardReturnRow { id: string; returnNo: string; issueNo: string; departmentName: string; reason: WardReturnReason; costValue: number; createdAt: string }
export interface ConsumptionReport {
  departments: { departmentId: string; departmentName: string; issues: number; issuedValue: number; returnedValue: number; netValue: number; openIndents: number; avgHoursToFirstIssue?: number | null }[];
  topProducts: { productId: string; productName: string; baseUnit: string; units: number; value: number }[];
  totalNet: number;
}

export const hospitalApi = {
  departments: () => request<Department[]>('GET', '/hospital/departments'),
  saveDepartment: (id: string | null, b: { name: string; code: string; isActive: boolean }) => request<Department>(id ? 'PUT' : 'POST', `/hospital/departments${id ? `/${id}` : ''}`, b),
  setStaffDepartment: (userId: string, departmentId: string | null) => request<void>('PUT', `/hospital/staff/${userId}`, { departmentId }),
  formulary: (q = '') => request<FormularyRow[]>('GET', `/hospital/formulary${qs({ q })}`),
  setFormulary: (b: { productId: string; inFormulary: boolean; isHighAlert: boolean; notes?: string | null }) => request<void>('PUT', '/hospital/formulary', b),
  indents: (status?: string) => request<WardIndent[]>('GET', `/hospital/indents${qs({ status })}`),
  indent: (id: string) => request<WardIndent>('GET', `/hospital/indents/${id}`),
  createIndent: (b: { departmentId: string; priority: string; encounterRef?: string | null; notes?: string | null; lines: { productId: string; qtyUnits: number }[] }) =>
    request<WardIndent>('POST', '/hospital/indents', b),
  cancelIndent: (id: string, reason: string) => request<WardIndent>('POST', `/hospital/indents/${id}/cancel`, { reason }),
  issues: (status?: string) => request<WardIssue[]>('GET', `/hospital/issues${qs({ status })}`),
  issue: (id: string) => request<WardIssue>('GET', `/hospital/issues/${id}`),
  prepareIssue: (b: { departmentId: string; wardIndentId?: string | null; receivedBy?: string | null; lines: { productId: string; qtyUnits: number; wardIndentLineId?: string | null }[] }) =>
    request<WardIssue>('POST', '/hospital/issues', b),
  verifyIssue: (id: string, version: number) => request<WardIssue>('POST', `/hospital/issues/${id}/verify`, { version }),
  cancelIssue: (id: string, reason: string) => request<WardIssue>('POST', `/hospital/issues/${id}/cancel`, { reason }),
  returns: () => request<WardReturnRow[]>('GET', '/hospital/returns'),
  createReturn: (b: { wardIssueId: string; reason: WardReturnReason; notes?: string | null; lines: { wardIssueBatchId: string; qtyUnits: number }[] }) =>
    request<WardReturnRow>('POST', '/hospital/returns', b),
  consumption: (from: string, to: string) => request<ConsumptionReport>('GET', `/hospital/reports/consumption${qs({ from, to })}`),
};

// ---------- Manufacturing & QA (pilot, not GMP-validated) ----------
export type MfgDocStatus = 'Draft' | 'Approved' | 'Retired';
export type ProductionStatus = 'Planned' | 'InProcess' | 'Quarantine' | 'Released' | 'Rejected' | 'Cancelled';
export interface Recipe { id: string; productId: string; productName: string; versionNo: number; batchSizeUnits: number; status: MfgDocStatus; notes?: string | null; approvedBy?: string | null; approvedAt?: string | null; lines: { materialProductId: string; materialName: string; baseUnit: string; qtyUnits: number }[] }
export interface SpecTest { id: string; seq: number; name: string; unit?: string | null; min?: number | null; max?: number | null; requirement?: string | null }
export interface QcSpec { id: string; productId: string; productName: string; versionNo: number; status: MfgDocStatus; approvedBy?: string | null; tests: SpecTest[] }
export interface QcSample {
  id: string; sampleNo: string; batchId: string; productName: string; batchNo: string; specVersion: number; status: 'Pending' | 'Passed' | 'Failed';
  testerSignature?: string | null; testedAt?: string | null; createdAt: string; tests: SpecTest[];
  results: { testId: string; testName: string; unit?: string | null; min?: number | null; max?: number | null; requirement?: string | null; value?: number | null; text?: string | null; pass: boolean }[];
}
export interface ProductionBatch {
  id: string; batchNo: string; productId: string; productName: string; recipeVersion: number; mfgDate: string; expiryDate: string; mrp: number; plannedUnits: number;
  status: ProductionStatus; materialCost: number; outputUnits: number; yieldPercent?: number | null; outputBatchId?: string | null; createdBy?: string | null; createdAt: string;
  decisionSignature?: string | null; decisionAt?: string | null; decisionNote?: string | null; version: number;
  materials: { materialProductId: string; materialName: string; batchId: string; batchNo: string; expiryDate: string; qtyUnits: number; costPerUnit: number }[];
  samples: QcSample[];
}
export interface LotNode { batchId: string; productName: string; batchNo: string; expiryDate: string; qtyUnits: number; productionStatus?: string | null; productionBatchId?: string | null }
export interface Genealogy { batchId: string; productName: string; batchNo: string; isBlocked: boolean; blockReason?: string | null; madeFrom: LotNode[]; usedIn: LotNode[]; samples: QcSample[] }

export const mfgApi = {
  recipes: () => request<Recipe[]>('GET', '/mfg/recipes'),
  createRecipe: (b: { productId: string; batchSizeUnits: number; notes?: string | null; lines: { materialProductId: string; qtyUnits: number }[] }) => request<Recipe>('POST', '/mfg/recipes', b),
  approveRecipe: (id: string, password: string) => request<Recipe>('POST', `/mfg/recipes/${id}/approve`, { password }),
  specs: () => request<QcSpec[]>('GET', '/mfg/specs'),
  createSpec: (b: { productId: string; tests: { name: string; unit?: string | null; min?: number | null; max?: number | null; requirement?: string | null }[] }) => request<QcSpec>('POST', '/mfg/specs', b),
  approveSpec: (id: string, password: string) => request<QcSpec>('POST', `/mfg/specs/${id}/approve`, { password }),
  samples: (status?: string) => request<QcSample[]>('GET', `/mfg/samples${qs({ status })}`),
  createSample: (batchId: string) => request<QcSample>('POST', '/mfg/samples', { batchId }),
  recordResults: (id: string, password: string, results: { qcSpecTestId: string; value?: number | null; text?: string | null; pass?: boolean | null }[]) =>
    request<QcSample>('POST', `/mfg/samples/${id}/results`, { password, results }),
  batches: (status?: string) => request<ProductionBatch[]>('GET', `/mfg/batches${qs({ status })}`),
  batch: (id: string) => request<ProductionBatch>('GET', `/mfg/batches/${id}`),
  planBatch: (b: { recipeId: string; batchNo: string; expiryDate: string; mrp: number; plannedUnits: number }) => request<ProductionBatch>('POST', '/mfg/batches', b),
  issueMaterials: (id: string) => request<ProductionBatch>('POST', `/mfg/batches/${id}/issue-materials`, {}),
  recordOutput: (id: string, outputUnits: number) => request<ProductionBatch>('POST', `/mfg/batches/${id}/output`, { outputUnits }),
  cancelBatch: (id: string) => request<ProductionBatch>('POST', `/mfg/batches/${id}/cancel`, {}),
  decide: (id: string, release: boolean, password: string, note?: string | null) => request<ProductionBatch>('POST', `/mfg/batches/${id}/${release ? 'release' : 'reject'}`, { password, note }),
  lots: (q: string) => request<LotNode[]>('GET', `/mfg/lots${qs({ q })}`),
  genealogy: (batchId: string) => request<Genealogy>('GET', `/mfg/genealogy/${batchId}`),
};

// ---------- Follow-ups: dispatch, MR expenses/targets, controlled-drug register, deviations ----------
export interface PendingDispatchInvoice { id: string; invoiceNo: string; invoiceDate: string; retailerName: string; route?: string | null; netAmount: number; previouslyFailed: boolean }
export interface DispatchStop { id: string; seq: number; tradeInvoiceId: string; invoiceNo: string; retailerName: string; address?: string | null; phone?: string | null; netAmount: number; status: 'Pending' | 'Delivered' | 'Failed'; receivedBy?: string | null; note?: string | null; at?: string | null }
export interface DispatchTrip { id: string; dispatchNo: string; date: string; route?: string | null; vehicle?: string | null; driverName?: string | null; status: 'Packing' | 'OnRoute' | 'Completed' | 'Cancelled'; createdAt: string; departedAt?: string | null; completedAt?: string | null; version: number; value: number; stops: DispatchStop[] }
export interface PickLine { productId: string; productName: string; batchNo: string; expiryDate: string; packs: number; freePacks: number; invoices: string[] }
export const dispatchApi = {
  pending: () => request<PendingDispatchInvoice[]>('GET', '/distribution/dispatch/pending'),
  list: (status?: string) => request<DispatchTrip[]>('GET', `/distribution/dispatches${qs({ status })}`),
  get: (id: string) => request<DispatchTrip>('GET', `/distribution/dispatches/${id}`),
  pickList: (id: string) => request<PickLine[]>('GET', `/distribution/dispatches/${id}/pick-list`),
  create: (b: { route?: string | null; vehicle?: string | null; driverName?: string | null; invoiceIds: string[] }) => request<DispatchTrip>('POST', '/distribution/dispatches', b),
  depart: (id: string) => request<DispatchTrip>('POST', `/distribution/dispatches/${id}/depart`, {}),
  cancel: (id: string) => request<DispatchTrip>('POST', `/distribution/dispatches/${id}/cancel`, {}),
  confirm: (id: string, stopId: string, b: { delivered: boolean; receivedBy?: string | null; note?: string | null }) => request<DispatchTrip>('POST', `/distribution/dispatches/${id}/stops/${stopId}`, b),
};

export type ExpenseCategory = 'Travel' | 'Food' | 'Lodging' | 'Other';
export interface ExpenseClaim { id: string; repUserId: string; repName: string; date: string; category: ExpenseCategory; amount: number; km?: number | null; note?: string | null; status: SfaStatus; reviewedBy?: string | null; reviewComment?: string | null }
export interface ScorecardRow { repUserId: string; repName: string; callsTarget: number; calls: number; coverageTargetPercent: number; coveragePercent: number; expensesApproved: number; expensesPending: number; callsMet: boolean; coverageMet: boolean }
export const sfaExtraApi = {
  expenses: (month: string, status?: string) => request<ExpenseClaim[]>('GET', `/sfa/expenses${qs({ month, status })}`),
  claim: (b: { date: string; category: ExpenseCategory; amount: number; km?: number | null; note?: string | null }) => request<ExpenseClaim>('POST', '/sfa/expenses', b),
  review: (id: string, approve: boolean, comment?: string) => request<ExpenseClaim>('POST', `/sfa/expenses/${id}/${approve ? 'approve' : 'reject'}`, { comment }),
  setTarget: (b: { repUserId: string; month: string; callsTarget: number; coverageTargetPercent: number }) => request<void>('PUT', '/sfa/targets', b),
  scorecard: (month: string) => request<ScorecardRow[]>('GET', `/sfa/scorecard${qs({ month })}`),
};

export interface RegisterBatch {
  productId: string; productName: string; schedule: Schedule; baseUnit: string; batchId: string; batchNo: string; expiryDate: string; opening: number; received: number; issued: number; closing: number;
  entries: { at: string; type: string; documentNo?: string | null; party?: string | null; prescriber?: string | null; in: number; out: number; balance: number }[];
}
export const controlledDrugRegister = (from: string, to: string) => request<RegisterBatch[]>('GET', `/reports/controlled-drugs${qs({ from, to })}`);

export type DeviationSeverity = 'Minor' | 'Major' | 'Critical';
export interface Deviation {
  id: string; deviationNo: string; productionBatchId?: string | null; batchNo?: string | null; title: string; description: string; severity: DeviationSeverity;
  status: 'Open' | 'CapaInProgress' | 'Closed'; rootCause?: string | null; correctiveAction?: string | null; preventiveAction?: string | null; capaOwner?: string | null;
  capaDueDate?: string | null; overdue: boolean; raisedBy: string; raisedAt: string; closureSignature?: string | null; closedAt?: string | null; version: number;
}
export const deviationApi = {
  list: (status?: string) => request<Deviation[]>('GET', `/mfg/deviations${qs({ status })}`),
  raise: (b: { productionBatchId?: string | null; title: string; description: string; severity: DeviationSeverity }) => request<Deviation>('POST', '/mfg/deviations', b),
  investigate: (id: string, b: { rootCause: string; correctiveAction: string; preventiveAction?: string | null; capaOwnerUserId?: string | null; capaDueDate?: string | null; version: number }) =>
    request<Deviation>('POST', `/mfg/deviations/${id}/investigation`, b),
  close: (id: string, password: string, note?: string | null) => request<Deviation>('POST', `/mfg/deviations/${id}/close`, { password, note }),
};
