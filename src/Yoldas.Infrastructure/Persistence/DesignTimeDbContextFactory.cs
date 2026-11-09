using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Yoldas.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; generating migrations does not open a connection.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<YoldasDbContext>
{
    public YoldasDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<YoldasDbContext>();
        DependencyInjection.ConfigureDbContext(options, DependencyInjection.BuildDataSource("Host=localhost;Database=yoldas_design"));
        return new YoldasDbContext(options.Options);
    }
}
