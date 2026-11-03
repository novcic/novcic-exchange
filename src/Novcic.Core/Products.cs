namespace Novcic.Core;
public class ExchangeRevision
{
    public int Id { get; set; }=1;
    public long Revision { get; set; }
}
public class Instrument
{
    public string Id { get; set; } = "BTCUSDT";
    public string Kind { get; set; } = "Spot";
    public DateTime? Expiry { get; set; }
    public decimal? Strike { get; set; }
    public string? OptionSide { get; set; }
    public string Status { get; set; } = "Trading";
    public decimal? SettlementPrice { get; set; }
}
public class Position
{
    public Guid UserId { get; set; }
    public string InstrumentId { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal Cost { get; set; }
    public int Leverage { get; set; } = 1;
    public decimal RealizedPnl { get; set; }
    public decimal Reserved { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public class MarginBalance
{
    public Guid UserId { get; set; }
    public decimal Cash { get; set; }
}
public class ClearingPool
{
    public int Id { get; set; } = 1;
    public decimal Cash { get; set; }
}
public class RiskEvent
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.UtcNow;
}
public class FundingEvent
{
    public string InstrumentId { get; set; } = "";
    public DateTime Time { get; set; }
    public decimal Rate { get; set; }
}
public class IndexSample
{
    public long Id { get; set; }
    public DateTime Time { get; set; }
    public decimal Price { get; set; }
    public decimal Spot { get; set; }
    public decimal? Perpetual { get; set; }
}
public class LendingPool
{
    public string Asset { get; set; } = "";
    public decimal Cash { get; set; }
    public decimal Debt { get; set; }
    public decimal Shares { get; set; }
    public decimal Reserves { get; set; }
    public decimal ReserveRemainder { get; set; }
    public DateTime AccruedAt { get; set; } = DateTime.UtcNow;
}
public class Supply
{
    public Guid UserId { get; set; }
    public string Asset { get; set; } = "";
    public decimal Shares { get; set; }
    public decimal QueuedShares { get; set; }
}
public class Loan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Asset { get; set; } = "";
    public string CollateralAsset { get; set; } = "";
    public decimal Principal { get; set; }
    public decimal Interest { get; set; }
    public decimal InterestRemainder { get; set; }
    public decimal BadDebt { get; set; }
    public decimal Collateral { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class LiquidityPool
{
    public int Id { get; set; } = 1;
    public decimal Btc { get; set; }
    public decimal Usdt { get; set; }
    public decimal Shares { get; set; }
}
public class LiquidityShare
{
    public Guid UserId { get; set; }
    public decimal Shares { get; set; }
}
public class DcaSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public decimal? Budget { get; set; }
    public decimal Spent { get; set; }
    public int Hours { get; set; }
    public DateTime NextRun { get; set; }
    public bool Paused { get; set; }
    public string? LastOutcome { get; set; }
}
