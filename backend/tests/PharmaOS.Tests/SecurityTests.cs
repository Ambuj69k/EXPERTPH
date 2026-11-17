using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PharmaOS.Core.Common;

namespace PharmaOS.Tests;

/// <summary>Account security: lockout, owner password reset, password change, TOTP two-step sign-in, API headers.</summary>
[Collection("api")]
public class SecurityTests(ApiFixture f)
{
    private async Task<HttpResponseMessage> Login(string email, string password) =>
        await f.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password }, ApiFixture.Json);

    [Fact]
    public async Task Lockout_after_five_failures_and_owner_reset()
    {
        var owner = await f.NewTenantAsync();
        var cashier = await owner.AddUserAsync("Cashier");
        var email = (await cashier.Get("/api/v1/me")).GetProperty("email").GetString()!;
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Login(email, "wrong-pass")).StatusCode);
        Assert.Equal((HttpStatusCode)423, (await Login(email, "Password@123")).StatusCode); // correct password still refused while locked

        var id = (await owner.Get("/api/v1/users")).EnumerateArray().Single(u => u.GetProperty("email").GetString() == email).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Post, $"/api/v1/users/{id}/reset-password", new { newPassword = "x" })).Status);
        await owner.Post($"/api/v1/users/{id}/reset-password", new { newPassword = "Temp@54321" });
        Assert.Equal(HttpStatusCode.OK, (await Login(email, "Temp@54321")).StatusCode);
    }

    [Fact]
    public async Task Change_password_and_two_step_sign_in()
    {
        var c = await f.NewTenantAsync();
        var email = (await c.Get("/api/v1/me")).GetProperty("email").GetString()!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Try(HttpMethod.Post, "/api/v1/me/password", new { currentPassword = "nope", newPassword = "Better@2026" })).Status);
        await c.Post("/api/v1/me/password", new { currentPassword = "Password@123", newPassword = "Better@2026" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(email, "Password@123")).StatusCode);

        var setup = await c.Post("/api/v1/me/mfa/setup", new { });
        var secret = Totp.FromBase32(setup.GetProperty("secret").GetString()!);
        Assert.StartsWith("otpauth://totp/PharmaOS:", setup.GetProperty("otpAuthUri").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, "/api/v1/me/mfa/enable", new { code = "000000" })).Status);
        await c.Post("/api/v1/me/mfa/enable", new { code = Totp.Code(secret, DateTimeOffset.UtcNow) });

        var step1 = await (await Login(email, "Better@2026")).Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json);
        Assert.True(step1.GetProperty("mfaRequired").GetBoolean());
        Assert.False(step1.TryGetProperty("token", out _)); // no session before the code
        var mfaToken = step1.GetProperty("mfaToken").GetString();
        var anon = f.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/v1/auth/mfa", new { mfaToken, code = "123456" }, ApiFixture.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/v1/auth/mfa", new { mfaToken = "forged", code = Totp.Code(secret, DateTimeOffset.UtcNow) }, ApiFixture.Json)).StatusCode);
        var ok = await anon.PostAsJsonAsync("/api/v1/auth/mfa", new { mfaToken, code = Totp.Code(secret, DateTimeOffset.UtcNow) }, ApiFixture.Json);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.False(string.IsNullOrEmpty((await ok.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json)).GetProperty("token").GetString()));

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Try(HttpMethod.Post, "/api/v1/me/mfa/disable", new { password = "wrong" })).Status);
        await c.Post("/api/v1/me/mfa/disable", new { password = "Better@2026" });
        Assert.True((await (await Login(email, "Better@2026")).Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json)).TryGetProperty("token", out _));
    }

    [Fact]
    public void Totp_matches_rfc6238_reference_vector()
    {
        // RFC 6238 Appendix B, SHA-1 secret "12345678901234567890", T = 59 s → 94287082 (8 digits) → 287082 (6 digits)
        var secret = "12345678901234567890"u8.ToArray();
        Assert.Equal("287082", Totp.Code(secret, DateTimeOffset.FromUnixTimeSeconds(59)));
        Assert.Equal(secret, Totp.FromBase32(Totp.Base32(secret)));
    }

    [Fact]
    public async Task Api_responses_carry_security_headers()
    {
        var res = await f.Factory.CreateClient().GetAsync("/health");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
    }
}
