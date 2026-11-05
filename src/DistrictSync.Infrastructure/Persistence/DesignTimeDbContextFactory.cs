using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace DistrictSync.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without a running database or host.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=DistrictSync;Trusted_Connection=True;TrustServerCertificate=True", sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema))
            .Options;

        var tenant = new TenantContext();
        tenant.SetSystem("design-time");
        var services = new ServiceCollection().BuildServiceProvider();
        return new AppDbContext(options, tenant, new DomainEventDispatcher(services), new OutboxWriter(), TimeProvider.System);
    }
}
