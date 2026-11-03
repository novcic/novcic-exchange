using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Novcic.Core;
public class DesignFactory : IDesignTimeDbContextFactory<ExchangeDb>
{
    public ExchangeDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ExchangeDb>()
        .UseNpgsql(Environment.GetEnvironmentVariable("EXCHANGE_DB") ?? "Host=localhost;Port=54330;Database=novcic;Username=novcic;Password=novcic-local-demo").Options);
}
