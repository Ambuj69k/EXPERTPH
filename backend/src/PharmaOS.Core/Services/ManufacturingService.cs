using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

// ---------- contracts ----------
public record RecipeLineInput(Guid MaterialProductId, decimal QtyUnits);
public record RecipeInput(Guid ProductId, decimal BatchSizeUnits, string? Notes, List<RecipeLineInput> Lines);
public record RecipeDto(Guid Id, Guid ProductId, string ProductName, int VersionNo, decimal BatchSizeUnits, MfgDocStatus Status, string? Notes, string? ApprovedBy,
    DateTimeOffset? ApprovedAt, List<RecipeLineDto> Lines);
public record RecipeLineDto(Guid MaterialProductId, string MaterialName, string BaseUnit, decimal QtyUnits);
public record SpecTestInput(string Name, string? Unit, decimal? Min, decimal? Max, string? Requirement);
public record SpecInput(Guid ProductId, List<SpecTestInput> Tests);
public record SpecDto(Guid Id, Guid ProductId, string ProductName, int VersionNo, MfgDocStatus Status, string? ApprovedBy, List<SpecTestDto> Tests);
public record SpecTestDto(Guid Id, int Seq, string Name, string? Unit, decimal? Min, decimal? Max, string? Requirement);
public record SignatureInput(string Password, string? Note);
public record ProductionInput(Guid RecipeId, string BatchNo, DateOnly ExpiryDate, decimal Mrp, decimal PlannedUnits);
public record OutputInput(decimal OutputUnits);
public record ConsumptionDto(Guid MaterialProductId, string MaterialName, Guid BatchId, string BatchNo, DateOnly ExpiryDate, decimal QtyUnits, decimal CostPerUnit);
public record ProductionDto(Guid Id, string BatchNo, Guid ProductId, string ProductName, int RecipeVersion, DateOnly MfgDate, DateOnly ExpiryDate, decimal Mrp,
    decimal PlannedUnits, ProductionStatus Status, decimal MaterialCost, decimal OutputUnits, decimal? YieldPercent, Guid? OutputBatchId, string? CreatedBy,
    DateTimeOffset CreatedAt, string? DecisionSignature, DateTimeOffset? DecisionAt, string? DecisionNote, int Version, List<ConsumptionDto> Materials,
    List<QcSampleDto> Samples);
public record QcSampleInput(Guid BatchId);
public record QcResultInput(Guid QcSpecTestId, decimal? Value, string? Text, bool? Pass);
public record QcResultsInput(List<QcResultInput> Results, string Password);
public record QcResultDto(Guid TestId, string TestName, string? Unit, decimal? Min, decimal? Max, string? Requirement, decimal? Value, string? Text, bool Pass);
public record QcSampleDto(Guid Id, string SampleNo, Guid BatchId, string ProductName, string BatchNo, int SpecVersion, QcSampleStatus Status, string? TesterSignature,
    DateTimeOffset? TestedAt, DateTimeOffset CreatedAt, List<SpecTestDto> Tests, List<QcResultDto> Results);
public record GenealogyNode(Guid BatchId, string ProductName, string BatchNo, DateOnly ExpiryDate, decimal QtyUnits, string? ProductionStatus, Guid? ProductionBatchId);
public record Genealogy(Guid BatchId, string ProductName, string BatchNo, bool IsBlocked, string? BlockReason, List<GenealogyNode> MadeFrom, List<GenealogyNode> UsedIn,
    List<QcSampleDto> Samples);

