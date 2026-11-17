using System.Globalization;
using System.Xml.Linq;

namespace PharmaOS.Tests;

[Collection("api")]
public class TallyExportTests(ApiFixture f)
{
    [Fact]
    public async Task Tally_xml_is_well_formed_balanced_and_has_masters()
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Tally Tab");
        var supplier = (await c.Post("/api/v1/suppliers", new { name = "Shah & Sons <Pharma>", paymentTermsDays = 30, isActive = true })).GetProperty("id").GetGuid();
        await c.PurchaseAsync(supplier, product, ("TY1", 12, 3, 100, 70));
        await c.SellAsync(product, 10, 100);
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        var xml = XDocument.Parse(await c.Http.GetStringAsync($"/api/v1/accounting/export.tally.xml?from={today}&to={today}")); // throws if not well-formed
        var ledgers = xml.Descendants("LEDGER").ToDictionary(l => l.Element("NAME")!.Value, l => l.Element("PARENT")!.Value);
        var vouchers = xml.Descendants("VOUCHER").ToList();
        Assert.Equal(2, vouchers.Count); // purchase + sale
        Assert.Contains(vouchers, v => v.Element("NARRATION")!.Value.Contains("Shah & Sons <Pharma>"));

        foreach (var v in vouchers)
        {
            var lines = v.Elements("ALLLEDGERENTRIES.LIST").ToList();
            var amounts = lines.Select(l => decimal.Parse(l.Element("AMOUNT")!.Value, CultureInfo.InvariantCulture)).ToList();
            Assert.Equal(0m, amounts.Sum()); // balanced in Tally sign convention
            foreach (var l in lines)
            {
                var amount = decimal.Parse(l.Element("AMOUNT")!.Value, CultureInfo.InvariantCulture);
                Assert.Equal(amount < 0 ? "Yes" : "No", l.Element("ISDEEMEDPOSITIVE")!.Value); // debit = negative + deemed positive
                Assert.True(ledgers.ContainsKey(l.Element("LEDGERNAME")!.Value), "every voucher ledger has a master");
            }
        }
        Assert.Equal("Sundry Creditors", ledgers["Supplier Payables (2000)"]);
        Assert.Equal("Sales Accounts", ledgers["Sales (4000)"]);
        Assert.Equal("Duties & Taxes", ledgers["GST Output Payable (2100)"]);
        Assert.Equal(today.Replace("-", ""), vouchers[0].Element("DATE")!.Value);
    }
}
