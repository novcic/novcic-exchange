using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Novcic.Core;

public static class Wire
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString };
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Country { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public bool Revoked { get; set; }
}
public class Wallet
{
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "Funding";
    public string Asset { get; set; } = "BTC";
    public decimal Available { get; set; }
    public decimal Locked { get; set; }
}
public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public long Sequence { get; set; }
    public string Side { get; set; } = "Buy";
    public string Type { get; set; } = "Limit";
    public decimal? Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal Remaining { get; set; }
    public decimal Reserved { get; set; }
    public string Status { get; set; } = "Open";
    public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class Trade
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Sequence { get; set; }
    public Guid MakerOrderId { get; set; }
    public Guid TakerOrderId { get; set; }
    public Guid BuyerId { get; set; }
    public Guid SellerId { get; set; }
    public string TakerSide { get; set; } = "Buy";
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class LedgerEntry
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string Wallet { get; set; } = "";
    public string Asset { get; set; } = "";
    public decimal AvailableDelta { get; set; }
    public decimal LockedDelta { get; set; }
    public string Reason { get; set; } = "";
    public Guid ReferenceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class Receipt
{
    public Guid UserId { get; set; }
    public string Key { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Response { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class ExchangeDb(DbContextOptions<ExchangeDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<LedgerEntry> Ledger => Set<LedgerEntry>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<User>().ToTable("Users", "auth").HasIndex(x => x.Email).IsUnique();
        m.Entity<User>().Property(x => x.Email).HasMaxLength(254);
        m.Entity<Session>().ToTable("Sessions", "auth").HasIndex(x => x.TokenHash).IsUnique();
        m.Entity<Session>().HasIndex(x => x.FamilyId);
        m.Entity<Wallet>().ToTable("Wallets", "exchange", t => {
            t.HasCheckConstraint("wallet_nonnegative", "\"Available\" >= 0 AND \"Locked\" >= 0");
        }).HasKey(x => new { x.UserId, x.Kind, x.Asset });
        m.Entity<Order>().ToTable("Orders", "exchange").HasIndex(x => new { x.Status, x.Side, x.Price, x.Sequence });
        m.Entity<Order>().HasIndex(x => new { x.UserId, x.CreatedAt });
        m.Entity<Order>().Property(x => x.Sequence).UseIdentityAlwaysColumn();
        m.Entity<Trade>().ToTable("Trades", "exchange").HasIndex(x => x.CreatedAt);
        m.Entity<Trade>().Property(x => x.Sequence).UseIdentityAlwaysColumn();
        m.Entity<LedgerEntry>().ToTable("Ledger", "exchange").HasIndex(x => new { x.UserId, x.Id });
        m.Entity<Receipt>().ToTable("Receipts", "exchange").HasKey(x => new { x.UserId, x.Key });
        m.Entity<Receipt>().Property(x => x.Key).HasMaxLength(128);
        foreach (var e in m.Model.GetEntityTypes())
        foreach (var p in e.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        { p.SetPrecision(28); p.SetScale(8); }
    }
}
public record PlaceOrder(string Side, string Type, decimal Quantity, decimal? Price);
public record TransferRequest(string Asset, string From, string To, decimal Amount);
public record Candle(long Time, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);
public class RuleException(string message) : Exception(message);
