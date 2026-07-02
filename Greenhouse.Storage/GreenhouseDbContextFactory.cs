using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Greenhouse.Storage;

/// <summary>
/// Lets the <c>dotnet ef</c> tooling construct the context at design time
/// (for example, when adding or scripting migrations) without a running host.
/// Not used at runtime; the host registers the context through DI.
/// </summary>
internal sealed class GreenhouseDbContextFactory : IDesignTimeDbContextFactory<GreenhouseDbContext>
{
    public GreenhouseDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GreenhouseDbContext>()
            .UseSqlite("Data Source=greenhouse.db")
            .Options;

        return new GreenhouseDbContext(options);
    }
}
