namespace PharmaOS.Core.Common;

/// <summary>
/// Central rounding rules. All money is decimal, rounded half away from zero to paise.
/// NOTE: rounding/tax conventions here are engineering defaults and must be reviewed by a CA.
/// </summary>
public static class Money
{
    public static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    public static decimal R4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);

    /// <summary>Split a tax-inclusive amount (e.g. MRP-based retail price) into taxable value and tax.</summary>
    public static (decimal Taxable, decimal Tax) SplitInclusive(decimal inclusiveAmount, decimal gstRatePercent)
    {
        var taxable = R2(inclusiveAmount * 100m / (100m + gstRatePercent));
        return (taxable, R2(inclusiveAmount - taxable));
    }

    /// <summary>Tax on a tax-exclusive amount (e.g. supplier purchase rate).</summary>
    public static decimal TaxOnExclusive(decimal taxableAmount, decimal gstRatePercent)
        => R2(taxableAmount * gstRatePercent / 100m);

    /// <summary>Round invoice total to nearest rupee; returns (rounded, roundOff).</summary>
    public static (decimal Rounded, decimal RoundOff) RoundToRupee(decimal amount)
    {
        var rounded = Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        return (rounded, R2(rounded - amount));
    }
}

/// <summary>Business-calendar helpers. Indian financial year runs April–March.</summary>
public static class BusinessCalendar
{
    public const string DefaultTimeZone = "Asia/Kolkata";

    public static DateOnly LocalDate(DateTimeOffset utc, string timeZoneId = DefaultTimeZone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, tz).DateTime);
    }

    /// <summary>e.g. 2026-11-16 → "2627" (FY 2026-27).</summary>
    public static string FinancialYearCode(DateOnly date)
    {
        var start = date.Month >= 4 ? date.Year : date.Year - 1;
        return $"{start % 100:D2}{(start + 1) % 100:D2}";
    }

    /// <summary>Medicine expiry is printed as MM/YYYY and means "valid through end of that month".</summary>
    public static DateOnly EndOfMonth(int year, int month)
        => new(year, month, DateTime.DaysInMonth(year, month));

    public static DateOnly NormalizeExpiry(DateOnly d) => EndOfMonth(d.Year, d.Month);
}

/// <summary>Retail line pricing shared by billing, previews and the POS screen logic.</summary>
public static class Pricing
{
    /// <summary>
    /// Gross is always at MRP. The selling base is the branch price when it is below MRP; the requested (extra)
    /// discount % then applies to that base. Discount = gross − net, so the bill shows MRP and the full saving.
    /// </summary>
    public static (decimal MrpPerUnit, decimal Gross, decimal Discount, decimal DiscountPercent, decimal Net) Line(
        decimal qtyUnits, decimal mrpPerPack, decimal packSize, decimal? branchPricePerPack, decimal extraDiscountPercent)
    {
        var mrpPerUnit = Money.R4(mrpPerPack / packSize);
        var gross = Money.R2(qtyUnits * mrpPerUnit);
        var baseAmount = branchPricePerPack is { } p && p < mrpPerPack ? Money.R2(qtyUnits * Money.R4(p / packSize)) : gross;
        var net = baseAmount - Money.R2(baseAmount * extraDiscountPercent / 100m);
        var discount = gross - net;
        return (mrpPerUnit, gross, discount, gross == 0 ? 0 : Money.R2(discount * 100m / gross), net);
    }
}
