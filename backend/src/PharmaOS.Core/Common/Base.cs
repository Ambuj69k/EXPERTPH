namespace PharmaOS.Core.Common;

/// <summary>Base for every persisted entity. Ids are time-ordered GUIDs (v7) so they index well.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>Anything that belongs to exactly one customer tenant. Enforced by global query filters + SaveChanges guard.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

public abstract class TenantEntity : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
}

/// <summary>Rows that must never be updated or deleted once written (stock ledger, audit log).</summary>
public interface IImmutable { }

/// <summary>Optimistic concurrency: Version is checked on update and incremented automatically.</summary>
public interface IVersioned
{
    int Version { get; set; }
}

/// <summary>Current request's identity. Empty TenantId = anonymous / system context.</summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    Guid UserId { get; }
    Guid BranchId { get; }
    UserRole Role { get; }
}

public sealed class FixedTenantContext : ITenantContext
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid BranchId { get; set; }
    public UserRole Role { get; set; } = UserRole.Owner;
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Expected business-rule failure. Mapped to RFC7807 problem responses by the API.</summary>
public class BusinessException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;

    public static BusinessException NotFound(string what) => new("not_found", $"{what} not found.", 404);
    public static BusinessException Validation(string message) => new("validation", message, 400);
    public static BusinessException Conflict(string code, string message) => new(code, message, 409);
    public static BusinessException Forbidden(string message) => new("forbidden", message, 403);
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
