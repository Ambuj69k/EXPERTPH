using PharmaOS.Core.Common;
using PharmaOS.Core.Services;

namespace PharmaOS.Api;

public record AuthResponse(string Token, DateTimeOffset ExpiresAt, SessionInfo Session);
public record ReasonRequest(string Reason);
public record ActiveRequest(bool IsActive);
public record SwitchBranchRequest(Guid BranchId);

public static class Endpoints
{
    public static void MapPharmaEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        // ---- Auth & onboarding (anonymous) ----
        var auth = api.MapGroup("/auth").RequireRateLimiting("auth");
        auth.MapPost("/register", async (RegisterRequest r, AccountService svc, TokenService tokens, CancellationToken ct) =>
        {
            var s = await svc.RegisterAsync(r, ct);
            var (token, exp) = tokens.Issue(s);
            return Results.Ok(new AuthResponse(token, exp, s));
        });
        auth.MapPost("/login", async (LoginRequest r, AccountService svc, TokenService tokens, MfaChallenge mfa, CancellationToken ct) =>
        {
            var outcome = await svc.LoginAsync(r, ct);
            if (outcome.Session is null) return Results.Ok(new MfaPending(true, mfa.Issue(outcome.MfaUserId!.Value, "tenant")));
            var (token, exp) = tokens.Issue(outcome.Session);
            return Results.Ok(new AuthResponse(token, exp, outcome.Session));
        });
        auth.MapPost("/mfa", async (MfaLoginRequest r, AccountService svc, TokenService tokens, MfaChallenge mfa, CancellationToken ct) =>
        {
            var s = await svc.CompleteMfaAsync(mfa.Read(r.MfaToken, "tenant"), r.Code, ct);
            var (token, exp) = tokens.Issue(s);
            return Results.Ok(new AuthResponse(token, exp, s));
        });

        var secured = api.MapGroup("").RequireAuthorization(Policies.Tenant);
        secured.MapGet("/me", (System.Security.Claims.ClaimsPrincipal u, AccountService svc, CancellationToken ct) =>
            u.FindFirst(Claims.Support) is null ? svc.GetSessionAsync(ct) : svc.GetSupportSessionAsync(ct));

        // ---- Users ----
        // ---- own account security ----
        var meSec = secured.MapGroup("/me");
        meSec.MapGet("/mfa", async (AccountService svc, CancellationToken ct) => Results.Ok(new { enabled = await svc.MfaEnabledAsync(ct) }));
        meSec.MapPost("/mfa/setup", (AccountService svc, CancellationToken ct) => svc.BeginMfaSetupAsync(ct));
        meSec.MapPost("/mfa/enable", async (CodeRequest r, AccountService svc, CancellationToken ct) => { await svc.EnableMfaAsync(r.Code, ct); return Results.NoContent(); });
        meSec.MapPost("/mfa/disable", async (DisableMfaRequest r, AccountService svc, CancellationToken ct) => { await svc.DisableMfaAsync(r.Password, ct); return Results.NoContent(); });
        meSec.MapPost("/password", async (PasswordChange r, AccountService svc, CancellationToken ct) => { await svc.ChangePasswordAsync(r, ct); return Results.NoContent(); });

        var users = secured.MapGroup("/users").RequireAuthorization(Policies.Admin);
        users.MapPost("/{id:guid}/reset-password", async (Guid id, PasswordReset r, AccountService svc, CancellationToken ct) =>
        {
            await svc.ResetPasswordAsync(id, r.NewPassword, ct);
            return Results.NoContent();
        });
        users.MapGet("", (AccountService svc, CancellationToken ct) => svc.ListUsersAsync(ct));
        users.MapPost("", (CreateUserRequest r, AccountService svc, CancellationToken ct) => svc.CreateUserAsync(r, ct));
        users.MapPut("/{id:guid}/active", async (Guid id, ActiveRequest r, AccountService svc, CancellationToken ct) =>
        {
            await svc.SetUserActiveAsync(id, r.IsActive, ct);
            return Results.NoContent();
        });

