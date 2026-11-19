using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record DeviationInput(Guid? ProductionBatchId, string Title, string Description, DeviationSeverity Severity);
public record InvestigationInput(string RootCause, string CorrectiveAction, string? PreventiveAction, Guid? CapaOwnerUserId, DateOnly? CapaDueDate, int Version);
public record DeviationDto(Guid Id, string DeviationNo, Guid? ProductionBatchId, string? BatchNo, string Title, string Description, DeviationSeverity Severity,
    DeviationStatus Status, string? RootCause, string? CorrectiveAction, string? PreventiveAction, string? CapaOwner, DateOnly? CapaDueDate, bool Overdue,
    string RaisedBy, DateTimeOffset RaisedAt, string? ClosureSignature, DateTimeOffset? ClosedAt, int Version);

public partial class ManufacturingService
{
    public async Task<DeviationDto> RaiseDeviationAsync(DeviationInput i, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(i.Title) && !string.IsNullOrWhiteSpace(i.Description), "Title and description are required.");
        Require(Enum.IsDefined(i.Severity), "Invalid severity.");
        if (i.ProductionBatchId is { } pb && !await db.ProductionBatches.AnyAsync(x => x.Id == pb, ct)) throw BusinessException.NotFound("Production batch");
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        var d = new Deviation
        {
            ProductionBatchId = i.ProductionBatchId, Title = i.Title.Trim(), Description = i.Description.Trim(), Severity = i.Severity,
            RaisedByUserId = ctx.UserId, RaisedAt = clock.UtcNow,
            DeviationNo = SequenceService.Format(branch.Code, "E", Today, await sequences.NextAsync(branch.Id, "E", Today, ct)),
        };
        db.Deviations.Add(d);
        audit.Record("deviation.raised", nameof(Deviation), d.Id, $"{d.DeviationNo} {d.Severity}: {d.Title}");
        await SaveAsync(ct);
        return await DeviationDtoAsync(d.Id, ct);
    }

    public async Task<DeviationDto> InvestigateAsync(Guid id, InvestigationInput i, CancellationToken ct = default)
    {
        var d = await db.Deviations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Deviation");
        if (d.Status == DeviationStatus.Closed) throw BusinessException.Conflict("invalid_state", "This deviation is closed.");
        Require(!string.IsNullOrWhiteSpace(i.RootCause) && !string.IsNullOrWhiteSpace(i.CorrectiveAction), "Root cause and corrective action are required.");
        if (i.CapaOwnerUserId is { } o && !await db.Users.AnyAsync(u => u.Id == o && u.IsActive, ct)) throw BusinessException.NotFound("CAPA owner");
        db.Entry(d).Property(x => x.Version).OriginalValue = i.Version;
        d.RootCause = i.RootCause.Trim(); d.CorrectiveAction = i.CorrectiveAction.Trim(); d.PreventiveAction = i.PreventiveAction?.Trim();
        d.CapaOwnerUserId = i.CapaOwnerUserId; d.CapaDueDate = i.CapaDueDate; d.Status = DeviationStatus.CapaInProgress;
        audit.Record("deviation.investigated", nameof(Deviation), d.Id, d.DeviationNo);
        await SaveAsync(ct);
        return await DeviationDtoAsync(d.Id, ct);
    }

    /// <summary>QA closes the deviation with an e-signature once CAPA is in place.</summary>
    public async Task<DeviationDto> CloseDeviationAsync(Guid id, SignatureInput s, CancellationToken ct = default)
    {
        var d = await db.Deviations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Deviation");
        if (d.Status != DeviationStatus.CapaInProgress) throw BusinessException.Conflict("invalid_state", "Record the investigation and CAPA before closing.");
        var signer = await accounts.VerifySignatureAsync(s.Password, ct);
        d.Status = DeviationStatus.Closed; d.ClosedAt = clock.UtcNow; d.ClosureSignature = $"Closed by {signer} (QA){(string.IsNullOrWhiteSpace(s.Note) ? "" : $" — {s.Note.Trim()}")}";
        audit.Record("deviation.closed", nameof(Deviation), d.Id, d.ClosureSignature);
        await SaveAsync(ct);
        return await DeviationDtoAsync(d.Id, ct);
    }

    public async Task<List<DeviationDto>> DeviationsAsync(DeviationStatus? status, Guid? productionBatchId, CancellationToken ct = default)
    {
        var q = db.Deviations.AsNoTracking();
        if (status is { } st) q = q.Where(x => x.Status == st);
        if (productionBatchId is { } pb) q = q.Where(x => x.ProductionBatchId == pb);
        var ids = await q.OrderByDescending(x => x.RaisedAt).Select(x => x.Id).Take(300).ToListAsync(ct);
        var list = new List<DeviationDto>();
        foreach (var x in ids) list.Add(await DeviationDtoAsync(x, ct));
        return list;
    }

    /// <summary>Open Major/Critical deviations on a batch block its release.</summary>
    private Task<bool> HasBlockingDeviationAsync(Guid productionBatchId, CancellationToken ct) =>
        db.Deviations.AnyAsync(x => x.ProductionBatchId == productionBatchId && x.Status != DeviationStatus.Closed && x.Severity != DeviationSeverity.Minor, ct);

    private async Task<DeviationDto> DeviationDtoAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Deviations.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var batchNo = d.ProductionBatchId is { } pb ? await db.ProductionBatches.Where(x => x.Id == pb).Select(x => x.BatchNo).FirstOrDefaultAsync(ct) : null;
        var userIds = new[] { d.RaisedByUserId }.Concat(d.CapaOwnerUserId is { } o ? new[] { o } : []).ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return new DeviationDto(d.Id, d.DeviationNo, d.ProductionBatchId, batchNo, d.Title, d.Description, d.Severity, d.Status, d.RootCause, d.CorrectiveAction,
            d.PreventiveAction, d.CapaOwnerUserId is { } ow ? names.GetValueOrDefault(ow) : null, d.CapaDueDate,
            d.Status != DeviationStatus.Closed && d.CapaDueDate < Today, names.GetValueOrDefault(d.RaisedByUserId, ""), d.RaisedAt, d.ClosureSignature, d.ClosedAt, d.Version);
    }
}
