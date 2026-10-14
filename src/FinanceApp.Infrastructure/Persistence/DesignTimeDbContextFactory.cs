using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinanceApp.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef migrations</c>; the database file is never opened.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
