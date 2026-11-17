using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using PharmaOS.Core.Services;

namespace PharmaOS.Api;

/// <summary>
/// Runs after authentication for tenant tokens:
///  • support sessions are read-only and every request is written to the tenant's audit log;
///  • accounts whose trial ended / subscription was cancelled / deletion is scheduled become read-only,
///    except for data export, support tickets and the subscription screen.
/// </summary>
public class TenantGuardMiddleware(RequestDelegate next)
{
    private static readonly string[] AlwaysAllowed = ["/api/v1/auth", "/api/v1/export", "/api/v1/support", "/api/v1/subscription", "/api/v1/me"];

    public async Task InvokeAsync(HttpContext http, ITenantContext tenant, SubscriptionService subscription, PharmaDbContext db, IClock clock)
    {
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true || user.FindFirst(Claims.Tenant) is null || !http.Request.Path.StartsWithSegments("/api/v1"))
        {
            await next(http);
            return;
        }
        var isRead = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);

        if (user.FindFirst(Claims.Support) is { } support)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                UserId = Guid.TryParse(support.Value, out var p) ? p : null, Action = "support.request", EntityType = "Http",
                Summary = $"{http.Request.Method} {http.Request.Path}{http.Request.QueryString}", At = clock.UtcNow,
            });
            await db.SaveChangesAsync(http.RequestAborted);
            if (!isRead)
            {
                await Problem(http, 403, "support_read_only", "Support sessions are read-only.");
                return;
            }
        }
        else if (!isRead && !AlwaysAllowed.Any(a => http.Request.Path.StartsWithSegments(a)))
        {
            var e = await subscription.CurrentAsync(http.RequestAborted);
            if (e.ReadOnly)
            {
                await Problem(http, 402, "subscription_inactive", e.Notice ?? "Your subscription is not active.");
                return;
            }
        }
        await next(http);
    }

    private static Task Problem(HttpContext http, int status, string code, string detail)
    {
        http.Response.StatusCode = status;
        return Results.Problem(detail: detail, statusCode: status, title: code,
            extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = http.TraceIdentifier }).ExecuteAsync(http);
    }
}

/// <summary>Endpoint filter: the tenant's plan must include the module.</summary>
public static class ModuleGate
{
    public static TBuilder RequireModule<TBuilder>(this TBuilder builder, string module) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            await ctx.HttpContext.RequestServices.GetRequiredService<SubscriptionService>().EnsureModuleAsync(module, ctx.HttpContext.RequestAborted);
            return await next(ctx);
        });
}