        // ---- Products ----
        var products = secured.MapGroup("/products");
        products.MapGet("", (string? q, bool? includeInactive, int? page, int? pageSize, MasterDataService svc, CancellationToken ct)
            => svc.SearchProductsAsync(q, includeInactive ?? false, page ?? 1, pageSize ?? 50, ct));
        products.MapGet("/{id:guid}", (Guid id, MasterDataService svc, CancellationToken ct) => svc.GetProductAsync(id, ct));
        products.MapPost("", (ProductInput r, MasterDataService svc, CancellationToken ct) => svc.CreateProductAsync(r, ct))
            .RequireAuthorization(Policies.MasterWrite);
        products.MapPut("/{id:guid}", (Guid id, ProductInput r, MasterDataService svc, CancellationToken ct) => svc.UpdateProductAsync(id, r, ct))
            .RequireAuthorization(Policies.MasterWrite);

        // ---- Branch selling prices ----
        var prices = secured.MapGroup("/prices");
        prices.MapGet("", (string? q, bool? onlyWithPrice, PriceService svc, CancellationToken ct) => svc.ListAsync(q, onlyWithPrice ?? false, ct)).RequireAuthorization(Policies.Sell);
        prices.MapPut("/{productId:guid}", async (Guid productId, SetPriceRequest r, PriceService svc, CancellationToken ct) =>
        {
            await svc.SetAsync(productId, r.PricePerPack, ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.SalesAdmin);

        // ---- Suppliers ----
        var suppliers = secured.MapGroup("/suppliers").RequireAuthorization(Policies.Purchase);
        suppliers.MapGet("", (string? q, int? page, int? pageSize, MasterDataService svc, CancellationToken ct)
            => svc.SearchSuppliersAsync(q, page ?? 1, pageSize ?? 50, ct));
        suppliers.MapPost("", (SupplierInput r, MasterDataService svc, CancellationToken ct) => svc.CreateSupplierAsync(r, ct));
        suppliers.MapPut("/{id:guid}", (Guid id, SupplierInput r, MasterDataService svc, CancellationToken ct) => svc.UpdateSupplierAsync(id, r, ct));

        // ---- Customers ----
        secured.MapGet("/customers", (string? q, MasterDataService svc, CancellationToken ct) => svc.SearchCustomersAsync(q, ct))
            .RequireAuthorization(Policies.Sell);

        // ---- Purchases ----
        var purchases = secured.MapGroup("/purchases").RequireAuthorization(Policies.Purchase);
        purchases.MapGet("", (PurchaseStatus? status, Guid? supplierId, DateOnly? from, DateOnly? to, int? page, int? pageSize,
            PurchaseService svc, CancellationToken ct) => svc.ListAsync(status, supplierId, from, to, page ?? 1, pageSize ?? 50, ct));
        purchases.MapGet("/{id:guid}", (Guid id, PurchaseService svc, CancellationToken ct) => svc.GetAsync(id, ct));
        purchases.MapPost("", (PurchaseInput r, PurchaseService svc, CancellationToken ct) => svc.CreateDraftAsync(r, ct));
        purchases.MapPut("/{id:guid}", (Guid id, PurchaseInput r, PurchaseService svc, CancellationToken ct) => svc.UpdateDraftAsync(id, r, ct));
        purchases.MapDelete("/{id:guid}", async (Guid id, PurchaseService svc, CancellationToken ct) =>
        {
            await svc.DeleteDraftAsync(id, ct);
            return Results.NoContent();
        });
        purchases.MapPost("/{id:guid}/post", (Guid id, PurchaseService svc, CancellationToken ct) => svc.PostAsync(id, ct));
        purchases.MapPost("/{id:guid}/cancel", (Guid id, ReasonRequest r, PurchaseService svc, CancellationToken ct) => svc.CancelAsync(id, r.Reason, ct))
            .RequireAuthorization(Policies.SalesAdmin);

        // ---- Purchasing automation ----
        purchases.MapGet("/reorder", (int? lookbackDays, int? coverDays, PurchasingService svc, CancellationToken ct)
            => svc.ReorderAsync(lookbackDays ?? 30, coverDays ?? 15, ct)).RequireModule(Plans.Purchasing);
        purchases.MapPost("/import/preview", (ImportPreviewRequest r, ImportService svc, CancellationToken ct) => svc.PreviewAsync(r, ct)).RequireModule(Plans.Purchasing);
        purchases.MapPost("/import/extract", async (Guid supplierId, HttpRequest req, IInvoiceExtractor extractor, CancellationToken ct) =>
        {
            if (!extractor.IsConfigured) return Results.Problem(statusCode: 501, title: "ai_not_configured",
                detail: "Automatic PDF/image reading is not set up. It needs an approved AI provider. Use CSV import for now.",
                extensions: new Dictionary<string, object?> { ["code"] = "ai_not_configured" });
            var form = await req.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault() ?? throw BusinessException.Validation("Attach a PDF or image.");
            if (file.Length > 10 * 1024 * 1024) throw BusinessException.Validation("File too large (max 10 MB).");
            await using var s = file.OpenReadStream();
            return Results.Ok(await extractor.ExtractAsync(supplierId, s, file.ContentType, ct));
        }).DisableAntiforgery();
        secured.MapPost("/suppliers/{id:guid}/item-map", (Guid id, List<ItemMapInput> r, ImportService svc, CancellationToken ct) => svc.SaveMappingsAsync(id, r, ct))
            .RequireAuthorization(Policies.Purchase);

        var pos = secured.MapGroup("/purchase-orders").RequireAuthorization(Policies.Purchase).RequireModule(Plans.Purchasing);
        pos.MapGet("", (PurchaseOrderStatus? status, int? page, PurchasingService svc, CancellationToken ct) => svc.ListAsync(status, page ?? 1, 50, ct));
        pos.MapGet("/{id:guid}", (Guid id, PurchasingService svc, CancellationToken ct) => svc.GetAsync(id, ct));
        pos.MapPost("", (PurchaseOrderInput r, PurchasingService svc, CancellationToken ct) => svc.CreateAsync(r, ct));
        pos.MapPost("/{id:guid}/send", (Guid id, PurchasingService svc, CancellationToken ct) => svc.SetStatusAsync(id, PurchaseOrderStatus.Sent, ct));
        pos.MapPost("/{id:guid}/close", (Guid id, PurchasingService svc, CancellationToken ct) => svc.SetStatusAsync(id, PurchaseOrderStatus.Closed, ct));
        pos.MapPost("/{id:guid}/cancel", (Guid id, PurchasingService svc, CancellationToken ct) => svc.SetStatusAsync(id, PurchaseOrderStatus.Cancelled, ct));

        // ---- POS & Sales ----
        secured.MapGet("/pos/catalog", (StockService svc, CancellationToken ct) => svc.PosCatalogAsync(ct)).RequireAuthorization(Policies.Sell);
        secured.MapGet("/pos/search", (string q, StockService svc, CancellationToken ct) => svc.PosSearchAsync(q, ct))
            .RequireAuthorization(Policies.Sell);
        var sales = secured.MapGroup("/sales").RequireAuthorization(Policies.Sell);
        sales.MapPost("", (SaleRequest r, SalesService svc, CancellationToken ct) => svc.CreateAsync(r, ct));
        sales.MapGet("", (DateOnly? from, DateOnly? to, string? q, int? page, int? pageSize, SalesService svc, CancellationToken ct)
            => svc.ListAsync(from, to, q, page ?? 1, pageSize ?? 50, ct));
        sales.MapGet("/{id:guid}", (Guid id, SalesService svc, CancellationToken ct) => svc.GetAsync(id, ct));
        sales.MapGet("/{id:guid}/returns", (Guid id, SalesService svc, CancellationToken ct) => svc.ListReturnsForInvoiceAsync(id, ct));
        sales.MapPost("/{id:guid}/cancel", (Guid id, ReasonRequest r, SalesService svc, CancellationToken ct) => svc.CancelAsync(id, r.Reason, ct))
            .RequireAuthorization(Policies.SalesAdmin);
        secured.MapPost("/sales-returns", (SalesReturnRequest r, SalesService svc, CancellationToken ct) => svc.CreateReturnAsync(r, ct))
            .RequireAuthorization(Policies.SalesAdmin);

        // ---- Stock ----
        var stock = secured.MapGroup("/stock");
        stock.MapGet("", (string? q, bool? inStock, bool? belowReorder, int? page, int? pageSize, StockService svc, CancellationToken ct)
            => svc.StockAsync(q, inStock ?? false, belowReorder ?? false, page ?? 1, pageSize ?? 50, ct));
        stock.MapGet("/movements/{productId:guid}", (Guid productId, int? take, StockService svc, CancellationToken ct)
            => svc.MovementsAsync(productId, take ?? 100, ct)).RequireAuthorization(Policies.Reports);
        stock.MapPost("/adjustments", (AdjustmentRequest r, StockService svc, CancellationToken ct) => svc.AdjustAsync(r, ct))
            .RequireAuthorization(Policies.SalesAdmin);

        // ---- Customers (master) ----
        var customers = secured.MapGroup("/customers");
        customers.MapPost("", (CustomerInput r, MasterDataService svc, CancellationToken ct) => svc.CreateCustomerAsync(r, ct))
            .RequireAuthorization(Policies.Sell);
        customers.MapPut("/{id:guid}", (Guid id, CustomerInput r, MasterDataService svc, CancellationToken ct) => svc.UpdateCustomerAsync(id, r, ct))
            .RequireAuthorization(Policies.SalesAdmin); // credit limits are a control, not a counter action

        // ---- CRM ----
        customers.MapGet("/lookup", async (string phone, CrmService svc, CancellationToken ct) =>
            await svc.LookupByPhoneAsync(phone, ct) is { } c ? Results.Ok(c) : Results.NoContent()).RequireAuthorization(Policies.Sell);
        customers.MapGet("/{id:guid}/profile", (Guid id, CrmService svc, CancellationToken ct) => svc.ProfileAsync(id, ct)).RequireAuthorization(Policies.Sell);
        customers.MapGet("/{id:guid}/purchases", (Guid id, int? page, CrmService svc, CancellationToken ct) => svc.PurchasesAsync(id, page ?? 1, 20, ct))
            .RequireAuthorization(Policies.Sell);
        customers.MapPost("/{id:guid}/consents", (Guid id, ConsentInput r, CrmService svc, CancellationToken ct) => svc.RecordConsentAsync(id, r, ct))
            .RequireAuthorization(Policies.Sell);
        customers.MapGet("/{id:guid}/loyalty", (Guid id, LoyaltyService svc, CancellationToken ct) => svc.AccountAsync(id, ct)).RequireAuthorization(Policies.Sell);
        customers.MapPost("/{id:guid}/loyalty/adjust", (Guid id, AdjustPointsRequest r, LoyaltyService svc, CancellationToken ct) => svc.AdjustAsync(id, r, ct))
            .RequireAuthorization(Policies.Admin);
        customers.MapPost("/merge", (MergeRequest r, CrmService svc, CancellationToken ct) => svc.MergeAsync(r, ct)).RequireAuthorization(Policies.SalesAdmin);

        var reminders = secured.MapGroup("/reminders").RequireAuthorization(Policies.Sell).RequireModule(Plans.Crm);
        reminders.MapGet("", (int? dueWithinDays, ReminderStatus? status, Guid? customerId, CrmService svc, CancellationToken ct)
            => svc.ListRemindersAsync(dueWithinDays, status, customerId, ct));
        reminders.MapPost("", (ReminderInput r, CrmService svc, CancellationToken ct) => svc.CreateReminderAsync(r, ct));
        reminders.MapPut("/{id:guid}", (Guid id, ReminderUpdate r, CrmService svc, CancellationToken ct) => svc.UpdateReminderAsync(id, r, ct));
        reminders.MapPost("/{id:guid}/notify", (Guid id, NotifyRequest r, CrmService svc, CancellationToken ct) => svc.PrepareReminderAsync(id, r, ct));

        var notifications = secured.MapGroup("/notifications").RequireAuthorization(Policies.Sell).RequireModule(Plans.Crm);
        notifications.MapGet("", (Guid? customerId, int? page, CrmService svc, CancellationToken ct) => svc.ListNotificationsAsync(customerId, page ?? 1, 50, null, ct));
        notifications.MapPost("/{id:guid}/sent", (Guid id, CrmService svc, CancellationToken ct) => svc.MarkNotificationAsync(id, true, null, ct));
        notifications.MapPost("/{id:guid}/failed", (Guid id, ReasonRequest r, CrmService svc, CancellationToken ct) => svc.MarkNotificationAsync(id, false, r.Reason, ct));
        secured.MapGet("/notification-templates", (CrmService svc, CancellationToken ct) => svc.TemplatesAsync(ct)).RequireAuthorization(Policies.Admin);
        secured.MapPut("/notification-templates", (TemplateInput r, CrmService svc, CancellationToken ct) => svc.SaveTemplateAsync(r, ct)).RequireAuthorization(Policies.Admin);
        secured.MapGet("/loyalty/settings", (LoyaltyService svc, CancellationToken ct) => svc.SettingsAsync(ct));
        secured.MapPut("/loyalty/settings", (LoyaltySettings r, LoyaltyService svc, CancellationToken ct) => svc.UpdateSettingsAsync(r, ct)).RequireAuthorization(Policies.Admin).RequireModule(Plans.Crm);

        // ---- Returns to supplier (expiry / damage / recall) ----
        var pr = secured.MapGroup("/purchase-returns").RequireAuthorization(Policies.Purchase);
        pr.MapGet("/candidates", (int? days, PurchaseReturnService svc, CancellationToken ct) => svc.CandidatesAsync(days ?? 90, ct));
        pr.MapGet("", (PurchaseReturnStatus? status, Guid? supplierId, int? page, int? pageSize, PurchaseReturnService svc, CancellationToken ct)
            => svc.ListAsync(status, supplierId, page ?? 1, pageSize ?? 50, ct));
        pr.MapGet("/{id:guid}", (Guid id, PurchaseReturnService svc, CancellationToken ct) => svc.GetAsync(id, ct));
        pr.MapPost("", (PurchaseReturnInput r, PurchaseReturnService svc, CancellationToken ct) => svc.CreateDraftAsync(r, ct));
        pr.MapDelete("/{id:guid}", async (Guid id, PurchaseReturnService svc, CancellationToken ct) =>
        {
            await svc.DeleteDraftAsync(id, ct);
            return Results.NoContent();
        });
        pr.MapPost("/{id:guid}/dispatch", (Guid id, PurchaseReturnService svc, CancellationToken ct) => svc.DispatchAsync(id, ct));
        pr.MapPost("/{id:guid}/settle", (Guid id, SettleReturnRequest r, PurchaseReturnService svc, CancellationToken ct) => svc.SettleAsync(id, r, ct));
        pr.MapPost("/{id:guid}/reject", (Guid id, ReasonRequest r, PurchaseReturnService svc, CancellationToken ct) => svc.RejectAsync(id, r.Reason, ct));

        // ---- Payables ----
        var payables = secured.MapGroup("/payables").RequireAuthorization(Policies.Purchase);
        payables.MapGet("", (LedgerService svc, CancellationToken ct) => svc.PayablesSummaryAsync(ct));
        payables.MapGet("/aging", (LedgerService svc, CancellationToken ct) => svc.PayablesAgingAsync(ct));
        payables.MapGet("/suppliers/{id:guid}", (Guid id, LedgerService svc, CancellationToken ct) => svc.SupplierStatementAsync(id, ct));
        var sp = secured.MapGroup("/supplier-payments").RequireAuthorization(Policies.SalesAdmin); // paying money out: owner/pharmacist
        sp.MapGet("", (Guid? supplierId, int? page, int? pageSize, LedgerService svc, CancellationToken ct)
            => svc.ListSupplierPaymentsAsync(supplierId, page ?? 1, pageSize ?? 50, ct));
        sp.MapPost("", (SupplierPaymentRequest r, LedgerService svc, CancellationToken ct) => svc.CreateSupplierPaymentAsync(r, ct));
        sp.MapPost("/{id:guid}/cancel", (Guid id, ReasonRequest r, LedgerService svc, CancellationToken ct) => svc.CancelSupplierPaymentAsync(id, r.Reason, ct));

        // ---- Receivables ----
        var receivables = secured.MapGroup("/receivables").RequireAuthorization(Policies.Sell);
        receivables.MapGet("", (LedgerService svc, CancellationToken ct) => svc.ReceivablesSummaryAsync(ct));
        receivables.MapGet("/aging", (LedgerService svc, CancellationToken ct) => svc.ReceivablesAgingAsync(ct));
        receivables.MapGet("/customers/{id:guid}", (Guid id, LedgerService svc, CancellationToken ct) => svc.CustomerStatementAsync(id, ct));
        var cr = secured.MapGroup("/customer-receipts").RequireAuthorization(Policies.Sell);
        cr.MapGet("", (Guid? customerId, int? page, int? pageSize, LedgerService svc, CancellationToken ct)
            => svc.ListCustomerReceiptsAsync(customerId, page ?? 1, pageSize ?? 50, ct));
        cr.MapPost("", (CustomerReceiptRequest r, LedgerService svc, CancellationToken ct) => svc.CreateCustomerReceiptAsync(r, ct));
        cr.MapPost("/{id:guid}/cancel", (Guid id, ReasonRequest r, LedgerService svc, CancellationToken ct) => svc.CancelCustomerReceiptAsync(id, r.Reason, ct))
            .RequireAuthorization(Policies.SalesAdmin);

        // ---- Settings ----
        secured.MapGet("/settings", (ShiftService svc, CancellationToken ct) => svc.GetSettingsAsync(ct));
        secured.MapPut("/settings", (TenantSettings r, ShiftService svc, CancellationToken ct) => svc.UpdateSettingsAsync(r, ct))
            .RequireAuthorization(Policies.Admin);

        // ---- Cash shifts ----
        var shifts = secured.MapGroup("/shifts").RequireAuthorization(Policies.Sell);
        shifts.MapGet("/current", async (ShiftService svc, CancellationToken ct) =>
            await svc.CurrentAsync(ct) is { } s ? Results.Ok(s) : Results.NoContent());
        shifts.MapPost("/open", (OpenShiftRequest r, ShiftService svc, CancellationToken ct) => svc.OpenAsync(r, ct));
        shifts.MapPost("/current/movements", (CashMovementRequest r, ShiftService svc, CancellationToken ct) => svc.AddMovementAsync(r, ct));
        shifts.MapPost("/current/close", (CloseShiftRequest r, ShiftService svc, CancellationToken ct) => svc.CloseAsync(r, ct));
        shifts.MapGet("/{id:guid}", (Guid id, ShiftService svc, CancellationToken ct) => svc.GetAsync(id, ct));
        shifts.MapGet("", (DateOnly? from, DateOnly? to, ShiftService svc, CancellationToken ct) => svc.ListAsync(from, to, ct))
            .RequireAuthorization(Policies.Reports);

        // ---- Stock counts ----
        var counts = secured.MapGroup("/stock-counts").RequireAuthorization(Policies.SalesAdmin);
        counts.MapGet("", (StockCountService svc, CancellationToken ct) => svc.ListAsync(ct));
        counts.MapGet("/{id:guid}", (Guid id, StockCountService svc, CancellationToken ct) => svc.GetAsync(id, ct));
        counts.MapPost("", (CreateCountRequest r, StockCountService svc, CancellationToken ct) => svc.CreateAsync(r, ct));
        counts.MapPut("/{id:guid}/entries", (Guid id, List<CountEntry> r, StockCountService svc, CancellationToken ct) => svc.SaveEntriesAsync(id, r, ct));
        counts.MapPost("/{id:guid}/post", (Guid id, StockCountService svc, CancellationToken ct) => svc.PostAsync(id, ct));
        counts.MapPost("/{id:guid}/cancel", async (Guid id, StockCountService svc, CancellationToken ct) =>
        {
            await svc.CancelAsync(id, ct);
            return Results.NoContent();
        });

        // ---- Batch recall / quarantine ----
        var batches = secured.MapGroup("/batches").RequireAuthorization(Policies.SalesAdmin);
        batches.MapGet("", (string q, RecallService svc, CancellationToken ct) => svc.SearchAsync(q, ct));
        batches.MapGet("/{id:guid}/trace", (Guid id, RecallService svc, CancellationToken ct) => svc.TraceAsync(id, ct));
        batches.MapPost("/{id:guid}/block", async (Guid id, ReasonRequest r, RecallService svc, CancellationToken ct) =>
        {
            await svc.SetBlockedAsync(id, true, r.Reason, ct);
            return Results.NoContent();
        });
        batches.MapPost("/{id:guid}/unblock", async (Guid id, RecallService svc, CancellationToken ct) =>
        {
            await svc.SetBlockedAsync(id, false, null, ct);
            return Results.NoContent();
        });

        // ---- Branches & multi-store ----
        secured.MapPost("/auth/switch-branch", async (SwitchBranchRequest r, AccountService svc, TokenService tokens, CancellationToken ct) =>
        {
            var s = await svc.SwitchBranchAsync(r.BranchId, ct);
            var (token, exp) = tokens.Issue(s);
            return Results.Ok(new AuthResponse(token, exp, s));
        });
        var branches = secured.MapGroup("/branches");
        branches.MapGet("", (MultiStoreService svc, CancellationToken ct) => svc.BranchesAsync(ct));
        branches.MapGet("/directory", (MultiStoreService svc, CancellationToken ct) => svc.AllActiveBranchesAsync(ct));
        branches.MapPost("", (BranchInput r, MultiStoreService svc, CancellationToken ct) => svc.SaveBranchAsync(null, r, ct)).RequireAuthorization(Policies.Admin);
        branches.MapPut("/{id:guid}", (Guid id, BranchInput r, MultiStoreService svc, CancellationToken ct) => svc.SaveBranchAsync(id, r, ct)).RequireAuthorization(Policies.Admin);
        users.MapPut("/{id:guid}/branch", async (Guid id, SwitchBranchRequest r, MultiStoreService svc, CancellationToken ct) =>
        {
            await svc.AssignUserBranchAsync(id, r.BranchId, ct);
            return Results.NoContent();
        });

        var transfers = secured.MapGroup("/transfers").RequireAuthorization(Policies.SalesAdmin).RequireModule(Plans.MultiStore);
        transfers.MapGet("", (TransferStatus? status, MultiStoreService svc, CancellationToken ct) => svc.ListTransfersAsync(status, ct));
        transfers.MapGet("/{id:guid}", (Guid id, MultiStoreService svc, CancellationToken ct) => svc.GetTransferAsync(id, ct));
        transfers.MapPost("", (TransferInput r, MultiStoreService svc, CancellationToken ct) => svc.CreateTransferAsync(r, ct));
        transfers.MapPost("/{id:guid}/dispatch", (Guid id, MultiStoreService svc, CancellationToken ct) => svc.DispatchAsync(id, ct));
        transfers.MapPost("/{id:guid}/receive", (Guid id, ReceiveRequest r, MultiStoreService svc, CancellationToken ct) => svc.ReceiveAsync(id, r, ct));
        transfers.MapPost("/{id:guid}/cancel", async (Guid id, MultiStoreService svc, CancellationToken ct) =>
        {
            await svc.CancelTransferAsync(id, ct);
            return Results.NoContent();
        });
        var indents = secured.MapGroup("/indents").RequireAuthorization(Policies.Purchase).RequireModule(Plans.MultiStore);
        indents.MapGet("", (MultiStoreService svc, CancellationToken ct) => svc.ListIndentsAsync(ct));
        indents.MapPost("", (IndentInput r, MultiStoreService svc, CancellationToken ct) => svc.CreateIndentAsync(r, ct));
        indents.MapPost("/{id:guid}/fulfil", (Guid id, MultiStoreService svc, CancellationToken ct) => svc.FulfilIndentAsync(id, ct)).RequireAuthorization(Policies.SalesAdmin);
        indents.MapPost("/{id:guid}/reject", (Guid id, ReasonRequest r, MultiStoreService svc, CancellationToken ct) => svc.RejectIndentAsync(id, r.Reason, ct));

        // ---- Accounting ----
        var acc = secured.MapGroup("/accounting").RequireAuthorization(Policies.Accounting).RequireModule(Plans.Accounting);
        acc.MapPost("/sync", (AccountingService svc, CancellationToken ct) => svc.SyncAsync(ct));
        acc.MapGet("/accounts", (AccountingService svc, CancellationToken ct) => svc.AccountsAsync(ct));
        acc.MapPost("/accounts", (AccountInput r, AccountingService svc, CancellationToken ct) => svc.SaveAccountAsync(null, r, ct));
        acc.MapPut("/accounts/{id:guid}", (Guid id, AccountInput r, AccountingService svc, CancellationToken ct) => svc.SaveAccountAsync(id, r, ct));
        acc.MapGet("/journals", async (DateOnly? from, DateOnly? to, string? sourceType, AccountingService svc, CancellationToken ct) =>
        {
            await svc.SyncAsync(ct);
            return await svc.JournalsAsync(from, to, sourceType, null, ct);
        });
        acc.MapPost("/journals", (ManualJournalInput r, AccountingService svc, CancellationToken ct) => svc.CreateManualAsync(r, ct));
        acc.MapPost("/journals/{id:guid}/reverse", (Guid id, AccountingService svc, CancellationToken ct) => svc.ReverseManualAsync(id, ct));
        acc.MapGet("/trial-balance", (DateOnly asOf, AccountingService svc, CancellationToken ct) => svc.TrialBalanceAsync(asOf, ct));
        acc.MapGet("/profit-and-loss", (DateOnly from, DateOnly to, AccountingService svc, CancellationToken ct) => svc.ProfitAndLossAsync(from, to, ct));
        acc.MapGet("/balance-sheet", (DateOnly asOf, AccountingService svc, CancellationToken ct) => svc.BalanceSheetAsync(asOf, ct));
        acc.MapGet("/ledger/{accountId:guid}", (Guid accountId, DateOnly from, DateOnly to, AccountingService svc, CancellationToken ct) => svc.LedgerAsync(accountId, from, to, ct));
        acc.MapGet("/cash-book", async (DateOnly from, DateOnly to, string? book, AccountingService svc, CancellationToken ct) =>
            await svc.LedgerAsync(await svc.AccountIdAsync(book?.ToUpperInvariant() switch { "BANK" => "BANK", "CLEARING" => "CLEARING", _ => "CASH" }, ct), from, to, ct));
        acc.MapGet("/gst-summary", (DateOnly from, DateOnly to, AccountingService svc, CancellationToken ct) => svc.GstSummaryAsync(from, to, ct));
        acc.MapGet("/control-checks", (AccountingService svc, LedgerService ledger, CancellationToken ct) => svc.ControlChecksAsync(ledger, ct));
        acc.MapGet("/lock", async (AccountingService svc, CancellationToken ct) => Results.Ok(new { lockedUntil = await svc.LockDateAsync(ct) }));
        acc.MapPost("/close", async (CloseRequest r, AccountingService svc, CancellationToken ct) => Results.Ok(new { lockedUntil = await svc.CloseAsync(r, ct) }))
            .RequireAuthorization(Policies.Admin);
        acc.MapPost("/reopen", async (CloseRequest? r, AccountingService svc, CancellationToken ct) => Results.Ok(new { lockedUntil = await svc.ReopenAsync(r?.UpTo, ct) }))
            .RequireAuthorization(Policies.Admin);
        acc.MapGet("/export.tally.xml", async (DateOnly from, DateOnly to, AccountingService svc, CancellationToken ct) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(await svc.ExportTallyXmlAsync(from, to, ct)), "application/xml", $"tally_{from:yyyyMMdd}_{to:yyyyMMdd}.xml"));
        acc.MapGet("/export.csv", async (DateOnly from, DateOnly to, AccountingService svc, CancellationToken ct) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(await svc.ExportCsvAsync(from, to, ct)), "text/csv", $"journal_{from:yyyyMMdd}_{to:yyyyMMdd}.csv"));