/// <summary>
/// Minimal manufacturing &amp; QA. Separation of duties: the analyst who signs QC results cannot release that batch; release and
/// result entry need an electronic signature (password re-entry). Material lots of products that have an approved spec must
/// pass QC before use. Output stays quarantined (blocked) until QA releases it.
/// This is a pilot slice and is NOT validated for GMP decisions (no IQ/OQ/PQ, document control or training records).
/// </summary>
public partial class ManufacturingService(PharmaDbContext db, ITenantContext ctx, IClock clock, InventoryService inventory, SequenceService sequences,
    AuditService audit, AccountService accounts)
{
    private DateOnly Today => BusinessCalendar.LocalDate(clock.UtcNow);
    private const string QuarantineReason = "QC/QA quarantine — awaiting batch release";

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw BusinessException.Conflict("concurrency", "Changed by someone else at the same time. Reload."); }
    }

    // ===================== recipes =====================

    public async Task<List<RecipeDto>> RecipesAsync(CancellationToken ct = default)
    {
        var ids = await db.Recipes.AsNoTracking().OrderBy(r => r.Product!.Name).ThenByDescending(r => r.VersionNo).Select(r => r.Id).Take(300).ToListAsync(ct);
        var list = new List<RecipeDto>();
        foreach (var id in ids) list.Add(await RecipeDtoAsync(id, ct));
        return list;
    }

    public async Task<RecipeDto> CreateRecipeAsync(RecipeInput i, CancellationToken ct = default)
    {
        Require(i.BatchSizeUnits > 0, "Batch size must be positive.");
        Require(i.Lines.Count is > 0 and <= 100 && i.Lines.All(l => l.QtyUnits > 0), "Add materials with positive quantities.");
        Require(i.Lines.Select(l => l.MaterialProductId).Distinct().Count() == i.Lines.Count, "List each material once.");
        Require(i.Lines.All(l => l.MaterialProductId != i.ProductId), "A product cannot be its own material.");
        var ids = i.Lines.Select(l => l.MaterialProductId).Append(i.ProductId).Distinct().ToList();
        Require(await db.Products.CountAsync(p => ids.Contains(p.Id), ct) == ids.Count, "Unknown product.");
        var next = (await db.Recipes.Where(r => r.ProductId == i.ProductId).MaxAsync(r => (int?)r.VersionNo, ct) ?? 0) + 1;
        var r = new Recipe { ProductId = i.ProductId, VersionNo = next, BatchSizeUnits = i.BatchSizeUnits, Notes = i.Notes?.Trim() };
        r.Lines = i.Lines.Select(l => new RecipeLine { RecipeId = r.Id, MaterialProductId = l.MaterialProductId, QtyUnits = Math.Round(l.QtyUnits, 3) }).ToList();
        db.Recipes.Add(r);
        audit.Record("recipe.created", nameof(Recipe), r.Id, $"v{next}");
        await SaveAsync(ct);
        return await RecipeDtoAsync(r.Id, ct);
    }

    public async Task<RecipeDto> ApproveRecipeAsync(Guid id, SignatureInput s, CancellationToken ct = default)
    {
        var r = await db.Recipes.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Recipe");
        if (r.Status != MfgDocStatus.Draft) throw BusinessException.Conflict("invalid_state", "Only draft recipes can be approved.");
        var signer = await accounts.VerifySignatureAsync(s.Password, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var old in await db.Recipes.Where(x => x.ProductId == r.ProductId && x.Status == MfgDocStatus.Approved).ToListAsync(ct)) old.Status = MfgDocStatus.Retired;
        await SaveAsync(ct); // retire first: only one approved version may exist
        r.Status = MfgDocStatus.Approved; r.ApprovedByUserId = ctx.UserId; r.ApprovedAt = clock.UtcNow;
        audit.Record("recipe.approved", nameof(Recipe), r.Id, $"v{r.VersionNo} signed by {signer}");
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await RecipeDtoAsync(r.Id, ct);
    }

    private async Task<RecipeDto> RecipeDtoAsync(Guid id, CancellationToken ct)
    {
        var r = await db.Recipes.AsNoTracking().Include(x => x.Product).Include(x => x.Lines).ThenInclude(l => l.Material).SingleAsync(x => x.Id == id, ct);
        var by = r.ApprovedByUserId is { } u ? await db.Users.Where(x => x.Id == u).Select(x => x.DisplayName).FirstOrDefaultAsync(ct) : null;
        return new RecipeDto(r.Id, r.ProductId, r.Product!.Name, r.VersionNo, r.BatchSizeUnits, r.Status, r.Notes, by, r.ApprovedAt,
            r.Lines.OrderBy(l => l.Material!.Name).Select(l => new RecipeLineDto(l.MaterialProductId, l.Material!.Name, l.Material.BaseUnit, l.QtyUnits)).ToList());
    }

    // ===================== QC specifications =====================

    public async Task<List<SpecDto>> SpecsAsync(CancellationToken ct = default)
    {
        var specs = await db.QcSpecs.AsNoTracking().Include(s => s.Product).Include(s => s.Tests).OrderBy(s => s.Product!.Name).ThenByDescending(s => s.VersionNo).Take(300).ToListAsync(ct);
        var userIds = specs.Where(s => s.ApprovedByUserId != null).Select(s => s.ApprovedByUserId!.Value).Distinct().ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return specs.Select(s => SpecDto(s, s.ApprovedByUserId is { } a ? names.GetValueOrDefault(a) : null)).ToList();
    }

    private static SpecDto SpecDto(QcSpec s, string? by) => new(s.Id, s.ProductId, s.Product?.Name ?? "", s.VersionNo, s.Status, by, Tests(s));
    private static List<SpecTestDto> Tests(QcSpec s) => s.Tests.OrderBy(t => t.Seq).Select(t => new SpecTestDto(t.Id, t.Seq, t.Name, t.Unit, t.Min, t.Max, t.Requirement)).ToList();

    public async Task<SpecDto> CreateSpecAsync(SpecInput i, CancellationToken ct = default)
    {
        Require(await db.Products.AnyAsync(p => p.Id == i.ProductId, ct), "Unknown product.");
        Require(i.Tests.Count is > 0 and <= 50, "Add 1–50 tests.");
        Require(i.Tests.All(t => !string.IsNullOrWhiteSpace(t.Name)), "Every test needs a name.");
        Require(i.Tests.All(t => t.Min is null || t.Max is null || t.Min <= t.Max), "Min cannot be above max.");
        Require(i.Tests.All(t => t.Min is not null || t.Max is not null || !string.IsNullOrWhiteSpace(t.Requirement)), "Give numeric limits or a written requirement for each test.");
        var next = (await db.QcSpecs.Where(s => s.ProductId == i.ProductId).MaxAsync(s => (int?)s.VersionNo, ct) ?? 0) + 1;
        var spec = new QcSpec { ProductId = i.ProductId, VersionNo = next };
        spec.Tests = i.Tests.Select((t, n) => new QcSpecTest { QcSpecId = spec.Id, Seq = n + 1, Name = t.Name.Trim(), Unit = t.Unit?.Trim(), Min = t.Min, Max = t.Max, Requirement = t.Requirement?.Trim() }).ToList();
        db.QcSpecs.Add(spec);
        audit.Record("qc_spec.created", nameof(QcSpec), spec.Id, $"v{next}");
        await SaveAsync(ct);
        await db.Entry(spec).Reference(x => x.Product).LoadAsync(ct);
        return SpecDto(spec, null);
    }

    public async Task<SpecDto> ApproveSpecAsync(Guid id, SignatureInput s, CancellationToken ct = default)
    {
        var spec = await db.QcSpecs.Include(x => x.Tests).Include(x => x.Product).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Spec");
        if (spec.Status != MfgDocStatus.Draft) throw BusinessException.Conflict("invalid_state", "Only draft specs can be approved.");
        var signer = await accounts.VerifySignatureAsync(s.Password, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var old in await db.QcSpecs.Where(x => x.ProductId == spec.ProductId && x.Status == MfgDocStatus.Approved).ToListAsync(ct)) old.Status = MfgDocStatus.Retired;
        await SaveAsync(ct);
        spec.Status = MfgDocStatus.Approved; spec.ApprovedByUserId = ctx.UserId; spec.ApprovedAt = clock.UtcNow;
        audit.Record("qc_spec.approved", nameof(QcSpec), spec.Id, $"v{spec.VersionNo} signed by {signer}");
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return SpecDto(spec, signer);
    }

    // ===================== QC samples & results =====================

    public async Task<QcSampleDto> CreateSampleAsync(QcSampleInput i, CancellationToken ct = default)
    {
        var batch = await db.Batches.Include(b => b.Product).SingleOrDefaultAsync(b => b.Id == i.BatchId, ct) ?? throw BusinessException.NotFound("Batch");
        var spec = await db.QcSpecs.SingleOrDefaultAsync(s => s.ProductId == batch.ProductId && s.Status == MfgDocStatus.Approved, ct)
                   ?? throw BusinessException.Conflict("no_spec", $"{batch.Product!.Name} has no approved QC specification.");
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        var sample = new QcSample
        {
            BatchId = batch.Id, QcSpecId = spec.Id, CreatedAt = clock.UtcNow,
            SampleNo = SequenceService.Format(branch.Code, "Z", Today, await sequences.NextAsync(branch.Id, "Z", Today, ct)),
        };
        db.QcSamples.Add(sample);
        audit.Record("qc_sample.created", nameof(QcSample), sample.Id, $"{sample.SampleNo} {batch.BatchNo}");
        await SaveAsync(ct);
        return await SampleDtoAsync(sample.Id, ct);
    }

    public async Task<QcSampleDto> RecordResultsAsync(Guid id, QcResultsInput i, CancellationToken ct = default)
    {
        var sample = await db.QcSamples.Include(s => s.Spec).ThenInclude(s => s!.Tests).SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw BusinessException.NotFound("Sample");
        if (sample.Status != QcSampleStatus.Pending) throw BusinessException.Conflict("invalid_state", "Results were already signed. Take a new sample to retest.");
        var tests = sample.Spec!.Tests;
        Require(i.Results.Count == tests.Count && tests.All(t => i.Results.Count(r => r.QcSpecTestId == t.Id) == 1), "Enter one result for every test.");
        var signer = await accounts.VerifySignatureAsync(i.Password, ct);
        var outcomes = new List<bool>();
        foreach (var t in tests)
        {
            var r = i.Results.Single(x => x.QcSpecTestId == t.Id);
            bool pass;
            if (t.Min is not null || t.Max is not null)
            {
                Require(r.Value is not null, $"Enter a number for {t.Name}.");
                pass = (t.Min is null || r.Value >= t.Min) && (t.Max is null || r.Value <= t.Max); // limits decide, not the analyst
            }
            else
            {
                Require(r.Pass is not null && !string.IsNullOrWhiteSpace(r.Text), $"Describe the observation and mark pass/fail for {t.Name}.");
                pass = r.Pass!.Value;
            }
            db.QcResults.Add(new QcResult { QcSampleId = sample.Id, QcSpecTestId = t.Id, Value = r.Value, Text = r.Text?.Trim(), Pass = pass, RecordedAt = clock.UtcNow });
            outcomes.Add(pass);
        }
        sample.Status = outcomes.All(x => x) ? QcSampleStatus.Passed : QcSampleStatus.Failed;
        sample.TestedByUserId = ctx.UserId; sample.TestedAt = clock.UtcNow; sample.TesterSignature = $"Tested by {signer} (QC)";
        audit.Record("qc_sample.signed", nameof(QcSample), sample.Id, $"{sample.SampleNo}: {sample.Status}");
        await SaveAsync(ct);
        return await SampleDtoAsync(sample.Id, ct);
    }

    public async Task<List<QcSampleDto>> SamplesAsync(QcSampleStatus? status, CancellationToken ct = default)
    {
        var q = db.QcSamples.AsNoTracking();
        q = status is { } s ? q.Where(x => x.Status == s) : q.Where(x => x.CreatedAt >= clock.UtcNow.AddDays(-60) || x.Status == QcSampleStatus.Pending);
        var ids = await q.OrderByDescending(x => x.CreatedAt).Select(x => x.Id).Take(300).ToListAsync(ct);
        var list = new List<QcSampleDto>();
        foreach (var id in ids) list.Add(await SampleDtoAsync(id, ct));
        return list;
    }

    private async Task<QcSampleDto> SampleDtoAsync(Guid id, CancellationToken ct)
    {
        var s = await db.QcSamples.AsNoTracking().Include(x => x.Batch).ThenInclude(b => b!.Product).Include(x => x.Spec).ThenInclude(sp => sp!.Tests).SingleAsync(x => x.Id == id, ct);
        var results = await db.QcResults.AsNoTracking().Where(r => r.QcSampleId == id).ToListAsync(ct);
        var tests = Tests(s.Spec!);
        return new QcSampleDto(s.Id, s.SampleNo, s.BatchId, s.Batch!.Product!.Name, s.Batch.BatchNo, s.Spec!.VersionNo, s.Status, s.TesterSignature, s.TestedAt, s.CreatedAt, tests,
            results.Join(tests, r => r.QcSpecTestId, t => t.Id, (r, t) => new QcResultDto(t.Id, t.Name, t.Unit, t.Min, t.Max, t.Requirement, r.Value, r.Text, r.Pass))
                .OrderBy(r => tests.FindIndex(t => t.Id == r.TestId)).ToList());
    }

    // ===================== production batches =====================

    public async Task<ProductionDto> PlanBatchAsync(ProductionInput i, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.Include(r => r.Product).SingleOrDefaultAsync(r => r.Id == i.RecipeId, ct) ?? throw BusinessException.NotFound("Recipe");
        if (recipe.Status != MfgDocStatus.Approved) throw BusinessException.Conflict("recipe_not_approved", "Only the approved recipe version can be used.");
        Require(!string.IsNullOrWhiteSpace(i.BatchNo) && i.BatchNo.Trim().Length <= 30, "Batch number is required.");
        Require(i.PlannedUnits > 0 && i.Mrp > 0, "Planned quantity and MRP must be positive.");
        Require(i.ExpiryDate > Today, "Expiry must be in the future.");
        var batchNo = i.BatchNo.Trim().ToUpperInvariant();
        if (await db.Batches.AnyAsync(b => b.ProductId == recipe.ProductId && b.BatchNo == batchNo, ct)
            || await db.ProductionBatches.AnyAsync(p => p.ProductId == recipe.ProductId && p.BatchNo == batchNo && p.Status != ProductionStatus.Cancelled, ct))
            throw BusinessException.Conflict("duplicate_batch", $"Batch {batchNo} already exists for this product.");
        var pb = new ProductionBatch
        {
            BranchId = ctx.BranchId, RecipeId = recipe.Id, ProductId = recipe.ProductId, BatchNo = batchNo, MfgDate = Today, ExpiryDate = BusinessCalendar.NormalizeExpiry(i.ExpiryDate),
            Mrp = Money.R2(i.Mrp), PlannedUnits = i.PlannedUnits, CreatedByUserId = ctx.UserId, CreatedAt = clock.UtcNow,
        };
        db.ProductionBatches.Add(pb);
        audit.Record("production.planned", nameof(ProductionBatch), pb.Id, $"{recipe.Product!.Name} {batchNo} × {i.PlannedUnits}");
        await SaveAsync(ct);
        return await ProductionDtoAsync(pb.Id, ct);
    }

    /// <summary>Issues materials for the planned quantity: FEFO over usable lots (not blocked/expired; QC-passed where the material has a spec).</summary>
    public async Task<ProductionDto> IssueMaterialsAsync(Guid id, CancellationToken ct = default)
    {
        var pb = await db.ProductionBatches.Include(x => x.Recipe).ThenInclude(r => r!.Lines).ThenInclude(l => l.Material).SingleOrDefaultAsync(x => x.Id == id, ct)
                 ?? throw BusinessException.NotFound("Production batch");
        if (pb.Status != ProductionStatus.Planned) throw BusinessException.Conflict("invalid_state", "Materials were already issued.");
        var factor = pb.PlannedUnits / pb.Recipe!.BatchSizeUnits;
        var materialIds = pb.Recipe.Lines.Select(l => l.MaterialProductId).ToList();
        var withSpec = await db.QcSpecs.Where(s => materialIds.Contains(s.ProductId) && s.Status == MfgDocStatus.Approved).Select(s => s.ProductId).ToListAsync(ct);
        var today = Today;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var l in pb.Recipe.Lines)
        {
            var need = Math.Round(l.QtyUnits * factor, 3);
            var q = db.StockBalances.Where(s => s.BranchId == pb.BranchId && s.ProductId == l.MaterialProductId && s.QtyUnits > 0 && s.Batch!.ExpiryDate >= today && !s.Batch.IsBlocked);
            if (withSpec.Contains(l.MaterialProductId)) q = q.Where(s => db.QcSamples.Any(x => x.BatchId == s.BatchId && x.Status == QcSampleStatus.Passed));
            var lots = await q.OrderBy(s => s.Batch!.ExpiryDate).ThenBy(s => s.Batch!.BatchNo).Select(s => new { s.Batch, s.QtyUnits }).ToListAsync(ct);
            if (lots.Sum(x => x.QtyUnits) < need)
                throw BusinessException.Conflict("insufficient_material",
                    $"Only {lots.Sum(x => x.QtyUnits):0.###} {l.Material!.BaseUnit} of {l.Material.Name} usable (need {need:0.###}){(withSpec.Contains(l.MaterialProductId) ? " — lots must pass QC first" : "")}.");
            var left = need;
            foreach (var lot in lots)
            {
                if (left <= 0) break;
                var take = Math.Min(left, lot.QtyUnits); left -= take;
                var mc = new MaterialConsumption { ProductionBatchId = pb.Id, MaterialProductId = l.MaterialProductId, BatchId = lot.Batch!.Id, QtyUnits = take, CostPerUnit = lot.Batch.CostPerUnit };
                pb.Materials.Add(mc);
                db.MaterialConsumptions.Add(mc);
                await inventory.ApplyAsync(pb.BranchId, lot.Batch, -take, MovementType.ProductionConsume, nameof(ProductionBatch), pb.Id, mc.Id, $"for {pb.BatchNo}", ct);
                pb.MaterialCost += Money.R2(take * lot.Batch.CostPerUnit);
            }
        }
        pb.Status = ProductionStatus.InProcess; pb.MaterialsIssuedAt = clock.UtcNow;
        audit.Record("production.materials_issued", nameof(ProductionBatch), pb.Id, $"{pb.BatchNo} cost {pb.MaterialCost}");
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await ProductionDtoAsync(pb.Id, ct);
    }

    /// <summary>Books the output into stock as a quarantined (blocked) batch, valued at material cost.</summary>
    public async Task<ProductionDto> RecordOutputAsync(Guid id, OutputInput i, CancellationToken ct = default)
    {
        var pb = await db.ProductionBatches.Include(x => x.Product).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Production batch");
        if (pb.Status != ProductionStatus.InProcess) throw BusinessException.Conflict("invalid_state", "Issue materials before recording output.");
        Require(i.OutputUnits > 0 && i.OutputUnits == Math.Floor(i.OutputUnits), "Output must be whole units.");
        Require(i.OutputUnits <= pb.PlannedUnits * 1.1m, "Output is more than 110% of plan — check the count.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var costPerUnit = Math.Round(pb.MaterialCost / i.OutputUnits, 4);
        var batch = await inventory.GetOrCreateBatchAsync(pb.Product!, pb.BatchNo, pb.ExpiryDate, pb.Mrp, Money.R2(costPerUnit * pb.Product!.PackSize), costPerUnit, ct);
        batch.IsBlocked = true; batch.BlockReason = QuarantineReason;
        await inventory.ApplyAsync(pb.BranchId, batch, i.OutputUnits, MovementType.ProductionOutput, nameof(ProductionBatch), pb.Id, null, "production output (quarantine)", ct);
        pb.OutputUnits = i.OutputUnits; pb.OutputBatchId = batch.Id; pb.ProducedAt = clock.UtcNow; pb.Status = ProductionStatus.Quarantine;
        audit.Record("production.output", nameof(ProductionBatch), pb.Id, $"{pb.BatchNo}: {i.OutputUnits} units (yield {100m * i.OutputUnits / pb.PlannedUnits:0.0}%)");
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await ProductionDtoAsync(pb.Id, ct);
    }

    /// <summary>QA decision with e-signature. Release needs a passed QC sample on the output batch, signed by someone else.</summary>
    public async Task<ProductionDto> DecideAsync(Guid id, bool release, SignatureInput s, CancellationToken ct = default)
    {
        var pb = await db.ProductionBatches.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Production batch");
        if (pb.Status != ProductionStatus.Quarantine) throw BusinessException.Conflict("invalid_state", "Only quarantined batches can be released or rejected.");
        Require(release || !string.IsNullOrWhiteSpace(s.Note), "Give the reason for rejection.");
        var samples = await db.QcSamples.Where(x => x.BatchId == pb.OutputBatchId).ToListAsync(ct);
        if (release)
        {
            if (samples.Count == 0 || samples.Any(x => x.Status == QcSampleStatus.Pending) || samples.OrderByDescending(x => x.TestedAt).First().Status != QcSampleStatus.Passed)
                throw BusinessException.Conflict("qc_not_passed", "The batch needs a completed, passed QC result before release.");
            if (await HasBlockingDeviationAsync(pb.Id, ct))
                throw BusinessException.Conflict("open_deviation", "A Major/Critical deviation on this batch is still open. Close it (with CAPA) before release.");
            if (samples.Any(x => x.TestedByUserId == ctx.UserId))
                throw BusinessException.Conflict("same_person", "You signed QC results for this batch; another person must release it.");
        }
        var signer = await accounts.VerifySignatureAsync(s.Password, ct);
        var batch = await db.Batches.SingleAsync(b => b.Id == pb.OutputBatchId, ct);
        if (release) { batch.IsBlocked = false; batch.BlockReason = null; }
        else batch.BlockReason = $"Rejected by QA: {s.Note!.Trim()}";
        pb.Status = release ? ProductionStatus.Released : ProductionStatus.Rejected;
        pb.ReleasedByUserId = ctx.UserId; pb.DecisionAt = clock.UtcNow; pb.DecisionNote = s.Note?.Trim();
        pb.DecisionSignature = release ? $"Released for sale by {signer} (QA)" : $"Rejected by {signer} (QA)";
        audit.Record(release ? "production.released" : "production.rejected", nameof(ProductionBatch), pb.Id, pb.DecisionSignature);
        await SaveAsync(ct);
        return await ProductionDtoAsync(pb.Id, ct);
    }

    public async Task<ProductionDto> CancelBatchAsync(Guid id, CancellationToken ct = default)
    {
        var pb = await db.ProductionBatches.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Production batch");
        if (pb.Status != ProductionStatus.Planned) throw BusinessException.Conflict("invalid_state", "Only planned batches can be cancelled.");
        pb.Status = ProductionStatus.Cancelled;
        audit.Record("production.cancelled", nameof(ProductionBatch), pb.Id, pb.BatchNo);
        await SaveAsync(ct);
        return await ProductionDtoAsync(pb.Id, ct);
    }

    public async Task<List<ProductionDto>> BatchesAsync(ProductionStatus? status, CancellationToken ct = default)
    {
        var q = db.ProductionBatches.AsNoTracking();
        q = status is { } s ? q.Where(x => x.Status == s) : q.Where(x => x.CreatedAt >= clock.UtcNow.AddDays(-90) || x.Status == ProductionStatus.Quarantine || x.Status == ProductionStatus.InProcess);
        var ids = await q.OrderByDescending(x => x.CreatedAt).Select(x => x.Id).Take(200).ToListAsync(ct);
        var list = new List<ProductionDto>();
        foreach (var id in ids) list.Add(await ProductionDtoAsync(id, ct));
        return list;
    }

    public Task<ProductionDto> GetBatchAsync(Guid id, CancellationToken ct = default) => ProductionDtoAsync(id, ct);

    private async Task<ProductionDto> ProductionDtoAsync(Guid id, CancellationToken ct)
    {
        var p = await db.ProductionBatches.AsNoTracking().Include(x => x.Product).Include(x => x.Recipe).SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw BusinessException.NotFound("Production batch");
        var mats = await db.MaterialConsumptions.AsNoTracking().Where(m => m.ProductionBatchId == id).Include(m => m.Batch).ThenInclude(b => b!.Product).ToListAsync(ct);
        var samples = new List<QcSampleDto>();
        if (p.OutputBatchId is { } ob)
            foreach (var sid in await db.QcSamples.Where(x => x.BatchId == ob).OrderBy(x => x.CreatedAt).Select(x => x.Id).ToListAsync(ct)) samples.Add(await SampleDtoAsync(sid, ct));
        var by = p.CreatedByUserId is { } u ? await db.Users.Where(x => x.Id == u).Select(x => x.DisplayName).FirstOrDefaultAsync(ct) : null;
        return new ProductionDto(p.Id, p.BatchNo, p.ProductId, p.Product!.Name, p.Recipe!.VersionNo, p.MfgDate, p.ExpiryDate, p.Mrp, p.PlannedUnits, p.Status, p.MaterialCost,
            p.OutputUnits, p.OutputUnits > 0 ? Math.Round(100m * p.OutputUnits / p.PlannedUnits, 1) : null, p.OutputBatchId, by, p.CreatedAt, p.DecisionSignature, p.DecisionAt,
            p.DecisionNote, p.Version,
            mats.OrderBy(m => m.Batch!.Product!.Name).Select(m => new ConsumptionDto(m.MaterialProductId, m.Batch!.Product!.Name, m.BatchId, m.Batch.BatchNo, m.Batch.ExpiryDate, m.QtyUnits, m.CostPerUnit)).ToList(),
            samples);
    }

    // ===================== genealogy =====================

    /// <summary>Backward (what went into this batch) and forward (where this lot was used) — the basis for a recall.</summary>
    public async Task<Genealogy> GenealogyAsync(Guid batchId, CancellationToken ct = default)
    {
        var b = await db.Batches.AsNoTracking().Include(x => x.Product).SingleOrDefaultAsync(x => x.Id == batchId, ct) ?? throw BusinessException.NotFound("Batch");
        var producedBy = await db.ProductionBatches.AsNoTracking().Where(p => p.OutputBatchId == batchId).Select(p => p.Id).FirstOrDefaultAsync(ct);
        List<GenealogyNode> madeFrom = producedBy == Guid.Empty ? [] : await db.MaterialConsumptions.AsNoTracking().Where(m => m.ProductionBatchId == producedBy)
            .Select(m => new GenealogyNode(m.BatchId, m.Batch!.Product!.Name, m.Batch.BatchNo, m.Batch.ExpiryDate, m.QtyUnits, null, null)).ToListAsync(ct);
        var usedIn = await db.MaterialConsumptions.AsNoTracking().Where(m => m.BatchId == batchId)
            .Join(db.ProductionBatches, m => m.ProductionBatchId, p => p.Id, (m, p) => new { m.QtyUnits, p })
            .Select(x => new GenealogyNode(x.p.OutputBatchId ?? Guid.Empty, x.p.Product!.Name, x.p.BatchNo, x.p.ExpiryDate, x.QtyUnits, x.p.Status.ToString(), x.p.Id)).ToListAsync(ct);
        var samples = new List<QcSampleDto>();
        foreach (var sid in await db.QcSamples.Where(x => x.BatchId == batchId).OrderBy(x => x.CreatedAt).Select(x => x.Id).ToListAsync(ct)) samples.Add(await SampleDtoAsync(sid, ct));
        return new Genealogy(b.Id, b.Product!.Name, b.BatchNo, b.IsBlocked, b.BlockReason, madeFrom, usedIn, samples);
    }

    public async Task<List<GenealogyNode>> FindBatchesAsync(string q, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(q), "Type a batch number or product name.");
        var s = q.Trim();
        return await db.Batches.AsNoTracking().Where(b => b.BatchNo.Contains(s) || b.Product!.Name.Contains(s)).OrderByDescending(b => b.CreatedAt).Take(30)
            .Select(b => new GenealogyNode(b.Id, b.Product!.Name, b.BatchNo, b.ExpiryDate, db.StockBalances.Where(x => x.BatchId == b.Id).Sum(x => (decimal?)x.QtyUnits) ?? 0, null, null))
            .ToListAsync(ct);
    }
}
