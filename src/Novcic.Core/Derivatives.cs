using Microsoft.EntityFrameworkCore;
namespace Novcic.Core;
public record RiskSnapshot(decimal Equity, decimal InitialMargin, decimal MaintenanceMargin, decimal TransferableUsdt, decimal TransferableBtc, decimal UnrealizedPnl, decimal RealizedCash, decimal IndexPrice, bool PriceFresh, bool Restricted);
public partial class TradingService
{
    public async Task<IndexSample> FreshIndex()
    {
        var sample=await db.IndexSamples.OrderByDescending(x=>x.Id).FirstOrDefaultAsync();
        if(sample==null||DateTime.UtcNow-sample.Time>TimeSpan.FromSeconds(5))throw new RuleException("Reference price is stale. Risk-increasing actions are paused.");
        return sample;
    }
    public async Task<decimal> Mark(Instrument instrument,decimal index)
    {
        if(instrument.Kind=="Spot")return index;
        if(instrument.Kind=="Option"){var theoretical=await QuoteMark(instrument,index);if(theoretical.HasValue)return theoretical.Value;}
        if(instrument.Kind=="Option")return instrument.OptionSide=="Call"?Math.Max(0,index-instrument.Strike!.Value):Math.Max(0,instrument.Strike!.Value-index);
        var since=DateTime.UtcNow.AddSeconds(-150);
        var prices=await db.Trades.Where(x=>x.InstrumentId==instrument.Id&&x.CreatedAt>=since).Select(x=>x.Price).ToListAsync();
        return prices.Count==0?index:Math.Clamp(prices.Average(),index*0.99m,index*1.01m);
    }
    public async Task<RiskSnapshot> Risk(Guid user)
    {
        var sample=await db.IndexSamples.OrderByDescending(x=>x.Id).FirstOrDefaultAsync();
        var fresh=sample!=null&&DateTime.UtcNow-sample.Time<=TimeSpan.FromSeconds(5);
        var index=sample?.Price??0;
        var wallets=await db.Wallets.Where(x=>x.UserId==user&&x.Kind=="Unified Trading").ToListAsync();
        var usd=wallets.SingleOrDefault(x=>x.Asset=="USDT")?.Available??0;
        var btc=wallets.SingleOrDefault(x=>x.Asset=="BTC")?.Available??0;
        var cash=(await db.MarginBalances.FindAsync(user))?.Cash??0;
        decimal initial=0,maintenance=0,unrealized=0,callBtc=0,putUsdt=0,coveredOptionLiability=0;
        var instruments=await db.Instruments.ToDictionaryAsync(x=>x.Id);
        foreach(var p in await db.Positions.Where(x=>x.UserId==user&&x.Quantity!=0).ToListAsync()) {
            var instrument=instruments[p.InstrumentId];if(instrument.Kind=="Option"&&await QuoteMark(instrument,index)==null)fresh=false;var mark=await Mark(instrument,index);
            if(instrument.Kind=="Option") {
                // Long premium is already paid. Covered obligations are excluded
                // from collateral equity rather than reused for futures margin.
                if(p.Quantity>0)unrealized+=p.Quantity*mark;
                else {unrealized+=p.Quantity*mark;coveredOptionLiability-=p.Quantity*mark;if(instrument.OptionSide=="Call")callBtc+=-p.Quantity;else {var cover=-p.Quantity*instrument.Strike!.Value;initial+=cover;putUsdt+=cover;}}
            } else {unrealized+=p.Quantity*mark-p.Cost;initial+=Math.Abs(p.Quantity)*mark/p.Leverage;maintenance+=Math.Abs(p.Quantity)*mark*0.005m;}
        }
        foreach(var order in await db.Orders.Where(x=>x.UserId==user&&x.InstrumentId!="BTCUSDT"&&(x.Status=="Open"||x.Status=="PartiallyFilled")).ToListAsync()) {
            var fee=ExecutionFee(instruments[order.InstrumentId].Kind,order.Side,order.Remaining,order.Price??index,index,settings??new AmmSettings());
            initial+=fee;
            putUsdt+=fee;
            if(order.ReduceOnly)continue;
            var i=instruments[order.InstrumentId];
            if(i.Kind=="Option") {if(order.Side=="Buy")initial+=order.Remaining*order.Price!.Value;else if(i.OptionSide=="Call")callBtc+=order.Remaining;else {var cover=order.Remaining*i.Strike!.Value;initial+=cover;putUsdt+=cover;}}
            else initial+=order.Remaining*index/order.Leverage;
        }
        var equity=usd+Math.Max(0,btc-callBtc)*index*0.9m+cash+unrealized+coveredOptionLiability;
        var restricted=equity<maintenance||btc<callBtc||usd<putUsdt;
        var transferableBtc=index>0?Down(Math.Max(0,Math.Min(btc-callBtc,(equity-initial)/(index*0.9m)))):0;
        var hasExposure=await db.Positions.AnyAsync(x=>x.UserId==user&&x.Quantity!=0)||await db.Orders.AnyAsync(x=>x.UserId==user&&x.InstrumentId!="BTCUSDT"&&(x.Status=="Open"||x.Status=="PartiallyFilled"))||cash<0;
        var transferableUsd=Down(Math.Max(0,Math.Min(usd-putUsdt,equity-initial)));
        if(!hasExposure){transferableBtc=btc;transferableUsd=usd;}
        else if(!fresh||restricted){transferableBtc=0;transferableUsd=0;}
        return new(equity,initial,maintenance,transferableUsd,transferableBtc,unrealized,cash,index,fresh,restricted);
    }
    private async Task CheckRisk(Guid user)
    {
        var exposure=await db.Positions.AnyAsync(x=>x.UserId==user&&x.Quantity!=0)||await db.Orders.AnyAsync(x=>x.UserId==user&&x.InstrumentId!="BTCUSDT"&&(x.Status=="Open"||x.Status=="PartiallyFilled"));
        if(!exposure && ((await db.MarginBalances.FindAsync(user))?.Cash??0)>=0)return;
        var risk=await Risk(user);
        if(!risk.PriceFresh||risk.Restricted||risk.Equity<risk.InitialMargin)throw new RuleException("Insufficient cross-margin collateral or unavailable reference price.");
    }
    private async Task Cash(Guid user,decimal delta,Guid reference)
    {
        delta=decimal.Round(delta,8,MidpointRounding.ToEven);
        var balance=await db.MarginBalances.FindAsync(user);
        if(balance==null){balance=new MarginBalance{UserId=user};db.MarginBalances.Add(balance);}
        balance.Cash+=delta;
        if(delta!=0) {
            db.Journal.Add(new JournalLine{ReferenceId=reference,Account=$"margin:{user}",Asset="USDT",Delta=delta});
            db.Journal.Add(new JournalLine{ReferenceId=reference,Account="system:derivatives-clearing",Asset="USDT",Delta=-delta});
        }
    }
    public async Task<object> SettleCash(Guid user)
    {
        await CollectRealizedDebts();
        var margin=await db.MarginBalances.FindAsync(user);var funds=await Wallet(user,"USDT");
        if(margin==null||margin.Cash==0)return new {message="No realized cash to settle."};
        var pool=await Clearing();
        var amount=margin.Cash>0?Math.Min(margin.Cash,pool.Cash):-Math.Min(-margin.Cash,funds.Available);var id=Guid.NewGuid();
        if(amount==0)throw new RuleException("Realized profit is awaiting funded settlement liquidity.");
        Move(funds,amount,0,"Derivative cash settlement",id);await Cash(user,-amount,id);
        pool.Cash-=amount;SystemPost("derivatives:cash","USDT",-amount,id);
        return new {message="Realized cash settled.",amount,pending=margin.Cash};
    }
    private async Task<ClearingPool> Clearing()
    {
        var pool=await db.ClearingPools.FindAsync(1);
        if(pool==null){pool=new ClearingPool();db.ClearingPools.Add(pool);}
        return pool;
    }
    // Only actual USDT collected from debtors can fund realized-profit withdrawals.
    // Uncollected liabilities remain in cross-margin equity; no custody money is minted.
    public async Task CollectRealizedDebts()
    {
        await db.SaveChangesAsync();
        var pool=await Clearing();
        foreach(var balance in await db.MarginBalances.Where(x=>x.Cash<0).ToListAsync()) {
            var wallet=await Wallet(balance.UserId,"USDT");
            if(wallet.Available<-balance.Cash)await ConvertDebtCollateral(balance.UserId,-balance.Cash-wallet.Available);
            var amount=Math.Min(wallet.Available,-balance.Cash);
            if(amount==0)continue;
            var id=Guid.NewGuid();Move(wallet,-amount,0,"Derivative cash obligation",id);
            await Cash(balance.UserId,amount,id);pool.Cash+=amount;SystemPost("derivatives:cash","USDT",amount,id);
        }
        await db.SaveChangesAsync();
    }
    private async Task ConvertDebtCollateral(Guid user,decimal needed)
    {
        var sample=await db.IndexSamples.OrderByDescending(x=>x.Id).FirstOrDefaultAsync();
        if(sample==null||DateTime.UtcNow-sample.Time>TimeSpan.FromSeconds(5))return;
        var calls=await db.Instruments.Where(x=>x.Kind=="Option"&&x.OptionSide=="Call"&&x.Status=="Trading").Select(x=>x.Id).ToListAsync();
        var covered=-(await db.Positions.Where(x=>x.UserId==user&&x.Quantity<0&&calls.Contains(x.InstrumentId)).SumAsync(x=>x.Quantity));
        covered+=await db.Orders.Where(x=>x.UserId==user&&calls.Contains(x.InstrumentId)&&x.Side=="Sell"&&!x.ReduceOnly&&(x.Status=="Open"||x.Status=="PartiallyFilled")).SumAsync(x=>x.Remaining);
        var free=Math.Max(0,(await Wallet(user,"BTC")).Available-covered);
        var bids=await db.Orders.Where(x=>x.InstrumentId=="BTCUSDT"&&x.Side=="Buy"&&(x.Status=="Open"||x.Status=="PartiallyFilled")).OrderByDescending(x=>x.Price).ThenBy(x=>x.Sequence).ToListAsync();
        decimal quantity=0;
        foreach(var bid in bids){
            if(bid.UserId==user||bid.Price<sample.Price*0.9m||needed<=0)break;
            var qty=Math.Min(bid.Remaining,Math.Min(decimal.Floor((free-quantity)*1000000m)/1000000m,decimal.Ceiling(needed/bid.Price!.Value*1000000m)/1000000m));
            if(qty<=0)break;quantity+=qty;needed-=qty*bid.Price.Value;
        }
        if(quantity>0)await Place(user,new PlaceOrder("Sell","Market",quantity,null));
    }
    public async Task<object> PlaceDerivative(Guid user,PlaceOrder r)
    {
        if(r.Side is not("Buy" or "Sell")||r.Type is not("Limit" or "Market")||r.Leverage<1||r.Leverage>10)throw new RuleException("Choose a valid side, type and leverage between 1 and 10.");
        Amount(r.Quantity,6,"BTC quantity");if(r.Type=="Limit")Amount(r.Price??0,2,"Price");
        var instrument=await db.Instruments.FindAsync(r.Instrument)??throw new RuleException("Unknown instrument.");
        if(instrument.Status!="Trading"||instrument.Expiry<=DateTime.UtcNow)throw new RuleException("Instrument is no longer trading.");
        var leverage=instrument.Kind=="Option"?1:await AccountLeverage(user);
        if(instrument.Kind!="Option" && r.Leverage.HasValue && r.Leverage!=leverage)throw new RuleException("Use the account leverage endpoint to change cross leverage.");
        var reference=await FreshIndex();
        if(instrument.Kind!="Option"&&r.Type=="Limit"&&Math.Abs(r.Price!.Value/reference.Price-1)>0.1m)throw new RuleException("Derivative limit price must be within 10% of the reference index.");
        var position=await db.Positions.FindAsync(user,instrument.Id);
        var signed=r.Side=="Buy"?r.Quantity:-r.Quantity;
        if(r.ReduceOnly&&(position==null||position.Quantity*signed>=0||Math.Abs(signed)>Math.Abs(position.Quantity)))throw new RuleException("Reduce-only quantity exceeds the open position.");
        var opposing=await db.Orders.Where(x=>x.InstrumentId==instrument.Id&&x.Side!=r.Side&&(x.Status=="Open"||x.Status=="PartiallyFilled")).ToListAsync();
        opposing=(r.Side=="Buy"?opposing.OrderBy(x=>x.Price).ThenBy(x=>x.Sequence):opposing.OrderByDescending(x=>x.Price).ThenBy(x=>x.Sequence)).ToList();
        var order=new Order{UserId=user,InstrumentId=instrument.Id,Side=r.Side,Type=r.Type,Quantity=r.Quantity,Remaining=r.Quantity,Price=r.Type=="Limit"?r.Price:null,Leverage=leverage,ReduceOnly=r.ReduceOnly};
        db.Orders.Add(order);await db.SaveChangesAsync();
        foreach(var maker in opposing) {
            if(order.Remaining==0)break;
            if(r.Type=="Limit"&&(r.Side=="Buy"?maker.Price>r.Price:maker.Price<r.Price))break;
            if(maker.UserId==AmmAccount&&((maker.QuoteValidatedAt??maker.CreatedAt)<DateTime.UtcNow.AddSeconds(-3)||(instrument.Kind=="Option"&&await QuoteMark(instrument,reference.Price)==null))){maker.Status="Cancelled";maker.CancelReason="Expired AMM quote";continue;}
            var makerRisk=await Risk(maker.UserId);
            if((!maker.ReduceOnly&&(makerRisk.Restricted||makerRisk.Equity<makerRisk.InitialMargin))||(instrument.Kind!="Option"&&Math.Abs(maker.Price!.Value/reference.Price-1)>0.1m)) {
                maker.Status="Cancelled";maker.CancelReason="Risk or price-band restriction";await db.SaveChangesAsync();continue;
            }
            if(maker.UserId==user){order.CancelReason="Self-trade prevention";break;}
            var quantity=Math.Min(order.Remaining,maker.Remaining);var price=maker.Price!.Value;
            var makerPosition=await db.Positions.FindAsync(maker.UserId,instrument.Id);
            var makerSign=maker.Side=="Buy"?1:-1;
            if(maker.ReduceOnly&&(makerPosition==null||makerPosition.Quantity*makerSign>=0)){maker.Status="Cancelled";continue;}
            if(maker.ReduceOnly)quantity=Math.Min(quantity,Math.Abs(makerPosition!.Quantity));
            await FillPosition(order.UserId,instrument,quantity*(r.Side=="Buy"?1:-1),price,leverage,order.Id);
            await FillPosition(maker.UserId,instrument,quantity*makerSign,price,maker.Leverage,order.Id);
            order.Remaining-=quantity;maker.Remaining-=quantity;maker.Status=maker.Remaining==0?"Filled":"PartiallyFilled";
            var trade=new Trade{InstrumentId=instrument.Id,MakerOrderId=maker.Id,TakerOrderId=order.Id,BuyerId=r.Side=="Buy"?user:maker.UserId,SellerId=r.Side=="Sell"?user:maker.UserId,TakerSide=r.Side,Price=price,Quantity=quantity};
            trade.BuyerFee=await ChargeExecutionFee(trade.BuyerId,trade.SellerId,instrument,"Buy",quantity,price,trade.Id);
            trade.SellerFee=await ChargeExecutionFee(trade.SellerId,trade.BuyerId,instrument,"Sell",quantity,price,trade.Id);
            trade.BuyerFeeAsset=trade.SellerFeeAsset="USDT";db.Trades.Add(trade);
            order.FilledNotional+=price*quantity;maker.FilledNotional+=price*quantity;
            await db.SaveChangesAsync();await RecordPnlTrade(trade);await EvaluateProtection(trade);if(!maker.ReduceOnly)await CheckRisk(maker.UserId);
        }
        order.Status=order.Remaining==0?"Filled":order.Remaining<order.Quantity?"PartiallyFilled":"Open";
        if(order.Remaining>0&&(r.Type=="Market"||order.CancelReason!=null)){order.Status="Cancelled";order.CancelReason??="Unfilled market remainder";}
        await db.SaveChangesAsync();
        if(!r.ReduceOnly)await CheckRisk(user);
        await CollectRealizedDebts();
        return order;
    }
    private async Task FillPosition(Guid user,Instrument instrument,decimal signed,decimal price,int leverage,Guid reference)
    {
        var p=await db.Positions.FindAsync(user,instrument.Id);
        if(p==null){p=new Position{UserId=user,InstrumentId=instrument.Id,Leverage=leverage};db.Positions.Add(p);}
        await InitializePositionAnalytics(p,instrument);
        var priorQuantity=p.Quantity;
        if(instrument.Kind=="Option") {
            await Cash(user,-signed*price,reference);p.RealizedPnl-=signed*price;
            var basis=ApplyBasis(p.Quantity,p.Cost,signed,price);p.Quantity=basis.Quantity;p.Cost=basis.Cost;p.TradeRealizedPnl+=basis.Realized;
            p.EntryPrice=p.Quantity==0?0:decimal.Round(p.Cost/p.Quantity,16);
        }
        else {
            var previous=p.Quantity;var combined=previous+signed;
            if(previous*signed<0){
                var closed=Math.Min(Math.Abs(previous),Math.Abs(signed));
                var cost=closed==Math.Abs(previous)?p.Cost:decimal.Round(p.Cost*closed/Math.Abs(previous),8,MidpointRounding.ToEven);
                var pnl=closed*price*Math.Sign(previous)-cost;
                await Cash(user,pnl,reference);p.RealizedPnl+=pnl;p.Cost-=cost;
                if(previous*combined<0)p.Cost=combined*price;
            }else p.Cost+=signed*price;
            p.Quantity=combined;p.Leverage=leverage;
            p.EntryPrice=combined==0?0:decimal.Round(p.Cost/combined,16,MidpointRounding.ToEven);
        }
        if(priorQuantity!=0&&(p.Quantity==0||Math.Sign(priorQuantity)!=Math.Sign(p.Quantity)))await EndPositionProtection(p,p.Quantity==0?"Position closed":"Position reversed");
        if(priorQuantity==0||priorQuantity*p.Quantity<0)p.LifecycleId=Guid.NewGuid();
        p.Version++;p.UpdatedAt=DateTime.UtcNow;
    }
}
