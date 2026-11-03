using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Novcic.Core;

public static class Wire
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString };
}

public class AccountSettings
{
    public Guid UserId { get; set; }
    public int Leverage { get; set; } = 1;
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
    public string InstrumentId { get; set; } = "BTCUSDT";
    public int Leverage { get; set; } = 1;
    public bool ReduceOnly { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public long Sequence { get; set; }
    public string Side { get; set; } = "Buy";
    public string Type { get; set; } = "Limit";
    public decimal? Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal Remaining { get; set; }
    public decimal Reserved { get; set; }
    public decimal FilledNotional { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal? AverageExecutionPrice => Quantity > Remaining ? FilledNotional / (Quantity - Remaining) : null;
    public string Status { get; set; } = "Open";
    public string? CancelReason { get; set; }
    public DateTime? QuoteValidatedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class Trade
{
    public string InstrumentId { get; set; } = "BTCUSDT";
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Sequence { get; set; }
    public decimal? BuyerFee { get; set; }
    public decimal? SellerFee { get; set; }
    public string? BuyerFeeAsset { get; set; }
    public string? SellerFeeAsset { get; set; }
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
public class JournalLine
{
    public long Id { get; set; }
    public Guid ReferenceId { get; set; }
    public string Account { get; set; } = "";
    public string Asset { get; set; } = "";
    public decimal Delta { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class DepositAddress
{
    public Guid UserId { get; set; }
    public string Address { get; set; } = "";
    public string EncryptedKey { get; set; } = "";
}
public class ChainTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Generation { get; set; } = "";
    public string Kind { get; set; } = "Deposit";
    public string Asset { get; set; } = "";
    public decimal Amount { get; set; }
    public string Address { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? Hash { get; set; }
    public string? RawTransaction { get; set; }
    public string? Sender { get; set; }
    public long? Nonce { get; set; }
    public long? Block { get; set; }
    public string? BlockHash { get; set; }
    public long LogIndex { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class ChainCursor
{
    public string Generation { get; set; } = "";
    public long Block { get; set; }
    public string BlockHash { get; set; } = "";
    public bool Quarantined { get; set; }
}
public class ExchangeDb(DbContextOptions<ExchangeDb> options) : DbContext(options)
{
    public DbSet<AccountSettings> AccountSettings => Set<AccountSettings>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<LedgerEntry> Ledger => Set<LedgerEntry>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<JournalLine> Journal => Set<JournalLine>();
    public DbSet<DepositAddress> DepositAddresses => Set<DepositAddress>();
    public DbSet<ChainTransfer> ChainTransfers => Set<ChainTransfer>();
    public DbSet<ChainCursor> ChainCursors => Set<ChainCursor>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<MarginBalance> MarginBalances => Set<MarginBalance>();
    public DbSet<ClearingPool> ClearingPools => Set<ClearingPool>();
    public DbSet<RiskEvent> RiskEvents => Set<RiskEvent>();
    public DbSet<FundingEvent> FundingEvents => Set<FundingEvent>();
    public DbSet<IndexSample> IndexSamples => Set<IndexSample>();
    public DbSet<LendingPool> LendingPools => Set<LendingPool>();
    public DbSet<Supply> Supplies => Set<Supply>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<LiquidityPool> LiquidityPools => Set<LiquidityPool>();
    public DbSet<LiquidityShare> LiquidityShares => Set<LiquidityShare>();
    public DbSet<DcaSchedule> DcaSchedules => Set<DcaSchedule>();
    public DbSet<ApiCredential> ApiKeys => Set<ApiCredential>();
    public DbSet<ApiNonce> ApiNonces => Set<ApiNonce>();
    public DbSet<ExchangeRevision> Revisions => Set<ExchangeRevision>();
    public DbSet<LiquidityOperation> LiquidityOperations => Set<LiquidityOperation>();
    public DbSet<OptionReference> OptionReferences => Set<OptionReference>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<LiquidityOperation>().ToTable("LiquidityOperations","exchange").HasIndex(x=>new{x.UserId,x.CreatedAt});
        m.Entity<OptionReference>().ToTable("OptionReferences","exchange").HasKey(x=>x.Symbol);
        m.Entity<AccountSettings>().ToTable("AccountSettings", "exchange").HasKey(x => x.UserId);
        m.Entity<User>().ToTable("Users", "auth").HasIndex(x => x.Email).IsUnique();
        m.Entity<User>().Property(x => x.Email).HasMaxLength(254);
        m.Entity<ApiCredential>().ToTable("ApiKeys","auth").HasIndex(x=>x.UserId);
        m.Entity<ApiNonce>().ToTable("ApiNonces","auth").HasKey(x=>new{x.KeyId,x.Nonce});
        m.Entity<ApiNonce>().HasIndex(x=>x.CreatedAt);
        m.Entity<ExchangeRevision>().ToTable("Revision","exchange").HasData(new ExchangeRevision());
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
        m.Entity<JournalLine>().ToTable("Journal", "exchange").HasIndex(x => new { x.ReferenceId, x.Asset });
        m.Entity<DepositAddress>().ToTable("Addresses", "custody").HasKey(x => x.UserId);
        m.Entity<DepositAddress>().HasIndex(x => x.Address).IsUnique();
        m.Entity<ChainTransfer>().ToTable("Transfers", "custody").HasIndex(x => new { x.Generation, x.Hash, x.LogIndex, x.Kind }).IsUnique();
        m.Entity<ChainTransfer>().HasIndex(x => new { x.UserId, x.CreatedAt });
        m.Entity<ChainCursor>().ToTable("Cursors", "custody").HasKey(x => x.Generation);
        m.Entity<Instrument>().ToTable("Instruments", "exchange");
        m.Entity<Order>().Property(x=>x.InstrumentId).HasDefaultValue("BTCUSDT");
        m.Entity<Trade>().Property(x=>x.InstrumentId).HasDefaultValue("BTCUSDT");
        m.Entity<Order>().HasIndex(x=>new{x.InstrumentId,x.Status,x.Side,x.Price,x.Sequence});
        m.Entity<Trade>().HasIndex(x=>new{x.InstrumentId,x.CreatedAt});
        m.Entity<Position>().ToTable("Positions", "exchange").HasKey(x => new { x.UserId, x.InstrumentId });
        m.Entity<MarginBalance>().ToTable("MarginBalances", "exchange").HasKey(x => x.UserId);
        m.Entity<ClearingPool>().ToTable("ClearingPools", "exchange", t => t.HasCheckConstraint("clearing_nonnegative", "\"Cash\" >= 0"));
        m.Entity<RiskEvent>().ToTable("RiskEvents", "exchange").HasIndex(x => new { x.UserId, x.Time });
        m.Entity<FundingEvent>().ToTable("FundingEvents", "exchange").HasKey(x => new { x.InstrumentId, x.Time });
        m.Entity<IndexSample>().ToTable("IndexSamples", "exchange").HasIndex(x => x.Time);
        m.Entity<LendingPool>().ToTable("LendingPools", "exchange",t=>t.HasCheckConstraint("lending_nonnegative","\"Cash\">=0 AND \"Debt\">=0 AND \"Shares\">=0 AND \"Reserves\">=0")).HasKey(x => x.Asset);
        m.Entity<Supply>().ToTable("Supplies", "exchange",t=>t.HasCheckConstraint("supply_nonnegative","\"Shares\">=0 AND \"QueuedShares\">=0 AND \"QueuedShares\"<=\"Shares\"")).HasKey(x => new { x.UserId, x.Asset });
        m.Entity<Loan>().ToTable("Loans", "exchange",t=>t.HasCheckConstraint("loan_nonnegative","\"Principal\">=0 AND \"Interest\">=0 AND \"Collateral\">=0 AND \"BadDebt\">=0")).HasIndex(x => x.UserId);
        m.Entity<LiquidityPool>().ToTable("LiquidityPools", "exchange",t=>t.HasCheckConstraint("amm_nonnegative","\"Btc\">=0 AND \"Usdt\">=0 AND \"Shares\">=0"));
        m.Entity<LiquidityShare>().ToTable("LiquidityShares", "exchange",t=>t.HasCheckConstraint("lp_nonnegative","\"Shares\">=0")).HasKey(x => x.UserId);
        m.Entity<DcaSchedule>().ToTable("DcaSchedules", "exchange").HasIndex(x => x.NextRun);
        foreach (var e in m.Model.GetEntityTypes())
        foreach (var p in e.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        { p.SetPrecision(28); p.SetScale(8); }
        m.Entity<Position>().Property(x => x.EntryPrice).HasPrecision(28, 16);
        m.Entity<Loan>().Property(x => x.InterestRemainder).HasPrecision(28, 20);
        m.Entity<LendingPool>().Property(x => x.ReserveRemainder).HasPrecision(28, 20);
    }
}
public record PlaceOrder(string Side, string Type, decimal Quantity, decimal? Price, string Instrument = "BTCUSDT", int? Leverage = null, bool ReduceOnly = false);
public record TransferRequest(string Asset, string From, string To, decimal Amount);
public record WithdrawalRequest(string Asset, string Address, decimal Amount);
public record Candle(long Time, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);
public class RuleException(string message) : Exception(message);
