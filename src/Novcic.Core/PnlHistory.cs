using Microsoft.EntityFrameworkCore;
namespace Novcic.Core;

public class PnlLifecycle
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string InstrumentId { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Direction { get; set; }
    public decimal Remaining { get; set; }
    public decimal Quantity { get; set; }
    public decimal EntryNotional { get; set; }
    public decimal ExitNotional { get; set; }
    public decimal OpeningFee { get; set; }
    public decimal ClosingFee { get; set; }
    public decimal Funding { get; set; }
    public bool Verified { get; set; } = true;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
public class PnlProjectionState
{
    public int Id { get; set; } = 1;
    public long LastSequence { get; set; }
}
public record PnlRow(long Id,string InstrumentId,string Kind,string Direction,decimal Quantity,decimal AverageEntry,decimal AverageExit,decimal GrossPnl,decimal OpeningFee,decimal ClosingFee,decimal Funding,decimal NetPnl,DateTime OpenedAt,DateTime ClosedAt,long DurationSeconds);
public record PnlPage(List<PnlRow> Items,string? NextCursor);

public partial class TradingService
{
    // Called inside the same financial transaction as matching. The watermark also
    // makes full-history reconstruction and replay safe under the shared command lock.
    public async Task RecordPnlTrade(Trade trade)
    {
        var state=await db.PnlProjection.FindAsync(1);
        if(state==null){state=new PnlProjectionState();db.PnlProjection.Add(state);}
        if(trade.Sequence<=state.LastSequence)return;
        var instrument=(await db.Instruments.FindAsync(trade.InstrumentId))!;
        if(instrument.Kind!="Spot"){
            await RecordPnlFill(trade.BuyerId,instrument,trade.Quantity,trade.Price,trade.BuyerFee,trade.CreatedAt);
            await RecordPnlFill(trade.SellerId,instrument,-trade.Quantity,trade.Price,trade.SellerFee,trade.CreatedAt);
        }
        state.LastSequence=trade.Sequence;
        await db.SaveChangesAsync();
    }
    private async Task RecordPnlFill(Guid user,Instrument instrument,decimal signed,decimal price,decimal? fee,DateTime time)
    {
        var row=await db.PnlLifecycles.SingleOrDefaultAsync(x=>x.UserId==user&&x.InstrumentId==instrument.Id&&x.ClosedAt==null);
        var total=Math.Abs(signed);var opening=total;var charged=fee??0;
        if(row!=null&&row.Direction!=Math.Sign(signed)){
            var closing=Math.Min(row.Remaining,total);
            var closingFee=closing==total?charged:decimal.Round(charged*closing/total,8,MidpointRounding.ToEven);
            row.Remaining-=closing;row.ExitNotional+=closing*price;row.ClosingFee+=closingFee;row.Verified&=fee.HasValue;
            opening-=closing;charged-=closingFee;
            if(row.Remaining==0){row.ClosedAt=time;await db.SaveChangesAsync();row=null;}
        }
        if(opening>0){
            if(row==null){row=new PnlLifecycle{UserId=user,InstrumentId=instrument.Id,Kind=instrument.Kind,Direction=Math.Sign(signed),OpenedAt=time};db.PnlLifecycles.Add(row);}
            row.Quantity+=opening;row.Remaining+=opening;row.EntryNotional+=opening*price;row.OpeningFee+=charged;row.Verified&=fee.HasValue;
        }
    }
    public async Task SettlePnl(Guid user,string instrument,decimal exitNotional,DateTime time)
    {
        var row=await db.PnlLifecycles.SingleOrDefaultAsync(x=>x.UserId==user&&x.InstrumentId==instrument&&x.ClosedAt==null);
        if(row==null)throw new RuleException("Position history is missing for settlement.");
        // Use the actual rounded settlement cash plus remaining basis (futures),
        // or actual intrinsic payout (options), supplied by the settlement caller.
        row.ExitNotional+=exitNotional;row.Remaining=0;row.ClosedAt=time;
    }
    public async Task AttributePnlFunding(Guid user,string instrument,DateTime boundary,decimal amount)
    {
        if(amount==0)return;
        // The boundary can precede processing/restart and belong to a closed lifecycle.
        var row=await db.PnlLifecycles.SingleOrDefaultAsync(x=>x.UserId==user&&x.InstrumentId==instrument&&x.OpenedAt<boundary&&(x.ClosedAt==null||x.ClosedAt>=boundary));
        if(row==null)throw new RuleException("Position history is missing for funding.");
        row.Funding+=decimal.Round(amount,8,MidpointRounding.ToEven);
    }
    public async Task BackfillPnl()
    {
        if(await db.PnlProjection.AnyAsync())return;
        foreach(var trade in await db.Trades.AsNoTracking().OrderBy(t=>t.Sequence).ToListAsync())await RecordPnlTrade(trade);
        // Old settlement/funding journal entries did not identify their lifecycle.
        // Preserve reconstructed rows internally, but never publish an unverified net.
        foreach(var row in await db.PnlLifecycles.Where(x=>x.ClosedAt==null).ToListAsync()){
            var i=(await db.Instruments.FindAsync(row.InstrumentId))!;
            var p=await db.Positions.FindAsync(row.UserId,row.InstrumentId);
            if(i.Status=="Settled"&&p?.Quantity==0&&i.SettlementPrice.HasValue){
                var price=i.Kind=="Option"?(i.OptionSide=="Call"?Math.Max(0,i.SettlementPrice.Value-i.Strike!.Value):Math.Max(0,i.Strike!.Value-i.SettlementPrice.Value)):i.SettlementPrice.Value;
                await SettlePnl(row.UserId,i.Id,decimal.Round(row.Remaining*price,8,MidpointRounding.ToEven),p.UpdatedAt);row.Verified=false;
            }else if(p==null||p.Quantity!=row.Remaining*row.Direction)throw new RuleException("Position history does not reconcile with current exposure.");
        }
        foreach(var funding in await db.FundingEvents.Where(f=>f.Rate!=0).ToListAsync())
            foreach(var row in await db.PnlLifecycles.Where(x=>x.InstrumentId==funding.InstrumentId&&x.OpenedAt<funding.Time&&(x.ClosedAt==null||x.ClosedAt>=funding.Time)).ToListAsync())row.Verified=false;
        if(!db.PnlProjection.Local.Any())db.PnlProjection.Add(new PnlProjectionState());
        await db.SaveChangesAsync();
    }
    public async Task<PnlPage> PnlHistory(Guid user,string[]? allowed=null,string? instrument=null,string? product=null,DateOnly? from=null,DateOnly? to=null,string? cursor=null,int limit=50)
    {
        if(limit<1||limit>100||from>to||to==DateOnly.MaxValue)throw new RuleException("Use a valid date range and page size between 1 and 100.");
        if(product!=null&&product is not("Perpetual" or "Future" or "Option"))throw new RuleException("Choose a derivative product.");
        var query=db.PnlLifecycles.AsNoTracking().Where(x=>x.UserId==user&&x.Verified&&x.ClosedAt!=null);
        if(allowed!=null)query=query.Where(x=>allowed.Contains(x.Kind));
        if(instrument!=null)query=query.Where(x=>x.InstrumentId==instrument);
        if(product!=null)query=query.Where(x=>x.Kind==product);
        if(from.HasValue){var start=from.Value.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc);query=query.Where(x=>x.ClosedAt>=start);}
        if(to.HasValue){var end=to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc);query=query.Where(x=>x.ClosedAt<end);}
        if(cursor!=null){
            if(!long.TryParse(cursor,out var id))throw new RuleException("Invalid history cursor.");
            var anchor=await query.SingleOrDefaultAsync(x=>x.Id==id)??throw new RuleException("History cursor does not match this account or filter.");
            query=query.Where(x=>x.ClosedAt<anchor.ClosedAt||(x.ClosedAt==anchor.ClosedAt&&x.Id<id));
        }
        var rows=await query.OrderByDescending(x=>x.ClosedAt).ThenByDescending(x=>x.Id).Take(limit+1).ToListAsync();
        var items=rows.Take(limit).Select(x=>{var gross=x.Direction*(x.ExitNotional-x.EntryNotional);return new PnlRow(x.Id,x.InstrumentId,x.Kind,x.Direction>0?"Long":"Short",x.Quantity,x.EntryNotional/x.Quantity,x.ExitNotional/x.Quantity,gross,x.OpeningFee,x.ClosingFee,x.Funding,gross-x.OpeningFee-x.ClosingFee+x.Funding,x.OpenedAt,x.ClosedAt!.Value,(x.ClosedAt.Value.Ticks-x.OpenedAt.Ticks)/TimeSpan.TicksPerSecond);}).ToList();
        return new(items,rows.Count>limit?items[^1].Id.ToString(System.Globalization.CultureInfo.InvariantCulture):null);
    }
}
