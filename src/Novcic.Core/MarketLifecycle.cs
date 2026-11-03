using Microsoft.EntityFrameworkCore;
namespace Novcic.Core;
public partial class TradingService
{
    public async Task ProcessMarkets()
    {
        if(!await db.Instruments.AnyAsync())db.Instruments.AddRange(new Instrument(),new Instrument{Id="BTC-PERP",Kind="Perpetual"});
        await db.SaveChangesAsync();
        var sample=await db.IndexSamples.OrderByDescending(x=>x.Id).FirstOrDefaultAsync();
        if(sample==null||DateTime.UtcNow-sample.Time>TimeSpan.FromSeconds(5))return;
        var now=DateTime.UtcNow;var friday=now.Date.AddHours(8);while(friday<=now||friday.DayOfWeek!=DayOfWeek.Friday)friday=friday.AddDays(1);
        var month=new DateTime(now.Year,now.Month,DateTime.DaysInMonth(now.Year,now.Month),8,0,0,DateTimeKind.Utc);while(month.DayOfWeek!=DayOfWeek.Friday)month=month.AddDays(-1);
        if(month<=now){var next=now.AddMonths(1);month=new DateTime(next.Year,next.Month,DateTime.DaysInMonth(next.Year,next.Month),8,0,0,DateTimeKind.Utc);while(month.DayOfWeek!=DayOfWeek.Friday)month=month.AddDays(-1);}
        foreach(var expiry in new[]{friday,month}.Distinct()) {
            var id=$"BTC-{expiry:yyyyMMdd}";
            if(await db.Instruments.FindAsync(id)==null){
                db.Instruments.Add(new Instrument{Id=id,Kind="Future",Expiry=expiry});
                var center=decimal.Round(sample.Price/100m,0,MidpointRounding.AwayFromZero)*100;
                foreach(var step in new[]{-2,-1,0,1,2})foreach(var side in new[]{"Call","Put"}){
                    var strike=decimal.Round(center*(1+step*0.05m)/100,0,MidpointRounding.AwayFromZero)*100;
                    db.Instruments.Add(new Instrument{Id=$"{id}-{strike:0}-{side[0]}",Kind="Option",Expiry=expiry,Strike=strike,OptionSide=side});
                }
            }
        }
        await db.SaveChangesAsync();
        foreach(var instrument in await db.Instruments.Where(x=>x.Status=="Trading"&&x.Expiry<=now).ToListAsync()) {
            var start=instrument.Expiry!.Value.AddMinutes(-30);
            var observations=await db.IndexSamples.Where(x=>x.Time>=start.AddSeconds(-5)&&x.Time<=instrument.Expiry).OrderBy(x=>x.Time).ToListAsync();
            var twap=PriceWindows.Twap(observations,start,instrument.Expiry.Value);
            if(!twap.HasValue)continue;
            var settlement=twap.Value;instrument.SettlementPrice=settlement;instrument.Status="Settled";
            foreach(var order in await db.Orders.Where(x=>x.InstrumentId==instrument.Id&&(x.Status=="Open"||x.Status=="PartiallyFilled")).ToListAsync()){order.Status="Cancelled";order.CancelReason="Contract expiry";}
            foreach(var p in await db.Positions.Where(x=>x.InstrumentId==instrument.Id&&x.Quantity!=0).ToListAsync()) {
                var price=instrument.Kind=="Option"?(instrument.OptionSide=="Call"?Math.Max(0,settlement-instrument.Strike!.Value):Math.Max(0,instrument.Strike!.Value-settlement)):settlement;
                var amount=decimal.Round(instrument.Kind=="Option"?p.Quantity*price:p.Quantity*price-p.Cost,8,MidpointRounding.ToEven);
                await Cash(p.UserId,amount,Guid.NewGuid());p.RealizedPnl+=amount;p.Quantity=0;p.Cost=0;p.UpdatedAt=now;
            }
        }
        var interval=new DateTime(now.Ticks/(TimeSpan.TicksPerHour*8)*(TimeSpan.TicksPerHour*8),DateTimeKind.Utc);
        if(await db.FundingEvents.FindAsync("BTC-PERP",interval)==null) {
            var samples=await db.Trades.Where(x=>x.InstrumentId=="BTC-PERP"&&x.CreatedAt>=interval.AddHours(-8)&&x.CreatedAt<interval).ToListAsync();
            var indexHistory=await db.IndexSamples.Where(x=>x.Time>=interval.AddHours(-8).AddSeconds(-5)&&x.Time<=interval).ToListAsync();
            var reference=PriceWindows.Twap(indexHistory,interval.AddHours(-8),interval);
            var rate=reference.HasValue&&samples.Count>0?decimal.Round(Math.Clamp(samples.Average(x=>x.Price)/reference.Value-1,-0.0005m,0.0005m),8):0;
            if(rate!=0) {
                // Reconstruct ownership at the boundary, including trades after restart.
                var history=await db.Trades.Where(x=>x.InstrumentId=="BTC-PERP"&&x.CreatedAt<interval).ToListAsync();
                var holdings=history.SelectMany(t=>new[]{(User:t.BuyerId,Qty:t.Quantity),(User:t.SellerId,Qty:-t.Quantity)}).GroupBy(x=>x.User);
                foreach(var h in holdings)await Cash(h.Key,-h.Sum(x=>x.Qty)*reference!.Value*rate,Guid.NewGuid());
            }
            db.FundingEvents.Add(new FundingEvent{InstrumentId="BTC-PERP",Time=interval,Rate=rate});
        }
        foreach(var pool in await db.LendingPools.ToListAsync()){await Accrue(pool);await RedeemQueue(pool);}
        await db.SaveChangesAsync();
        await CollectRealizedDebts();
    }
    public async Task LiquidatePositions(Guid user)
    {
            var risk=await Risk(user);if(!risk.PriceFresh||!risk.Restricted)return;
            foreach(var order in await db.Orders.Where(x=>x.UserId==user&&(x.Status=="Open"||x.Status=="PartiallyFilled")&&!x.ReduceOnly).ToListAsync())await Cancel(user,order.Id);
            foreach(var p in await db.Positions.Where(x=>x.UserId==user&&x.Quantity!=0).ToListAsync()) {
                var i=await db.Instruments.FindAsync(p.InstrumentId);if(i!.Kind=="Option")continue;
                var quantity=decimal.Ceiling(Math.Abs(p.Quantity)*0.25m*1000000m)/1000000m;
                // A reduce-only market order uses real resting liquidity. No synthetic liquidation fills.
                await PlaceDerivative(user,new PlaceOrder(p.Quantity>0?"Sell":"Buy","Market",Math.Min(quantity,Math.Abs(p.Quantity)),null,p.InstrumentId,p.Leverage,true));
            }
            if(!await db.RiskEvents.AnyAsync(x=>x.UserId==user&&x.Type=="Liquidation"&&x.Time>DateTime.UtcNow.AddMinutes(-1)))
                db.RiskEvents.Add(new RiskEvent{UserId=user,Type="Liquidation",Detail="Maintenance breach: orders cancelled and partial reductions attempted against available liquidity."});
    }
    public async Task RunDca(Guid id)
    {
        var now=DateTime.UtcNow;
        foreach(var schedule in await db.DcaSchedules.Where(x=>x.Id==id&&!x.Paused&&x.NextRun<=now).ToListAsync()) {
            var funds=await Wallet(schedule.UserId,"USDT");var budget=schedule.Budget.HasValue?Math.Min(schedule.Amount,schedule.Budget.Value-schedule.Spent):schedule.Amount;
            schedule.NextRun=now.AddHours(schedule.Hours);
            if(budget<=0){schedule.Paused=true;schedule.LastOutcome="Budget complete";continue;}
            if(funds.Available<budget){schedule.LastOutcome="Suspended: insufficient funds";continue;}
            var asks=await db.Orders.Where(x=>x.InstrumentId=="BTCUSDT"&&x.Side=="Sell"&&(x.Status=="Open"||x.Status=="PartiallyFilled")).OrderBy(x=>x.Price).ThenBy(x=>x.Sequence).ToListAsync();
            decimal quantity=0,remaining=budget;
            foreach(var ask in asks){if(ask.UserId==schedule.UserId)break;var qty=Math.Min(ask.Remaining,decimal.Floor(remaining/(ask.Price!.Value*(ask.UserId==AmmAccount?1+(settings??new AmmSettings()).SpotFee:1))*1000000m)/1000000m);quantity+=qty;remaining-=qty*ask.Price.Value*(ask.UserId==AmmAccount?1+(settings??new AmmSettings()).SpotFee:1);}
            if(quantity<=0){schedule.LastOutcome="Suspended: no liquidity";continue;}
            var order=(Order)await Place(schedule.UserId,new PlaceOrder("Buy","Market",quantity,null));
            await db.SaveChangesAsync();var spent=await db.Trades.Where(x=>x.TakerOrderId==order.Id).SumAsync(x=>x.Price*x.Quantity);
            await CheckRisk(schedule.UserId);
            spent-=await db.Ledger.Where(x=>x.ReferenceId==order.Id&&x.UserId==schedule.UserId&&x.Reason=="AMM execution fee").SumAsync(x=>x.AvailableDelta);
            schedule.Spent+=spent;schedule.LastOutcome=$"Purchased {order.Quantity-order.Remaining:0.######} BTC for {spent:0.########} USDT";
        }
    }
}
