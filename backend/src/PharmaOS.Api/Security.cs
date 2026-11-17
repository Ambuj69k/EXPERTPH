using Microsoft.AspNetCore.DataProtection;
using PharmaOS.Core.Common;

namespace PharmaOS.Api;

/// <summary>MFA seeds encrypted with ASP.NET Data Protection (keys persisted under Security:KeysPath — keep on a data drive and back up).</summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _p = provider.CreateProtector("PharmaOS.Mfa.v1");
    public string Protect(byte[] secret) => _p.Protect(Convert.ToBase64String(secret));
    public byte[] Unprotect(string protectedValue) => Convert.FromBase64String(_p.Unprotect(protectedValue));
}

/// <summary>Short-lived token proving the password step passed, exchanged for a session after the MFA code.</summary>
public sealed class MfaChallenge(IDataProtectionProvider provider)
{
    private readonly ITimeLimitedDataProtector _p = provider.CreateProtector("PharmaOS.MfaChallenge.v1").ToTimeLimitedDataProtector();
    public string Issue(Guid userId, string audience) => _p.Protect($"{audience}:{userId}", TimeSpan.FromMinutes(5));

    public Guid Read(string token, string audience)
    {
        try
        {
            var v = _p.Unprotect(token);
            if (v.StartsWith(audience + ":") && Guid.TryParse(v[(audience.Length + 1)..], out var id)) return id;
        }
        catch (System.Security.Cryptography.CryptographicException) { }
        throw new BusinessException("mfa_expired", "Sign-in step expired. Enter your password again.", 401);
    }
}

public record MfaLoginRequest(string MfaToken, string Code);
public record MfaPending(bool MfaRequired, string MfaToken);
public record DisableMfaRequest(string Password);

/// <summary>Conservative headers for an API that only returns JSON/files.</summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        var h = http.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        h["Cache-Control"] = "no-store";
        return next(http);
    }
}
