using System.Security.Claims;
using PharmaOS.Core.Domain;
using PharmaOS.Core.Services;

namespace PharmaOS.Api;

public record PlatformLogin(string Email, string Password);
public record PlatformAuthResponse(string Token, DateTimeOffset ExpiresAt, PlatformSession Session);
public record PaidRequest(string Reference);
public record ProviderReply(string? Body, TicketStatus Status);

/// <summary>SaaS provider console API. Requires a platform token; tenant tokens are rejected by policy.</summary>
public static class PlatformEndpoints
{
    private static Guid Me(ClaimsPrincipal u) => Guid.Parse(u.FindFirst(Claims.User)!.Value);

    public static void MapPlatformEndpoints(this WebApplication app)
    {
        var root = app.MapGroup("/api/v1/platform");
        root.MapPost("/auth/login", async (PlatformLogin r, PlatformService svc, TokenService tokens, MfaChallenge mfa, CancellationToken ct) =>
        {
            var (s, mfaUser) = await svc.LoginAsync(r.Email, r.Password, ct);
            if (s is null) return Results.Ok(new MfaPending(true, mfa.Issue(mfaUser!.Value, "platform")));
            var (token, exp) = tokens.IssuePlatform(s);
            return Results.Ok(new PlatformAuthResponse(token, exp, s));
        }).RequireRateLimiting("auth");
        root.MapPost("/auth/mfa", async (MfaLoginRequest r, PlatformService svc, TokenService tokens, MfaChallenge mfa, CancellationToken ct) =>
        {
            var s = await svc.CompleteMfaAsync(mfa.Read(r.MfaToken, "platform"), r.Code, ct);
            var (token, exp) = tokens.IssuePlatform(s);
            return Results.Ok(new PlatformAuthResponse(token, exp, s));
        }).RequireRateLimiting("auth");

        var p = root.MapGroup("").RequireAuthorization(Policies.Platform);
        p.MapGet("/me/mfa", async (ClaimsPrincipal u, PlatformService svc, CancellationToken ct) => Results.Ok(new { enabled = await svc.MfaEnabledAsync(Me(u), ct) }));
        p.MapPost("/me/mfa/setup", (ClaimsPrincipal u, PlatformService svc, CancellationToken ct) => svc.BeginMfaSetupAsync(Me(u), ct));
        p.MapPost("/me/mfa/enable", async (CodeRequest r, ClaimsPrincipal u, PlatformService svc, CancellationToken ct) =>
        {
            await svc.EnableMfaAsync(Me(u), r.Code, ct);
            return Results.NoContent();
        });
        p.MapGet("/dashboard", (PlatformService svc, CancellationToken ct) => svc.DashboardAsync(ct));
        p.MapGet("/tenants", (PlatformService svc, CancellationToken ct) => svc.TenantsAsync(ct));
        p.MapGet("/tenants/{id:guid}", (Guid id, PlatformService svc, CancellationToken ct) => svc.TenantAsync(id, ct));
        p.MapPut("/tenants/{id:guid}/subscription", (Guid id, SubscriptionInput r, ClaimsPrincipal u, PlatformService svc, CancellationToken ct) =>
            svc.UpdateSubscriptionAsync(id, Me(u), r, ct));
        p.MapPost("/tenants/{id:guid}/suspend", async (Guid id, ClaimsPrincipal u, PlatformService svc, CancellationToken ct) =>
        {
            await svc.SetActiveAsync(id, Me(u), false, ct);
            return Results.NoContent();
        });
        p.MapPost("/tenants/{id:guid}/reactivate", async (Guid id, ClaimsPrincipal u, PlatformService svc, CancellationToken ct) =>
        {
            await svc.SetActiveAsync(id, Me(u), true, ct);
            return Results.NoContent();
        });
        p.MapPost("/tenants/{id:guid}/invoices", (Guid id, PlatformInvoiceInput r, ClaimsPrincipal u, PlatformService svc, CancellationToken ct) =>
            svc.CreateInvoiceAsync(id, Me(u), r, ct));
        p.MapGet("/invoices/overdue", (PlatformService svc, CancellationToken ct) => svc.InvoicesAsync(null, true, ct));
        p.MapPost("/invoices/{id:guid}/paid", (Guid id, PaidRequest r, ClaimsPrincipal u, PlatformService svc, CancellationToken ct) =>
            svc.MarkPaidAsync(id, Me(u), r.Reference, ct));
        p.MapPost("/invoices/{id:guid}/void", async (Guid id, ClaimsPrincipal u, PlatformService svc, CancellationToken ct) =>
        {
            await svc.VoidInvoiceAsync(id, Me(u), ct);
            return Results.NoContent();
        });
        p.MapGet("/tickets", (PlatformService svc, CancellationToken ct) => svc.OpenTicketsAsync(ct));
        p.MapPost("/tickets/{id:guid}/reply", async (Guid id, ProviderReply r, ClaimsPrincipal u, PlatformService svc, CancellationToken ct) =>
        {
            var me = await svc.LoginlessSessionAsync(Me(u), ct);
            return await svc.ReplyAsync(id, me, new ReplyInput(r.Body ?? ""), r.Status, ct);
        });
        p.MapPost("/tenants/{id:guid}/support-session", async (Guid id, ClaimsPrincipal u, PlatformService svc, TokenService tokens, CancellationToken ct) =>
        {
            var s = await svc.StartSupportSessionAsync(id, Me(u), ct);
            var (token, exp) = tokens.IssueSupport(Me(u), s);
            return Results.Ok(new { token, expiresAt = exp, session = s });
        });
        p.MapPost("/tenants/{id:guid}/schedule-deletion", async (Guid id, ScheduleDeletionRequest r, ClaimsPrincipal u, PlatformService svc, CancellationToken ct) =>
        {
            await svc.ScheduleDeletionAsync(id, Me(u), r.DaysFromNow, ct);
            return Results.NoContent();
        });
        p.MapPost("/tenants/{id:guid}/cancel-deletion", async (Guid id, ClaimsPrincipal u, PlatformService svc, CancellationToken ct) =>
        {
            await svc.CancelDeletionAsync(id, Me(u), ct);
            return Results.NoContent();
        });
        p.MapPost("/tenants/{id:guid}/purge", async (Guid id, ClaimsPrincipal u, PlatformService svc, CancellationToken ct) =>
        {
            await svc.PurgeAsync(id, Me(u), ct);
            return Results.NoContent();
        });
    }
}
