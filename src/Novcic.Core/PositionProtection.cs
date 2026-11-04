using Microsoft.EntityFrameworkCore;
namespace Novcic.Core;

public class PositionProtection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string InstrumentId { get; set; } = "";
    public Guid LifecycleId { get; set; }
    public long Version { get; set; } = 1;
    public int Direction { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal? StopLoss { get; set; }
    public string Status { get; set; } = "Armed";
    public string? TriggerLeg { get; set; }
    public long? TriggerSequence { get; set; }
    public long Attempts { get; set; }
    public int RetryDelayMs { get; set; } = 100;
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public string? Detail { get; set; }
}
public record ProtectionRequest(string Instrument, Guid LifecycleId, long PositionVersion, long ProtectionVersion, decimal? TakeProfit, decimal? StopLoss);
public record StopClosingRequest(string Instrument, Guid Id, long Version);
public record AmendOrderRequest(string Instrument, Guid Id, string Version, decimal Price);
public record PositionView(string InstrumentId,string Kind,decimal Quantity,decimal EntryPrice,decimal Cost,int Leverage,decimal RealizedPnl,
    Guid LifecycleId,long Version,decimal? LastPrice,decimal? PriceChangePercent,decimal? MarkPrice,decimal? PositionValue,
    decimal? UnrealizedPnl,decimal? ReturnOnMarginPercent,decimal? EntryMargin,decimal? LiquidationPrice,string LiquidationStatus,PositionProtection? Protection,Guid UserId,decimal Reserved,DateTime UpdatedAt);

