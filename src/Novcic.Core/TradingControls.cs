using Microsoft.EntityFrameworkCore;
namespace Novcic.Core;
public record LeverageRequest(int Leverage);
public record OrderPreviewRequest(string Side, string Type, decimal? Price, string Instrument = "BTCUSDT", bool ReduceOnly = false, decimal Quantity = 0);
public record OrderPreview(decimal MaximumQuantity, decimal Quantity, decimal Fee, string FeeAsset, decimal NetReceipt, decimal OrderCost, string OrderCostAsset, long Revision, string? Error);
public partial class TradingService
{
    public async Task<int> AccountLeverage(Guid user) => (await db.AccountSettings.FindAsync(user))?.Leverage ?? 1;
    public async Task<object> PreviewLeverage(Guid user, LeverageRequest request)
    {
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001234)");
        try{return await SetLeverage(user,request);}finally{await tx.RollbackAsync();db.ChangeTracker.Clear();}
    }
    public async Task<object> SetLeverage(Guid user, LeverageRequest request)
    {
        if(request.Leverage<1||request.Leverage>10)throw new RuleException("Cross leverage must be between 1 and 10.");
        var account=await db.AccountSettings.FindAsync(user);
        if(account==null){account=new AccountSettings{UserId=user};db.AccountSettings.Add(account);}
        account.Leverage=request.Leverage;
        var futures=await db.Instruments.Where(x=>x.Kind=="Perpetual"||x.Kind=="Future").Select(x=>x.Id).ToListAsync();
        foreach(var p in await db.Positions.Where(x=>x.UserId==user&&futures.Contains(x.InstrumentId)).ToListAsync())p.Leverage=request.Leverage;
        foreach(var o in await db.Orders.Where(x=>x.UserId==user&&futures.Contains(x.InstrumentId)&&(x.Status=="Open"||x.Status=="PartiallyFilled")).ToListAsync())o.Leverage=request.Leverage;
        await db.SaveChangesAsync();await CheckRisk(user);
        return new {leverage=account.Leverage,risk=await Risk(user)};
    }
    // Evaluate the actual matching/accounting path in rollback-only savepoints. No
    // wallet, order, trade, receipt or revision is committed by a preview.
    public async Task<OrderPreview> Preview(Guid user, OrderPreviewRequest request)
    {
        if(request.Side is not ("Buy" or "Sell")||request.Type is not ("Limit" or "Market"))throw new RuleException("Choose Buy/Sell and Limit/Market.");
        if(request.Type=="Limit")Amount(request.Price??0,2,"Limit price");
        if(request.Type=="Market"&&request.Price!=null)throw new RuleException("Market orders do not accept a limit price.");
        if(request.Quantity<0)throw new RuleException("Preview quantity must be nonnegative.");
        if(request.Quantity>0)Amount(request.Quantity,6,"BTC quantity");
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001234)");
        var baselineRisk=await Risk(user);
        var revision=await db.Revisions.Where(x=>x.Id==1).Select(x=>x.Revision).SingleAsync();
        var instrument=await db.Instruments.FindAsync(request.Instrument)??throw new RuleException("Unknown instrument.");
        var feeAsset=instrument.Kind=="Spot"&&request.Side=="Buy"?"BTC":"USDT";
        var book=await db.Orders.Where(x=>x.InstrumentId==request.Instrument&&x.Side!=request.Side&&(x.Status=="Open"||x.Status=="PartiallyFilled")).ToListAsync();
        decimal cap=request.Type=="Market"?Math.Min(1_000_000_000m,book.Sum(x=>x.Remaining)):1_000_000_000m;
        if(instrument.Kind=="Spot"&&request.Type=="Limit"&&request.Price>0){var wallet=await db.Wallets.FindAsync(user,"Unified Trading",request.Side=="Buy"?"USDT":"BTC");cap=Math.Min(cap,(wallet?.Available??0)/(request.Side=="Buy"?request.Price.Value:1));}
        if(request.ReduceOnly){var position=await db.Positions.FindAsync(user,request.Instrument);cap=Math.Min(cap,Math.Abs(position?.Quantity??0));}
        string? error=null;
        async Task<(bool Fits,decimal Fee,decimal Net,decimal Cost)> Evaluate(decimal quantity)
        {
            await tx.CreateSavepointAsync("preview");
            try {
                await EnsureWallets(user);
                var before=(await Wallet(user,"USDT")).Available;
                var order=(Order)await Place(user,new PlaceOrder(request.Side,request.Type,quantity,request.Price,request.Instrument,null,request.ReduceOnly));
                await db.SaveChangesAsync();
                if(request.Type=="Market"&&order.Remaining>0)throw new RuleException("Insufficient executable liquidity for this quantity.");
                if(!request.ReduceOnly)await CheckRisk(user);
                var fills=await db.Trades.Where(x=>x.TakerOrderId==order.Id).ToListAsync();
                var fee=fills.Sum(x=>request.Side=="Buy"?x.BuyerFee??0:x.SellerFee??0);
                if(order.Remaining>0&&order.Type=="Limit")fee+=ExecutionFee(instrument.Kind,request.Side,order.Remaining,request.Price!.Value,baselineRisk.IndexPrice,settings??new AmmSettings());
                var net=instrument.Kind=="Spot"?fills.Sum(x=>request.Side=="Buy"?x.Quantity:x.Quantity*x.Price)+(order.Type=="Limit"?order.Remaining*(request.Side=="Buy"?1:request.Price!.Value):0)-fee:0;
                var cost=instrument.Kind=="Spot"?(request.Side=="Buy"?order.FilledNotional+order.Reserved:order.Quantity-order.Remaining+order.Reserved):Math.Max(0,before-(await Wallet(user,"USDT")).Available);
                if(instrument.Kind!="Spot")cost+=Math.Max(0,(await Risk(user)).InitialMargin-baselineRisk.InitialMargin);
                return (true,fee,net,cost);
            } catch(RuleException ex){error=ex.Message;return (false,0,0,0);}
            finally{await tx.RollbackToSavepointAsync("preview");db.ChangeTracker.Clear();}
        }
        long low=0,high=(long)decimal.Floor(cap*1_000_000m);
        while(low<high){var mid=low+(high-low+1)/2;var result=await Evaluate(mid/1_000_000m);if(result.Fits)low=mid;else high=mid-1;}
        var max=low/1_000_000m;var selected=request.Quantity>0?request.Quantity:max;
        var estimate=selected>0?await Evaluate(selected):(false,0m,0m,0m);
        await tx.RollbackAsync();
        return new(max,selected,estimate.Item2,feeAsset,estimate.Item3,estimate.Item4,instrument.Kind=="Spot"&&request.Side=="Sell"?"BTC":"USDT",revision,estimate.Item1?null:error??"No executable balance or liquidity.");
    }
}
