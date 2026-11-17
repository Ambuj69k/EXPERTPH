using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

/// <summary>
/// Product master. Stock is kept in BASE units (e.g. tablets). One PACK (e.g. strip) = PackSize base units.
/// MRP and purchase rates are captured per pack, as printed/invoiced.
/// </summary>
public class Product : TenantEntity, IVersioned
{
    public string Name { get; set; } = "";
    public string? Composition { get; set; }
    public string? Manufacturer { get; set; }
    public string? HsnCode { get; set; }
    public decimal GstRate { get; set; }
    public decimal PackSize { get; set; } = 1m;
    public string PackUnit { get; set; } = "Strip";
    public string BaseUnit { get; set; } = "Tab";
    /// <summary>Whether part-packs (loose tablets) may be sold.</summary>
    public bool AllowLooseSale { get; set; } = true;
    public DrugSchedule Schedule { get; set; }
    public string? Barcode { get; set; }
    public decimal ReorderLevelUnits { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; }
    /// <summary>Shown on the public ordering page. Never honoured for scheduled (prescription) drugs.</summary>
    public bool OnlineListed { get; set; }
}

public class Supplier : TenantEntity, IVersioned
{
    public string Name { get; set; } = "";
    public string? Gstin { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? DrugLicenseNo { get; set; }
    public int PaymentTermsDays { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; }
    /// <summary>Saved CSV column mapping for this supplier's invoice files (JSON).</summary>
    public string? ImportMappingJson { get; set; }
}

/// <summary>Minimal retail customer record. Deliberately no health data.</summary>
public class Customer : TenantEntity
{
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    /// <summary>Max outstanding credit allowed. Null = no limit set (credit allowed); 0 = no credit.</summary>
    public decimal? CreditLimit { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>False once merged into another customer record.</summary>
    public bool IsActive { get; set; } = true;
    public Guid? MergedIntoId { get; set; }
}

/// <summary>Branch-specific selling price per pack (GST inclusive). Never above MRP: bills use min(price, batch MRP).</summary>
public class BranchProductPrice : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal PricePerPack { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
