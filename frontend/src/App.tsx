import { useEffect, useState, type ReactNode } from 'react';
import { Navigate, NavLink, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { api, type Entitlements, type Role } from './api';
import { RoleSets, useAuth } from './auth';
import { ToastHost } from './ui';
import { LoginPage, RegisterPage } from './pages/AuthPages';
import PosPage from './pages/PosPage';
import SalesPage from './pages/SalesPage';
import PurchasesPage from './pages/PurchasesPage';
import PurchaseEditPage from './pages/PurchaseEditPage';
import ProductsPage from './pages/ProductsPage';
import SuppliersPage from './pages/SuppliersPage';
import StockPage from './pages/StockPage';
import { DailySalesPage, NearExpiryPage, RecoveryPage, RetentionPage } from './pages/ReportPages';
import UsersPage from './pages/UsersPage';
import SupplierReturnsPage from './pages/SupplierReturnsPage';
import { PayablesPage, ReceivablesPage } from './pages/LedgerPages';
import CustomersPage from './pages/CustomersPage';
import ShiftPage from './pages/ShiftPage';
import StockCountPage from './pages/StockCountPage';
import RecallPage from './pages/RecallPage';
import SettingsPage from './pages/SettingsPage';
import RemindersPage from './pages/RemindersPage';
import { PurchaseOrdersPage, ReorderPage } from './pages/PurchaseOrdersPage';
import AccountingPage from './pages/AccountingPage';
import { BranchComparisonPage, BranchesPage, BranchSwitcher, IndentsPage, TransfersPage } from './pages/MultiStorePages';
import SupportPage from './pages/SupportPage';
import PlatformApp from './pages/PlatformApp';
import AccountPage from './pages/AccountPage';
import PricesPage from './pages/PricesPage';
import { ShopPage, TrackPage } from './pages/ShopPages';
import OnlineOrdersPage, { DeliveriesPage } from './pages/OnlineOrdersPage';

interface NavItem { to: string; label: string; roles: Role[]; key?: string; group: string; module?: string }
const NAV: NavItem[] = [
  { to: '/pos', label: 'Billing', roles: RoleSets.sell, key: 'F1', group: 'Counter' },
  { to: '/sales', label: 'Sales & Returns', roles: RoleSets.sell, group: 'Counter' },
  { to: '/receivables', label: 'Customer Dues', roles: RoleSets.sell, group: 'Counter' },
  { to: '/shift', label: 'Cash Drawer', roles: RoleSets.sell, group: 'Counter' },
  { to: '/stock-counts', label: 'Stock Count', roles: RoleSets.salesAdmin, group: 'Inventory' },
  { to: '/recall', label: 'Batch Recall', roles: RoleSets.salesAdmin, group: 'Inventory' },
  { to: '/reorder', label: 'Reorder', roles: RoleSets.purchase, group: 'Inventory', module: 'purchasing' },
  { to: '/purchase-orders', label: 'Purchase Orders', roles: RoleSets.purchase, group: 'Inventory', module: 'purchasing' },
  { to: '/purchases', label: 'Purchases', roles: RoleSets.purchase, group: 'Inventory' },
  { to: '/supplier-returns', label: 'Returns to Supplier', roles: RoleSets.purchase, group: 'Inventory' },
  { to: '/transfers', label: 'Transfers', roles: RoleSets.salesAdmin, group: 'Inventory', module: 'multistore' },
  { to: '/indents', label: 'Indents', roles: RoleSets.purchase, group: 'Inventory', module: 'multistore' },
  { to: '/stock', label: 'Stock', roles: [...RoleSets.sell, 'PurchaseClerk'], group: 'Inventory' },
  { to: '/payables', label: 'Payables', roles: RoleSets.purchase, group: 'Money' },
  { to: '/accounting', label: 'Accounts', roles: RoleSets.accounting, group: 'Money', module: 'accounting' },
  { to: '/products', label: 'Products', roles: [...RoleSets.sell, 'PurchaseClerk'], group: 'Masters' },
  { to: '/suppliers', label: 'Suppliers', roles: RoleSets.purchase, group: 'Masters' },
  { to: '/prices', label: 'Branch Prices', roles: RoleSets.sell, group: 'Masters' },
  { to: '/online-orders', label: 'Online Orders', roles: RoleSets.sell, group: 'Customers', module: 'online' },
  { to: '/deliveries', label: 'My Deliveries', roles: ['DeliveryAgent'], group: 'Counter', module: 'online' },
  { to: '/customers', label: 'Customers', roles: RoleSets.sell, group: 'Customers' },
  { to: '/reminders', label: 'Refill Reminders', roles: RoleSets.sell, group: 'Customers', module: 'crm' },
  { to: '/reports/retention', label: 'Customer Retention', roles: RoleSets.reports, group: 'Reports' },
  { to: '/reports/daily', label: 'Daily Sales', roles: RoleSets.reports, group: 'Reports' },
  { to: '/reports/expiry', label: 'Near Expiry', roles: RoleSets.reports, group: 'Reports' },
  { to: '/reports/recovery', label: 'Return Recovery', roles: RoleSets.reports, group: 'Reports' },
  { to: '/users', label: 'Users', roles: RoleSets.admin, group: 'Settings' },
  { to: '/branches', label: 'Branches', roles: RoleSets.admin, group: 'Settings' },
  { to: '/reports/branches', label: 'Branch Comparison', roles: RoleSets.accounting, group: 'Reports', module: 'multistore' },
  { to: '/settings', label: 'Settings & Plan', roles: RoleSets.admin, group: 'Settings' },
  { to: '/account', label: 'My Account', roles: ['Owner', 'Pharmacist', 'Cashier', 'PurchaseClerk', 'Accountant', 'DeliveryAgent'], group: 'Settings' },
  { to: '/support', label: 'Help & Support', roles: ['Owner', 'Pharmacist', 'Cashier', 'PurchaseClerk', 'Accountant', 'DeliveryAgent'], group: 'Settings' },
];

function Shell({ children }: { children: ReactNode }) {
  const { session, signOut } = useAuth();
  const navigate = useNavigate();
  const [ent, setEnt] = useState<Entitlements | null>(null);
  useEffect(() => { api.subscription().then(s => setEnt(s.current)).catch(() => {}); }, [session?.branchId]);
  const location = useLocation();
  const [mfaOff, setMfaOff] = useState(false);
  useEffect(() => {
    if (session && (session.role === 'Owner' || session.role === 'Accountant') && session.email !== 'support')
      api.mfaStatus().then(m => setMfaOff(!m.enabled)).catch(() => {});
  }, [session, location.pathname]);
  const support = session?.email === 'support';
  const items = NAV.filter(n => session && n.roles.includes(session.role) && (!n.module || !ent || ent.modules.includes(n.module)));

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'F1') { e.preventDefault(); navigate('/pos'); } };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [navigate]);

  const groups = [...new Set(items.map(i => i.group))];
  return (
    <div className="shell">
      <nav className="side" aria-label="Main">
        <div className="brand">PharmaOS</div>
        <div className="tenant">{session?.tenantName} · {session?.branchName}</div>
        <BranchSwitcher />
        {groups.map(g => (
          <div key={g}>
            <div className="group">{g}</div>
            {items.filter(i => i.group === g).map(i => (
              <NavLink key={i.to} to={i.to}>{i.label}{i.key && <kbd>{i.key}</kbd>}</NavLink>
            ))}
          </div>
        ))}
        <div className="foot">
          <div>{session?.displayName}</div>
          <div className="small" style={{ opacity: .7 }}>{session?.role}</div>
          <button className="small" onClick={signOut}>Sign out</button>
        </div>
      </nav>
      <main className="main">
        {support && <div className="alert error no-print">Provider support session — read-only. Every screen you open is recorded in this customer's audit log.</div>}
        {mfaOff && <div className="alert warn no-print">Your account can access money and customer data. <NavLink to="/account">Turn on two-step sign-in</NavLink> to protect it.</div>}
        {!support && ent?.notice && <div className={`alert ${ent.readOnly ? 'error' : 'warn'} no-print`}>{ent.notice}</div>}
        {children}
      </main>
      <ToastHost />
    </div>
  );
}

