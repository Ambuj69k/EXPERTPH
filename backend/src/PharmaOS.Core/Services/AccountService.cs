using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;

namespace PharmaOS.Core.Services;

public record RegisterRequest(string BusinessName, string BranchName, string BranchCode, string StateCode,
    string? Gstin, string? DrugLicenseNo, string OwnerName, string Email, string Password);

public record LoginRequest(string Email, string Password);

public record UserDto(Guid Id, string Email, string DisplayName, UserRole Role, Guid DefaultBranchId, bool IsActive);

public record CreateUserRequest(string Email, string DisplayName, string Password, UserRole Role, Guid? BranchId);

public record SessionInfo(Guid TenantId, string TenantName, Guid UserId, string DisplayName, string Email,
    UserRole Role, Guid BranchId, string BranchName, string BranchCode);

public record LoginOutcome(SessionInfo? Session, Guid? MfaUserId);
public record MfaSetup(string Secret, string OtpAuthUri);
public record CodeRequest(string Code);
public record PasswordChange(string CurrentPassword, string NewPassword);
public record PasswordReset(string NewPassword);

public partial class AccountService(PharmaDbContext db, ITenantContext ctx, IClock clock, AuditService audit, SubscriptionService subscription,
    ISecretProtector protector)
{
    private static readonly PasswordHasher<AppUser> Hasher = new();

    /// <summary>Self-service onboarding: creates tenant, first branch and owner in one transaction.</summary>
    public async Task<SessionInfo> RegisterAsync(RegisterRequest r, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(r.BusinessName), "Business name is required.");
        Require(!string.IsNullOrWhiteSpace(r.BranchName), "Branch name is required.");
        Require(BranchCodeRx().IsMatch(r.BranchCode ?? ""), "Branch code must be 1-4 letters/digits.");
        Require(Regex.IsMatch(r.StateCode ?? "", "^[0-9]{2}$"), "State code must be 2 digits.");
        Require(r.Gstin is null or "" || GstinRx().IsMatch(r.Gstin.ToUpperInvariant()), "GSTIN format looks invalid.");
        ValidateEmailAndPassword(r.Email, r.Password);

        var email = r.Email.Trim().ToLowerInvariant();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw BusinessException.Conflict("email_taken", "An account with this email already exists.");

        var now = clock.UtcNow;
        var tenant = new Tenant { Name = r.BusinessName.Trim(), CreatedAt = now };
        var branch = new Branch
        {
            TenantId = tenant.Id, Name = r.BranchName.Trim(), Code = r.BranchCode!.ToUpperInvariant(),
            StateCode = r.StateCode!, Gstin = string.IsNullOrWhiteSpace(r.Gstin) ? null : r.Gstin.ToUpperInvariant(),
            DrugLicenseNo = r.DrugLicenseNo,
        };
        var user = new AppUser
        {
            TenantId = tenant.Id, Email = email, DisplayName = r.OwnerName.Trim(), Role = UserRole.Owner,
            DefaultBranchId = branch.Id, CreatedAt = now,
        };
        user.PasswordHash = Hasher.HashPassword(user, r.Password);

        db.Tenants.Add(tenant);
        db.Branches.Add(branch);
        db.Users.Add(user);
        db.Subscriptions.Add(new Subscription
        {
            TenantId = tenant.Id, PlanCode = Plans.TrialPlan, Status = SubscriptionStatus.Trial, TrialEndsAt = now.AddDays(Plans.TrialDays), UpdatedAt = now,
        });
        db.AuditEvents.Add(new AuditEvent
        {
            TenantId = tenant.Id, UserId = user.Id, Action = "tenant.registered", EntityType = nameof(Tenant),
            EntityId = tenant.Id, Summary = tenant.Name, At = now,
        });
        await db.SaveChangesAsync(ct);
        return new SessionInfo(tenant.Id, tenant.Name, user.Id, user.DisplayName, user.Email, user.Role,
            branch.Id, branch.Name, branch.Code);
    }

    private const int MaxFailures = 5;
    private static readonly TimeSpan LockoutFor = TimeSpan.FromMinutes(15);

    /// <summary>Password step. Returns a session, or — when MFA is on — the user id that must complete the code step.</summary>
    public async Task<LoginOutcome> LoginAsync(LoginRequest r, CancellationToken ct = default)
    {
        var email = (r.Email ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == email, ct);
        var invalid = new BusinessException("invalid_credentials", "Email or password is incorrect.", 401);
        if (user is null || !user.IsActive) throw invalid;
        EnsureNotLocked(user.LockedUntil);
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, r.Password ?? "") == PasswordVerificationResult.Failed)
        {
            await RegisterFailureAsync(user, ct);
            throw invalid;
        }
        if (user.MfaEnabled) return new LoginOutcome(null, user.Id);
        return new LoginOutcome(await CompleteLoginAsync(user, ct), null);
    }

    /// <summary>Second step for MFA users: the 6-digit code from their authenticator app.</summary>
    public async Task<SessionInfo> CompleteMfaAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive && u.MfaEnabled, ct)
                   ?? throw new BusinessException("invalid_credentials", "Sign in again.", 401);
        EnsureNotLocked(user.LockedUntil);
        if (!Totp.Verify(protector.Unprotect(user.MfaSecret!), code, clock.UtcNow))
        {
            await RegisterFailureAsync(user, ct);
            throw new BusinessException("invalid_code", "That code is not correct. Check your authenticator app.", 401);
        }
        return await CompleteLoginAsync(user, ct);
    }

    private void EnsureNotLocked(DateTimeOffset? lockedUntil)
    {
        if (lockedUntil is { } l && l > clock.UtcNow)
            throw new BusinessException("locked", $"Too many failed attempts. Try again after {l.ToOffset(TimeSpan.FromHours(5.5)):HH:mm}.", 423);
    }

    private async Task RegisterFailureAsync(AppUser user, CancellationToken ct)
    {
        user.FailedLogins++;
        if (user.FailedLogins >= MaxFailures) { user.LockedUntil = clock.UtcNow.Add(LockoutFor); user.FailedLogins = 0; }
        db.AuditEvents.Add(new AuditEvent { TenantId = user.TenantId, UserId = user.Id, Action = user.LockedUntil > clock.UtcNow ? "auth.locked" : "auth.failed", EntityType = nameof(AppUser), EntityId = user.Id, At = clock.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    private async Task<SessionInfo> CompleteLoginAsync(AppUser user, CancellationToken ct)
    {
        user.FailedLogins = 0;
        user.LockedUntil = null;
        await db.SaveChangesAsync(ct);

        var tenant = await db.Tenants.SingleAsync(t => t.Id == user.TenantId, ct);
        if (!tenant.IsActive) throw BusinessException.Forbidden("This account is suspended.");
        var branch = await db.Branches.IgnoreQueryFilters().SingleAsync(b => b.Id == user.DefaultBranchId, ct);
        return new SessionInfo(tenant.Id, tenant.Name, user.Id, user.DisplayName, user.Email, user.Role,
            branch.Id, branch.Name, branch.Code);
    }

    // ---------- MFA & passwords (current user) ----------

    public async Task<MfaSetup> BeginMfaSetupAsync(CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(u => u.Id == ctx.UserId, ct);
        if (user.MfaEnabled) throw BusinessException.Conflict("mfa_on", "Two-step sign-in is already on. Turn it off first to re-enrol.");
        var secret = Totp.NewSecret();
        user.MfaSecret = protector.Protect(secret);
        await db.SaveChangesAsync(ct);
        return new MfaSetup(Totp.Base32(secret), Totp.Uri("PharmaOS", user.Email, secret));
    }

    public async Task EnableMfaAsync(string code, CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(u => u.Id == ctx.UserId, ct);
        Require(user.MfaSecret is not null && !user.MfaEnabled, "Start setup first.");
        if (!Totp.Verify(protector.Unprotect(user.MfaSecret!), code, clock.UtcNow))
            throw BusinessException.Validation("That code is not correct — make sure the phone's time is automatic and try the newest code.");
        user.MfaEnabled = true;
        audit.Record("auth.mfa_enabled", nameof(AppUser), user.Id, null);
        await db.SaveChangesAsync(ct);
    }

    public async Task DisableMfaAsync(string password, CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(u => u.Id == ctx.UserId, ct);
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "") == PasswordVerificationResult.Failed)
            throw new BusinessException("invalid_credentials", "Password is incorrect.", 401);
        user.MfaEnabled = false;
        user.MfaSecret = null;
        audit.Record("auth.mfa_disabled", nameof(AppUser), user.Id, null);
        await db.SaveChangesAsync(ct);
    }

    public async Task ChangePasswordAsync(PasswordChange r, CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(u => u.Id == ctx.UserId, ct);
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, r.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
            throw new BusinessException("invalid_credentials", "Current password is incorrect.", 401);
        Require(r.NewPassword is { Length: >= 8 } && r.NewPassword != r.CurrentPassword, "New password must be at least 8 characters and different from the current one.");
        user.PasswordHash = Hasher.HashPassword(user, r.NewPassword);
        audit.Record("auth.password_changed", nameof(AppUser), user.Id, null);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Owner sets a temporary password for a staff member (also clears a lockout). Owners reset their own via change-password.</summary>
    public async Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default)
    {
        Require(newPassword is { Length: >= 8 }, "Password must be at least 8 characters.");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw BusinessException.NotFound("User");
        Require(user.Id != ctx.UserId, "Use change password for your own account.");
        user.PasswordHash = Hasher.HashPassword(user, newPassword);
        user.FailedLogins = 0;
        user.LockedUntil = null;
        audit.Record("auth.password_reset", nameof(AppUser), user.Id, user.Email);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> MfaEnabledAsync(CancellationToken ct = default) =>
        await db.Users.Where(u => u.Id == ctx.UserId).Select(u => u.MfaEnabled).SingleOrDefaultAsync(ct);

    public async Task<SessionInfo> GetSessionAsync(CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == ctx.UserId && u.IsActive, ct)
                   ?? throw new BusinessException("unauthorized", "Session is no longer valid.", 401);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == ctx.TenantId, ct);
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        return new SessionInfo(tenant.Id, tenant.Name, user.Id, user.DisplayName, user.Email, user.Role,
            branch.Id, branch.Name, branch.Code);
    }

    /// <summary>Session shown during a provider support session (the viewer is not a tenant user).</summary>
    public async Task<SessionInfo> GetSupportSessionAsync(CancellationToken ct = default)
    {
        var tenant = await db.Tenants.SingleAsync(t => t.Id == ctx.TenantId, ct);
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        return new SessionInfo(tenant.Id, tenant.Name, ctx.UserId, "PharmaOS support (read-only)", "support", UserRole.Owner, branch.Id, branch.Name, branch.Code);
    }

    /// <summary>Owner/Accountant may work in any active branch; other staff only in their assigned branch.</summary>
    public async Task<SessionInfo> SwitchBranchAsync(Guid branchId, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == ctx.UserId && u.IsActive, ct)
                   ?? throw new BusinessException("unauthorized", "Session is no longer valid.", 401);
        var branch = await db.Branches.SingleOrDefaultAsync(b => b.Id == branchId && b.IsActive, ct) ?? throw BusinessException.NotFound("Branch");
        if (user.Role is not (UserRole.Owner or UserRole.Accountant) && user.DefaultBranchId != branchId)
            throw BusinessException.Forbidden("You can only work in your assigned branch.");
        var tenant = await db.Tenants.SingleAsync(t => t.Id == ctx.TenantId, ct);
        audit.Record("session.branch_switched", nameof(Branch), branch.Id, branch.Code);
        await db.SaveChangesAsync(ct);
        return new SessionInfo(tenant.Id, tenant.Name, user.Id, user.DisplayName, user.Email, user.Role, branch.Id, branch.Name, branch.Code);
    }

    public async Task<List<UserDto>> ListUsersAsync(CancellationToken ct = default) =>
        await db.Users.OrderBy(u => u.DisplayName)
            .Select(u => new UserDto(u.Id, u.Email, u.DisplayName, u.Role, u.DefaultBranchId, u.IsActive))
            .ToListAsync(ct);

    public async Task<UserDto> CreateUserAsync(CreateUserRequest r, CancellationToken ct = default)
    {
        ValidateEmailAndPassword(r.Email, r.Password);
        Require(!string.IsNullOrWhiteSpace(r.DisplayName), "Name is required.");
        Require(Enum.IsDefined(r.Role), "Invalid role.");
        var email = r.Email.Trim().ToLowerInvariant();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw BusinessException.Conflict("email_taken", "An account with this email already exists.");
        await subscription.EnsureCanAddUserAsync(ct);
        var branchId = r.BranchId ?? ctx.BranchId;
        if (!await db.Branches.AnyAsync(b => b.Id == branchId, ct)) throw BusinessException.NotFound("Branch");

        var user = new AppUser
        {
            Email = email, DisplayName = r.DisplayName.Trim(), Role = r.Role, DefaultBranchId = branchId,
            CreatedAt = clock.UtcNow,
        };
        user.PasswordHash = Hasher.HashPassword(user, r.Password);
        db.Users.Add(user);
        audit.Record("user.created", nameof(AppUser), user.Id, $"{email} as {r.Role}");
        await db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Email, user.DisplayName, user.Role, user.DefaultBranchId, user.IsActive);
    }

    public async Task SetUserActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw BusinessException.NotFound("User");
        if (user.Id == ctx.UserId && !active) throw BusinessException.Validation("You cannot deactivate yourself.");
        user.IsActive = active;
        audit.Record(active ? "user.activated" : "user.deactivated", nameof(AppUser), id, user.Email);
        await db.SaveChangesAsync(ct);
    }

    private static void ValidateEmailAndPassword(string? email, string? password)
    {
        Require(!string.IsNullOrWhiteSpace(email) && email.Contains('@') && email.Length <= 256, "A valid email is required.");
        Require(password is { Length: >= 8 }, "Password must be at least 8 characters.");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw BusinessException.Validation(message);
    }

    [GeneratedRegex("^[A-Za-z0-9]{1,4}$")] private static partial Regex BranchCodeRx();
    [GeneratedRegex("^[0-9]{2}[A-Z0-9]{10}[0-9A-Z]{3}$")] private static partial Regex GstinRx();
}