        // ---- Subscription, support, data export (tenant side) ----
        secured.MapGet("/subscription", async (SubscriptionService svc, CancellationToken ct) => Results.Ok(new { current = await svc.CurrentAsync(ct), plans = Plans.Catalog }));
        secured.MapPut("/settings/support-access", async (SupportAccessRequest r, TenantSupportService svc, CancellationToken ct) =>
            Results.Ok(new { supportAccessUntil = await svc.SetSupportAccessAsync(r.Hours, ct) })).RequireAuthorization(Policies.Admin);
        var support = secured.MapGroup("/support/tickets");
        support.MapGet("", (TenantSupportService svc, CancellationToken ct) => svc.ListAsync(ct));
        support.MapPost("", (TicketInput r, TenantSupportService svc, CancellationToken ct) => svc.CreateTicketAsync(r, ct));
        support.MapPost("/{id:guid}/reply", (Guid id, ReplyInput r, TenantSupportService svc, CancellationToken ct) => svc.ReplyAsync(id, r, ct));
        secured.MapGet("/export/all.zip", async (ExportService svc, CancellationToken ct) =>
            Results.File(await svc.ExportAllAsync(ct), "application/zip", $"pharmaos_export_{DateTime.UtcNow:yyyyMMdd}.zip")).RequireAuthorization(Policies.Admin);

        // ---- Reports ----
        var reports = secured.MapGroup("/reports").RequireAuthorization(Policies.Reports);
        reports.MapGet("/controlled-drugs", (DateOnly from, DateOnly to, ControlledDrugService svc, CancellationToken ct) => svc.RegisterAsync(from, to, ct));
        reports.MapGet("/near-expiry", (int? days, StockService svc, CancellationToken ct) => svc.NearExpiryAsync(days ?? 90, ct));
        reports.MapGet("/daily-sales", (DateOnly date, StockService svc, CancellationToken ct) => svc.DailySalesAsync(date, ct));
        reports.MapGet("/return-recovery", (PurchaseReturnService svc, CancellationToken ct) => svc.RecoveryAsync(ct));
        reports.MapGet("/branch-comparison", (DateOnly from, DateOnly to, MultiStoreService svc, CancellationToken ct) => svc.CompareAsync(from, to, ct)).RequireModule(Plans.MultiStore);
        reports.MapGet("/customer-retention", (DateOnly from, DateOnly to, CrmService svc, CancellationToken ct) => svc.RetentionAsync(from, to, ct));
    }
}