function Guard({ roles, children }: { roles: Role[]; children: ReactNode }) {
  const { can } = useAuth();
  return can(...roles) ? <>{children}</> : <div className="panel empty">You do not have access to this page.</div>;
}

export default function App() {
  const { session, loading } = useAuth();
  const location = useLocation();
  if (location.pathname.startsWith('/platform')) return <Routes><Route path="/platform/*" element={<PlatformApp />} /></Routes>;
  // customer-facing ordering pages need no staff login
  if (location.pathname.startsWith('/shop/') || location.pathname.startsWith('/track/'))
    return <Routes><Route path="/shop/:branchId" element={<ShopPage />} /><Route path="/track/:token" element={<TrackPage />} /></Routes>;
  if (loading) return <div className="auth muted">Loading…</div>;
  if (!session) {
    return (
      <Routes>
        <Route path="/register" element={<RegisterPage />} />
        <Route path="*" element={<LoginPage />} />
      </Routes>
    );
  }
  const home = session.role === 'PurchaseClerk' ? '/purchases' : session.role === 'Accountant' ? '/accounting' : session.role === 'DeliveryAgent' ? '/deliveries' : '/pos';
  return (
    <Shell>
      <Routes>
        <Route path="/pos" element={<Guard roles={RoleSets.sell}><PosPage /></Guard>} />
        <Route path="/sales" element={<Guard roles={RoleSets.sell}><SalesPage /></Guard>} />
        <Route path="/purchases" element={<Guard roles={RoleSets.purchase}><PurchasesPage /></Guard>} />
        <Route path="/purchases/:id" element={<Guard roles={RoleSets.purchase}><PurchaseEditPage /></Guard>} />
        <Route path="/stock" element={<StockPage />} />
        <Route path="/products" element={<ProductsPage />} />
        <Route path="/suppliers" element={<Guard roles={RoleSets.purchase}><SuppliersPage /></Guard>} />
        <Route path="/reports/daily" element={<Guard roles={RoleSets.reports}><DailySalesPage /></Guard>} />
        <Route path="/reports/expiry" element={<Guard roles={RoleSets.reports}><NearExpiryPage /></Guard>} />
        <Route path="/users" element={<Guard roles={RoleSets.admin}><UsersPage /></Guard>} />
        <Route path="/supplier-returns" element={<Guard roles={RoleSets.purchase}><SupplierReturnsPage /></Guard>} />
        <Route path="/payables" element={<Guard roles={RoleSets.purchase}><PayablesPage /></Guard>} />
        <Route path="/receivables" element={<Guard roles={RoleSets.sell}><ReceivablesPage /></Guard>} />
        <Route path="/customers" element={<Guard roles={RoleSets.sell}><CustomersPage /></Guard>} />
        <Route path="/reports/recovery" element={<Guard roles={RoleSets.reports}><RecoveryPage /></Guard>} />
        <Route path="/shift" element={<Guard roles={RoleSets.sell}><ShiftPage /></Guard>} />
        <Route path="/stock-counts" element={<Guard roles={RoleSets.salesAdmin}><StockCountPage /></Guard>} />
        <Route path="/recall" element={<Guard roles={RoleSets.salesAdmin}><RecallPage /></Guard>} />
        <Route path="/settings" element={<Guard roles={RoleSets.admin}><SettingsPage /></Guard>} />
        <Route path="/reminders" element={<Guard roles={RoleSets.sell}><RemindersPage /></Guard>} />
        <Route path="/support" element={<SupportPage />} />
        <Route path="/account" element={<AccountPage />} />
        <Route path="/prices" element={<Guard roles={RoleSets.sell}><PricesPage /></Guard>} />
        <Route path="/online-orders" element={<Guard roles={RoleSets.sell}><OnlineOrdersPage /></Guard>} />
        <Route path="/deliveries" element={<Guard roles={RoleSets.delivery}><DeliveriesPage /></Guard>} />
        <Route path="/branches" element={<Guard roles={RoleSets.admin}><BranchesPage /></Guard>} />
        <Route path="/transfers" element={<Guard roles={RoleSets.salesAdmin}><TransfersPage /></Guard>} />
        <Route path="/indents" element={<Guard roles={RoleSets.purchase}><IndentsPage /></Guard>} />
        <Route path="/reports/branches" element={<Guard roles={RoleSets.accounting}><BranchComparisonPage /></Guard>} />
        <Route path="/accounting" element={<Guard roles={RoleSets.accounting}><AccountingPage /></Guard>} />
        <Route path="/reorder" element={<Guard roles={RoleSets.purchase}><ReorderPage /></Guard>} />
        <Route path="/purchase-orders" element={<Guard roles={RoleSets.purchase}><PurchaseOrdersPage /></Guard>} />
        <Route path="/reports/retention" element={<Guard roles={RoleSets.reports}><RetentionPage /></Guard>} />
        <Route path="*" element={<Navigate to={home} replace />} />
      </Routes>
    </Shell>
  );
}
