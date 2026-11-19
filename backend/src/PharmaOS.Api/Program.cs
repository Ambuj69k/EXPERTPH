using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PharmaOS.Api;
using PharmaOS.Core;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Services;

var builder = WebApplication.CreateBuilder(args);

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var connectionString = builder.Configuration.GetConnectionString("Pharma")
                       ?? throw new InvalidOperationException("ConnectionStrings:Pharma is not configured.");

builder.Services.AddPharmaCore(connectionString, builder.Configuration["Storage:Root"]);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<TokenService>();
// Data Protection keys encrypt MFA seeds: persist them on a data drive (default next to the app) and back them up with the database
builder.Services.AddDataProtection().SetApplicationName("PharmaOS")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["Security:KeysPath"] ?? Path.Combine(AppContext.BaseDirectory, "keys")));
builder.Services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddSingleton<MfaChallenge>();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = jwt.SigningKey(),
        ClockSkew = TimeSpan.FromMinutes(1),
        RoleClaimType = Claims.Role,
        NameClaimType = Claims.User,
    };
    // Deactivated users lose access immediately, not when their token expires.
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            var db = ctx.HttpContext.RequestServices.GetRequiredService<PharmaDbContext>();
            var uid = Guid.TryParse(ctx.Principal?.FindFirst(Claims.User)?.Value, out var g) ? g : Guid.Empty;
            // provider staff token
            if (ctx.Principal?.FindFirst(Claims.Scope)?.Value == "platform")
            {
                if (!await db.PlatformUsers.AnyAsync(p => p.Id == uid && p.IsActive)) ctx.Fail("Platform user inactive.");
                return;
            }
            // read-only support session: provider user must be active AND the tenant owner's grant still open
            if (ctx.Principal?.FindFirst(Claims.Support) is not null)
            {
                var tid = Guid.TryParse(ctx.Principal.FindFirst(Claims.Tenant)?.Value, out var t) ? t : Guid.Empty;
                var granted = await db.Tenants.AnyAsync(x => x.Id == tid && x.SupportAccessUntil > DateTimeOffset.UtcNow && x.DeletedAt == null);
                if (!granted || !await db.PlatformUsers.AnyAsync(p => p.Id == uid && p.IsActive)) ctx.Fail("Support access expired or revoked.");
                return;
            }
            var bid = Guid.TryParse(ctx.Principal?.FindFirst(Claims.Branch)?.Value, out var b) ? b : Guid.Empty;
            var user = await db.Users.IgnoreQueryFilters().Where(u => u.Id == uid)
                .Select(u => new { u.IsActive, u.DefaultBranchId, u.Role, TenantActive = db.Tenants.Any(t => t.Id == u.TenantId && t.IsActive) })
                .SingleOrDefaultAsync();
            if (user is null || !user.IsActive) ctx.Fail("User inactive.");
            else if (!user.TenantActive) ctx.Fail("Account suspended."); // suspension takes effect immediately, not at token expiry
            // staff tokens are only valid for their currently assigned branch (owner/accountant may switch)
            else if (user.Role is not (UserRole.Owner or UserRole.Accountant) && user.DefaultBranchId != bid) ctx.Fail("Branch reassigned.");
        },
    };
});
builder.Services.AddAuthorization(o =>
{
    foreach (var (name, roles) in Policies.Map)
        o.AddPolicy(name, p => p.RequireRole(roles.Select(r => r.ToString())).RequireClaim(Claims.Tenant));
    o.AddPolicy(Policies.Tenant, p => p.RequireClaim(Claims.Tenant));
    o.AddPolicy(Policies.Platform, p => p.RequireClaim(Claims.Scope, "platform"));
});

var authPerMinute = builder.Configuration.GetValue("RateLimit:AuthPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
    // public ordering pages (no login): generous for browsing, still bounded per IP
    o.AddPolicy("public", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(60, authPerMinute), Window = TimeSpan.FromMinutes(1) }));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Refuse to run outside Development with the sample secrets that live in the repository.
if (!app.Environment.IsDevelopment())
{
    if (jwt.Key.Contains("dev-only", StringComparison.OrdinalIgnoreCase) || jwt.Key.Contains("change-me", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Jwt:Key is the development sample key. Set a long random key from a secret store before running in " + app.Environment.EnvironmentName + ".");
    if (builder.Configuration["Platform:BootstrapPassword"] is { } bp && bp.Contains("Dev-Only", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Platform:BootstrapPassword is the development sample password. Set a strong one from a secret store.");
}

// Business errors → RFC 7807 problem+json with a stable machine-readable "code".
app.UseExceptionHandler(e => e.Run(async http =>
{
    var ex = http.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, code, detail) = ex switch
    {
        BusinessException b => (b.Status, b.Code, b.Message),
        DbUpdateConcurrencyException => (409, "concurrency", "The record was changed by someone else. Reload and try again."),
        BadHttpRequestException bad => (400, "bad_request", bad.Message),
        _ => (500, "server_error", "Something went wrong. Please try again; if it continues, contact support."),
    };
    if (status == 500) app.Logger.LogError(ex, "Unhandled error");
    http.Response.StatusCode = status;
    await Results.Problem(detail: detail, statusCode: status, title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = http.TraceIdentifier }).ExecuteAsync(http);
}));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PharmaDbContext>().Database.MigrateAsync();
}
using (var scope = app.Services.CreateScope())
{
    // first provider admin from configuration (dev: appsettings.Development.json; prod: secret store)
    await scope.ServiceProvider.GetRequiredService<PlatformService>().EnsureBootstrapAsync(
        builder.Configuration["Platform:BootstrapEmail"], builder.Configuration["Platform:BootstrapPassword"]);
}

app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantGuardMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPharmaEndpoints();
app.MapPlatformEndpoints();
app.MapOnlineEndpoints();
app.MapDistributorEndpoints();
app.MapSfaEndpoints();
app.MapHospitalEndpoints();
app.MapManufacturingEndpoints();

app.Run();

public partial class Program;
