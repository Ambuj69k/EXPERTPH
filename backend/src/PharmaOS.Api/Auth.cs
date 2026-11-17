using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PharmaOS.Core.Common;
using PharmaOS.Core.Services;

namespace PharmaOS.Api;

public static class Claims
{
    public const string Tenant = "tid";
    public const string Branch = "bid";
    public const string Role = "role";
    public const string User = "sub";
    /// <summary>"platform" on SaaS-provider tokens.</summary>
    public const string Scope = "scope";
    /// <summary>Present on read-only support-session tokens (value = platform user id).</summary>
    public const string Support = "support";
}

public static class Policies
{
    public const string Sell = "sell";             // billing counter
    public const string SalesAdmin = "salesAdmin"; // cancel / return
    public const string Purchase = "purchase";
    public const string MasterWrite = "masterWrite";
    public const string Reports = "reports";
    public const string Admin = "admin";
    public const string Accounting = "accounting";
    public const string Delivery = "delivery";
    /// <summary>Any tenant (pharmacy) token — never satisfied by provider tokens.</summary>
    public const string Tenant = "tenant";
    /// <summary>SaaS provider staff only — never satisfied by tenant tokens.</summary>
    public const string Platform = "platform";

    public static readonly Dictionary<string, UserRole[]> Map = new()
    {
        [Accounting] = [UserRole.Owner, UserRole.Accountant],
        [Delivery] = [UserRole.Owner, UserRole.Pharmacist, UserRole.DeliveryAgent],
        [Sell] = [UserRole.Owner, UserRole.Pharmacist, UserRole.Cashier],
        [SalesAdmin] = [UserRole.Owner, UserRole.Pharmacist],
        [Purchase] = [UserRole.Owner, UserRole.Pharmacist, UserRole.PurchaseClerk],
        [MasterWrite] = [UserRole.Owner, UserRole.Pharmacist, UserRole.PurchaseClerk],
        [Reports] = [UserRole.Owner, UserRole.Pharmacist, UserRole.Accountant],
        [Admin] = [UserRole.Owner],
    };
}

public class JwtOptions
{
    public string Issuer { get; set; } = "pharmaos";
    public string Audience { get; set; } = "pharmaos";
    public string Key { get; set; } = "";
    public int LifetimeHours { get; set; } = 12;

    public SymmetricSecurityKey SigningKey()
    {
        if (Key.Length < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 characters. Set it via user-secrets or environment.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key));
    }
}

public class TokenService(JwtOptions options)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(SessionInfo s)
    {
        var expires = DateTimeOffset.UtcNow.AddHours(options.LifetimeHours);
        var token = new JwtSecurityToken(
            options.Issuer, options.Audience,
            [
                new Claim(Claims.User, s.UserId.ToString()),
                new Claim(Claims.Tenant, s.TenantId.ToString()),
                new Claim(Claims.Branch, s.BranchId.ToString()),
                new Claim(Claims.Role, s.Role.ToString()),
            ],
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(options.SigningKey(), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public (string Token, DateTimeOffset ExpiresAt) IssuePlatform(PlatformSession s)
    {
        var expires = DateTimeOffset.UtcNow.AddHours(8);
        return (Write([new Claim(Claims.User, s.UserId.ToString()), new Claim(Claims.Scope, "platform")], expires), expires);
    }

    /// <summary>Read-only owner-level view of one tenant for provider support; expires with the owner's grant (max 2 h).</summary>
    public (string Token, DateTimeOffset ExpiresAt) IssueSupport(Guid platformUserId, SupportSession s) =>
        (Write([
            new Claim(Claims.User, platformUserId.ToString()), new Claim(Claims.Tenant, s.TenantId.ToString()), new Claim(Claims.Branch, s.BranchId.ToString()),
            new Claim(Claims.Role, UserRole.Owner.ToString()), new Claim(Claims.Support, platformUserId.ToString()),
        ], s.ExpiresAt), s.ExpiresAt);

    private string Write(IEnumerable<Claim> claims, DateTimeOffset expires) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        options.Issuer, options.Audience, claims, expires: expires.UtcDateTime,
        signingCredentials: new SigningCredentials(options.SigningKey(), SecurityAlgorithms.HmacSha256)));
}

/// <summary>Tenant/user/branch for the current request, taken only from the validated token.</summary>
public class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;
    private Guid Read(string type) => Guid.TryParse(User?.FindFirst(type)?.Value, out var g) ? g : Guid.Empty;

    public Guid TenantId => Read(Claims.Tenant);
    public Guid UserId => Read(Claims.User);
    public Guid BranchId => Read(Claims.Branch);
    public UserRole Role => Enum.TryParse<UserRole>(User?.FindFirst(Claims.Role)?.Value, out var r) ? r : 0;
}