public partial class TradingService
{
    // Pure remaining-cost accounting, also used to rebuild option analytics from complete history.
    public static (decimal Quantity,decimal Cost,decimal Realized) ApplyBasis(decimal quantity,decimal cost,decimal signed,decimal price)
    {
        var combined=quantity+signed;decimal realized=0;
        if(quantity*signed<0){var closed=Math.Min(Math.Abs(quantity),Math.Abs(signed));var released=closed==Math.Abs(quantity)?cost:decimal.Round(cost*closed/Math.Abs(quantity),8,MidpointRounding.ToEven);realized=closed*price*Math.Sign(quantity)-released;cost-=released;if(quantity*combined<0)cost=combined*price;}
        else cost+=signed*price;
        return(combined,combined==0?0:cost,realized);
    }
    public async Task InitializePositionAnalytics(Position p,Instrument i)
    {
        if(p.AnalyticsReady)return;
        if(i.Kind=="Option"){
            decimal quantity=0,cost=0,realized=0;
            foreach(var t in await db.Trades.Where(t=>t.InstrumentId==i.Id&&(t.BuyerId==p.UserId||t.SellerId==p.UserId)).OrderBy(t=>t.Sequence).ToListAsync()){
                var next=ApplyBasis(quantity,cost,t.BuyerId==p.UserId?t.Quantity:-t.Quantity,t.Price);quantity=next.Quantity;cost=next.Cost;realized+=next.Realized;
            }
            if(p.Quantity!=0&&quantity!=p.Quantity)throw new RuleException("Option history does not reconcile with the position.");
            p.Cost=p.Quantity==0?0:cost;p.EntryPrice=p.Quantity==0?0:decimal.Round(cost/p.Quantity,16);p.TradeRealizedPnl=realized;
        }
        p.AnalyticsReady=true;
    }
    public async Task EndPositionProtection(Position p,string reason)
    {
        foreach(var protection in await db.Protections.Where(x=>x.UserId==p.UserId&&x.InstrumentId==p.InstrumentId&&x.LifecycleId==p.LifecycleId&&(x.Status=="Armed"||x.Status=="Closing")).ToListAsync()){
            protection.Status=reason=="Position closed"?"Completed":"Cancelled";protection.Detail=reason;protection.Version++;
        }
    }
    public async Task<object> SetProtection(Guid user,ProtectionRequest r)
    {
        var p=await db.Positions.FindAsync(user,r.Instrument)??throw new RuleException("Position not found.");
        if(p.Quantity==0||p.LifecycleId!=r.LifecycleId||p.Version!=r.PositionVersion)throw new RuleException("Position changed. Refresh and review again.");
        var instrument=await db.Instruments.FindAsync(r.Instrument);
        if(instrument==null||instrument.Status!="Trading"||instrument.Expiry<=DateTime.UtcNow)throw new RuleException("Instrument is no longer trading.");
        var existing=await db.Protections.SingleOrDefaultAsync(x=>x.UserId==user&&x.InstrumentId==r.Instrument&&x.LifecycleId==p.LifecycleId);
        if((existing?.Version??0)!=r.ProtectionVersion)throw new RuleException("TP/SL changed. Refresh and review again.");
        if(existing?.Status=="Closing")throw new RuleException("Stop the active closing process before changing TP/SL.");
        foreach(var price in new[]{r.TakeProfit,r.StopLoss})if(price.HasValue)Amount(price.Value,2,"Trigger price");
        var last=await db.Trades.Where(t=>t.InstrumentId==r.Instrument).OrderByDescending(t=>t.Sequence).Select(t=>(decimal?)t.Price).FirstOrDefaultAsync();
        if(last==null)throw new RuleException("A last traded price is required to set TP/SL.");
        var direction=Math.Sign(p.Quantity);
        if(r.TakeProfit.HasValue&&(r.TakeProfit.Value-last.Value)*direction<=0||r.StopLoss.HasValue&&(r.StopLoss.Value-last.Value)*direction>=0)throw new RuleException("TP/SL must be on the correct side of the current last traded price.");
        var item=existing??new PositionProtection{UserId=user,InstrumentId=r.Instrument,LifecycleId=p.LifecycleId};
        if(existing==null)db.Protections.Add(item);else item.Version++;
        item.TakeProfit=r.TakeProfit;item.StopLoss=r.StopLoss;item.Direction=direction;item.Status=r.TakeProfit==null&&r.StopLoss==null?"Cancelled":"Armed";item.TriggerLeg=null;item.TriggerSequence=null;item.Attempts=0;item.RetryDelayMs=100;item.Detail=null;
        return item;
    }
    public async Task<object> StopClosing(Guid user,StopClosingRequest r)
    {
        var item=await db.Protections.SingleOrDefaultAsync(x=>x.Id==r.Id&&x.UserId==user&&x.InstrumentId==r.Instrument)??throw new RuleException("Closing process not found.");
        if(item.Version!=r.Version)throw new RuleException("Closing process changed. Refresh and review again.");
        item.Status="Cancelled";item.Detail="Stopped by user";item.Version++;return item;
    }
    public async Task EvaluateProtection(Trade trade)
    {
        foreach(var item in await db.Protections.Where(x=>x.InstrumentId==trade.InstrumentId&&x.Status=="Armed").ToListAsync()){
            var p=await db.Positions.FindAsync(item.UserId,item.InstrumentId);
            if(p==null||p.Quantity==0||p.LifecycleId!=item.LifecycleId||Math.Sign(p.Quantity)!=item.Direction){item.Status="Cancelled";item.Version++;continue;}
            var tp=item.TakeProfit.HasValue&&(trade.Price-item.TakeProfit.Value)*item.Direction>=0;
            var sl=item.StopLoss.HasValue&&(trade.Price-item.StopLoss.Value)*item.Direction<=0;
            if(!tp&&!sl)continue;
            item.Status="Closing";item.TriggerLeg=tp?"Take profit":"Stop loss";item.TriggerSequence=trade.Sequence;item.NextAttemptAt=DateTime.UtcNow;item.Detail="Triggered; sibling cancelled";item.Version++;
        }
    }
    public async Task AttemptClose(Guid id)
    {
        var job=await db.Protections.FindAsync(id);
        if(job==null||job.Status!="Closing"||job.NextAttemptAt>DateTime.UtcNow)return;
        var p=await db.Positions.FindAsync(job.UserId,job.InstrumentId);var i=await db.Instruments.FindAsync(job.InstrumentId);
        if(p==null||p.Quantity==0||p.LifecycleId!=job.LifecycleId||Math.Sign(p.Quantity)!=job.Direction||i?.Status!="Trading"||i.Expiry<=DateTime.UtcNow){job.Status="Completed";job.Detail="Position ended or contract expired";job.Version++;return;}
        // Savepoint preserves durable retry state while rolling back any partially attempted accounting.
        var tx=db.Database.CurrentTransaction??throw new InvalidOperationException("Closing requires a transaction.");
        await tx.CreateSavepointAsync("closing_attempt");
        var filled=false;
        try{
            var order=(Order)await PlaceDerivative(job.UserId,new(p.Quantity>0?"Sell":"Buy","Market",Math.Abs(p.Quantity),null,p.InstrumentId,null,true),persistUnfilled:false);
            filled=order.Remaining<order.Quantity;
            order.ClosingJobId=job.Id;job.Detail=order.Remaining==0?"Close attempt filled":filled?"Partially filled; retrying remaining position":order.CancelReason is null or "Unfilled market remainder"?"Waiting for executable liquidity":order.CancelReason;
        }catch(RuleException e){
            await tx.RollbackToSavepointAsync("closing_attempt");db.ChangeTracker.Clear();job=(await db.Protections.FindAsync(id))!;job.Detail=e.Message;
        }
        job.Attempts++;job.RetryDelayMs=filled?100:Math.Min(1000,Math.Max(100,job.RetryDelayMs)*2);
        // Schedule from completion, not start: slow attempts must not create a catch-up loop.
        job.NextAttemptAt=DateTime.UtcNow.AddMilliseconds(job.RetryDelayMs);
    }
    public async Task<object> AmendOrder(Guid user,AmendOrderRequest r)
    {
        var old=await db.Orders.FindAsync(r.Id)??throw new RuleException("Order not found.");
        if(old.UserId!=user||old.InstrumentId!=r.Instrument)throw new RuleException("Order not found.");
        if(old.Version!=r.Version||old.Type!="Limit"||old.Status is not("Open" or "PartiallyFilled"))throw new RuleException("Order changed. Refresh and review again.");
        Amount(r.Price,2,"Limit price");if(old.Price==r.Price)return old;
        var qty=old.Remaining;await Cancel(user,old.Id);old.CancelReason="Amended";
        var next=(Order)await Place(user,new(old.Side,"Limit",qty,r.Price,old.InstrumentId,null,old.ReduceOnly));next.ReplacesId=old.Id;await db.SaveChangesAsync();if(!old.ReduceOnly)await CheckRisk(user);return next;
    }
    public async Task<List<PositionView>> PositionViews(Guid user,RiskSnapshot risk)
    {
        var result=new List<PositionView>();
        foreach(var p in await db.Positions.AsNoTracking().Where(x=>x.UserId==user).ToListAsync()){
            var i=(await db.Instruments.FindAsync(p.InstrumentId))!;
            var last=await db.Trades.Where(t=>t.InstrumentId==i.Id).OrderByDescending(t=>t.Sequence).Select(t=>(decimal?)t.Price).FirstOrDefaultAsync();
            decimal? mark=risk.PriceFresh?await Mark(i,risk.IndexPrice):null;
            var basis=i.Kind!="Option"?Math.Abs(p.Cost)/p.Leverage:p.Quantity>0?Math.Abs(p.Cost):i.OptionSide=="Put"?Math.Abs(p.Quantity)*i.Strike:Math.Abs(p.Quantity)*risk.IndexPrice;
            var pnl=mark*p.Quantity-p.Cost;
            var denominator=p.Quantity-Math.Abs(p.Quantity)*0.005m;
            var threshold=mark.HasValue&&denominator!=0?(risk.MaintenanceMargin-risk.Equity+denominator*mark.Value)/denominator:(decimal?)null;
            var liq=i.Kind=="Option"?null:threshold>0?threshold:null;
            var protection=await db.Protections.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==user&&x.InstrumentId==i.Id&&x.LifecycleId==p.LifecycleId);
            result.Add(new(p.InstrumentId,i.Kind,p.Quantity,p.EntryPrice,p.Cost,p.Leverage,i.Kind=="Option"&&p.Quantity!=0?p.TradeRealizedPnl:p.RealizedPnl,p.LifecycleId,p.Version,last,p.EntryPrice>0?(last/p.EntryPrice-1)*100:null,mark,Math.Abs(p.Quantity)*mark,pnl,basis>0?pnl/basis*100:null,basis,liq,p.Quantity==0?"No open position":i.Kind=="Option"?"Not applicable":!risk.PriceFresh?"Pricing unavailable":liq==null?"No positive threshold":"Approximate; other marks and collateral held constant",protection,p.UserId,p.Reserved,p.UpdatedAt));
        }
        return result;
    }
}
