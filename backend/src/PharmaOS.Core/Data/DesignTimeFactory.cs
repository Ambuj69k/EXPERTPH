using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PharmaOS.Core.Common;

namespace PharmaOS.Core.Data;

/// <summary>Used only by `dotnet ef` to build migrations.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<PharmaDbContext>
{
    public PharmaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PharmaDbContext>()
            .UseSqlServer("Server=localhost;Database=PharmaOS_Dev;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new PharmaDbContext(options, new FixedTenantContext());
    }
}
