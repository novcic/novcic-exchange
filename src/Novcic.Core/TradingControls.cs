using Microsoft.EntityFrameworkCore;
namespace Novcic.Core;
public record LeverageRequest(int Leverage);
public record OrderPreviewRequest(string Side, string Type, decimal? Price, string Instrument = "BTCUSDT", bool ReduceOnly = false, decimal Quantity = 0);
public record OrderPreview(decimal MaximumQuantity, decimal Quantity, decimal Fee, string FeeAsset, decimal NetReceipt, decimal OrderCost, string OrderCostAsset, long Revision, string? Error, decimal? ReferencePrice, string PriceSource);
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
        foreach(var p in await db.Positions.Where(x=>x.UserId==user&&futures.Contains(x.InstrumentId)).ToListAsync()){p.Leverage=request.Leverage;p.Version++;}
        foreach(var o in await db.Orders.Where(x=>x.UserId==user&&futures.Contains(x.InstrumentId)&&(x.Status=="Open"||x.Status=="PartiallyFilled")).ToListAsync())o.Leverage=request.Leverage;
        await db.SaveChangesAsync();await CheckRisk(user);
        return new {leverage=account.Leverage,risk=await Risk(user)};
    }
    public async Task<OrderPreview> Preview(Guid user, OrderPreviewRequest request)
    {
        if(request.Side is not ("Buy" or "Sell")||request.Type is not ("Limit" or "Market"))throw new RuleException("Choose Buy/Sell and Limit/Market.");
        if(request.Type=="Limit")Amount(request.Price??0,2,"Limit price");
        if(request.Type=="Market"&&request.Price!=null)throw new RuleException("Market orders do not accept a limit price.");
        if(request.Quantity<0)throw new RuleException("Preview quantity must be nonnegative.");
        if(request.Quantity>0)Amount(request.Quantity,6,"BTC quantity");
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var risk=await Risk(user);var i=await db.Instruments.FindAsync(request.Instrument)??throw new RuleException("Unknown instrument.");
        var price=request.Type=="Limit"?request.Price:await db.Trades.Where(t=>t.InstrumentId==i.Id).OrderByDescending(t=>t.Sequence).Select(t=>(decimal?)t.Price).FirstOrDefaultAsync();
        var source=request.Type=="Limit"?"Limit price":"Last traded price";
        if(price==null){price=i.Kind=="Option"?await QuoteMark(i,risk.IndexPrice):risk.IndexPrice;source=i.Kind=="Option"?"Theoretical premium":"Reference index";}
        var wallets=await db.Wallets.Where(w=>w.UserId==user&&w.Kind=="Unified Trading").ToListAsync();
        var usd=wallets.FirstOrDefault(w=>w.Asset=="USDT")?.Available??0;var btc=wallets.FirstOrDefault(w=>w.Asset=="BTC")?.Available??0;
        var position=await db.Positions.FindAsync(user,i.Id);
        var result=Estimate(request,i,price??0,risk,usd,btc,await AccountLeverage(user),position?.Quantity??0,settings??new AmmSettings());
        return result with { Revision=await db.Revisions.Where(x=>x.Id==1).Select(x=>x.Revision).SingleAsync(),ReferencePrice=price,PriceSource=source };
    }
    public static OrderPreview Estimate(OrderPreviewRequest r,Instrument i,decimal price,RiskSnapshot risk,decimal usd,decimal btc,int leverage,decimal position,AmmSettings rates)
    {
        var spot=i.Kind=="Spot";var feeAsset=spot&&r.Side=="Buy"?"BTC":"USDT";
        var costAsset=spot&&r.Side=="Sell"?"BTC":"USDT";
        if(price<=0||(i.Kind=="Option"&&risk.IndexPrice<=0))return new(0,r.Quantity,0,feeAsset,0,0,costAsset,0,"Price unavailable",null,"Unavailable");
        var feeUnit=i.Kind=="Spot"?(r.Side=="Buy"?1:price)*rates.SpotFee:i.Kind=="Option"?Math.Min(risk.IndexPrice*rates.OptionFee,price*rates.OptionPremiumCap):price*rates.FuturesFee;
        var free=Math.Max(0,risk.Equity-risk.InitialMargin);var cash=Math.Max(0,Math.Min(usd,risk.TransferableUsdt));decimal max,costUnit;
        if(spot){costUnit=r.Side=="Buy"?price:1;max=r.Side=="Buy"?cash/price:Math.Min(btc,risk.TransferableBtc);}
        else if(r.ReduceOnly){costUnit=feeUnit;max=position*(r.Side=="Buy"?1:-1)<0?Math.Abs(position):0;}
        else if(i.Kind=="Option"){
            costUnit=(r.Side=="Buy"?price:i.OptionSide=="Put"?i.Strike??0:0)+feeUnit;
            max=i.OptionSide=="Call"&&r.Side=="Sell"?Math.Min(btc,risk.TransferableBtc):costUnit>0?cash/costUnit:0;
            if(feeUnit>0)max=Math.Min(max,cash/feeUnit);
            if(costUnit>0)max=Math.Min(max,free/costUnit);
        }else{costUnit=price/leverage+feeUnit;max=free/costUnit;if(feeUnit>0)max=Math.Min(max,cash/feeUnit);}
        if(!spot&&!risk.PriceFresh)max=0;
        max=decimal.Floor(Math.Clamp(max,0,1_000_000_000m)*1_000_000m)/1_000_000m;
        var quantity=r.Quantity>0?r.Quantity:max;var fee=Down(quantity*feeUnit);
        var net=spot?quantity*(r.Side=="Buy"?1:price)-fee:0;
        var cost=spot?quantity*costUnit:Down(quantity*(costUnit-feeUnit))+fee;
        return new(max,quantity,fee,feeAsset,net,cost,costAsset,0,quantity>max?"Quantity exceeds estimated available balance.":null,price,"Estimate");
    }
}
