using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Tests;
public partial class ProductTests
{
    [Theory]
    [InlineData("BTC-PERP",50000,51000)]
    [InlineData("CALL",1000,1200)]
    public async Task PnlLifecycleAggregatesPartialClosesAdditionsAndActualFees(string symbol,decimal entry,decimal exit)
    {
        await Order(bob,"Sell",0.1m,entry,symbol);await Order(alice,"Buy",0.1m,null,symbol);
        await Order(bob,"Buy",0.04m,exit,symbol,true);await Order(alice,"Sell",0.04m,null,symbol,true);
        await using(var db=Db())Assert.Empty((await new TradingService(db).PnlHistory(alice)).Items);
        await Order(bob,"Sell",0.02m,entry,symbol);await Order(alice,"Buy",0.02m,null,symbol);
        await Order(bob,"Buy",0.08m,exit,symbol,true);await Order(alice,"Sell",0.08m,null,symbol,true);
        await using var final=Db();var svc=new TradingService(final);var row=Assert.Single((await svc.PnlHistory(alice)).Items);
        Assert.Equal(0.12m,row.Quantity);Assert.Equal(entry,row.AverageEntry);Assert.Equal(exit,row.AverageExit);Assert.Equal((exit-entry)*0.12m,row.GrossPnl);
        var trades=await final.Trades.Where(t=>t.InstrumentId==symbol).ToListAsync();var fees=trades.Sum(t=>t.BuyerId==alice?t.BuyerFee!.Value:t.SellerFee!.Value);
        Assert.Equal(fees,row.OpeningFee+row.ClosingFee);Assert.Equal(row.GrossPnl-fees,row.NetPnl);Assert.True(row.ClosedAt>=row.OpenedAt);
        var count=await final.PnlLifecycles.CountAsync();await svc.RecordPnlTrade(trades.Last());await svc.BackfillPnl();Assert.Equal(count,await final.PnlLifecycles.CountAsync());await Conserve();
    }
    [Fact] public async Task ReversalsSplitTheChargedFeeAndFundingCanReachClosedLifecycles()
    {
        await Order(bob,"Sell",0.1m,50000);await Order(alice,"Buy",0.1m,null);
        var boundary=DateTime.UtcNow;
        await Order(bob,"Buy",0.15m,51000);await Order(alice,"Sell",0.15m,null);
        await using var db=Db();var svc=new TradingService(db);var old=await db.PnlLifecycles.SingleAsync(x=>x.UserId==alice&&x.ClosedAt!=null);var next=await db.PnlLifecycles.SingleAsync(x=>x.UserId==alice&&x.ClosedAt==null);
        var fee=(await db.Trades.OrderByDescending(x=>x.Sequence).FirstAsync()).SellerFee!.Value;
        Assert.Equal(fee,old.ClosingFee+next.OpeningFee);Assert.Equal(-1,next.Direction);Assert.Equal(0.05m,next.Remaining);
        await svc.AttributePnlFunding(alice,"BTC-PERP",boundary,-0.123456789m);await db.SaveChangesAsync();
        Assert.Equal(-0.12345679m,old.Funding);Assert.Equal(0,next.Funding);
        Assert.Equal(100-old.OpeningFee-old.ClosingFee-0.12345679m,Assert.Single((await svc.PnlHistory(alice)).Items).NetPnl);
    }
    [Fact] public async Task HistoryPaginationFiltersBeforeLimitAndRejectsForeignCursors()
    {
        await using var db=Db();var now=DateTime.UtcNow;
        for(var n=0;n<6;n++)db.PnlLifecycles.Add(new PnlLifecycle{UserId=n==5?bob:alice,InstrumentId=n%2==0?"BTC-PERP":"CALL",Kind=n%2==0?"Perpetual":"Option",Quantity=1,EntryNotional=100,ExitNotional=110,Direction=1,OpenedAt=now.AddMinutes(-2),ClosedAt=now.AddSeconds(-n),Verified=n!=4});
        await db.SaveChangesAsync();var svc=new TradingService(db);
        var first=await svc.PnlHistory(alice,["Option"],limit:1);Assert.Single(first.Items);Assert.NotNull(first.NextCursor);
        var second=await svc.PnlHistory(alice,["Option"],cursor:first.NextCursor,limit:1);Assert.Single(second.Items);Assert.Null(second.NextCursor);Assert.NotEqual(first.Items[0].Id,second.Items[0].Id);
        await Assert.ThrowsAsync<RuleException>(()=>svc.PnlHistory(bob,cursor:first.NextCursor));
        Assert.Empty((await svc.PnlHistory(alice,from:DateOnly.FromDateTime(now.AddDays(1)))).Items);
        Assert.Equal(4,(await svc.PnlHistory(alice)).Items.Count);
    }
    [Fact] public async Task BackfillIsReadOnlyForMoneyAndHidesUnverifiableHistory()
    {
        await Order(bob,"Sell",0.1m,50000);await Order(alice,"Buy",0.1m,null);
        await Order(bob,"Buy",0.1m,51000);await Order(alice,"Sell",0.1m,null);
        var balance=await Available(alice,"USDT");await using var db=Db();
        await db.PnlLifecycles.ExecuteDeleteAsync();await db.PnlProjection.ExecuteDeleteAsync();
        var svc=new TradingService(db);await svc.BackfillPnl();Assert.Single((await svc.PnlHistory(alice)).Items);
        await db.PnlLifecycles.ExecuteDeleteAsync();await db.PnlProjection.ExecuteDeleteAsync();db.ChangeTracker.Clear();
        var trade=await db.Trades.OrderBy(t=>t.Sequence).FirstAsync();trade.BuyerFee=null;await db.SaveChangesAsync();
        await svc.BackfillPnl();Assert.Empty((await svc.PnlHistory(alice)).Items);Assert.Single((await svc.PnlHistory(bob)).Items);
        Assert.Equal(balance,await Available(alice,"USDT"));await Conserve();
    }
    [Fact] public async Task ZeroOptionSettlementProducesAClosedPremiumLoss()
    {
        await Order(bob,"Sell",0.1m,1000,"CALL");await Order(alice,"Buy",0.1m,null,"CALL");
        await using var db=Db();var svc=new TradingService(db);await svc.SettlePnl(alice,"CALL",0,DateTime.UtcNow);await db.SaveChangesAsync();
        var row=Assert.Single((await svc.PnlHistory(alice)).Items);Assert.Equal(0,row.AverageExit);Assert.Equal(-100,row.GrossPnl);Assert.Equal(-100-row.OpeningFee,row.NetPnl);
    }
    [Fact] public async Task FractionalSettlementHistoryMatchesActualRoundedCash()
    {
        const string symbol="ROUNDING-FUTURE";
        await using(var db=Db()){db.Instruments.Add(new Instrument{Id=symbol,Kind="Future",Expiry=DateTime.UtcNow.AddDays(1)});await db.SaveChangesAsync();}
        await Order(bob,"Sell",0.000001m,50000.01m,symbol);await Order(alice,"Buy",0.000001m,null,symbol);
        var expiry=DateTime.UtcNow.AddSeconds(-2);
        await using(var db=Db()){
            (await db.Instruments.FindAsync(symbol))!.Expiry=expiry;
            for(var t=expiry.AddMinutes(-30);t<=expiry;t=t.AddSeconds(4))db.IndexSamples.Add(new IndexSample{Time=t,Price=50000.005m,Spot=50000.005m});
            await db.SaveChangesAsync();
        }
        await Cmd(alice,"lifecycle",new{},async s=>{await s.ProcessMarkets();return new{};});
        await using var final=Db();var svc=new TradingService(final);
        foreach(var user in new[]{alice,bob}){
            var row=Assert.Single((await svc.PnlHistory(user)).Items);
            Assert.Equal(0,row.GrossPnl);Assert.Equal((await final.Positions.FindAsync(user,symbol))!.RealizedPnl,row.GrossPnl);
            Assert.Equal(-row.OpeningFee,row.NetPnl);Assert.Equal(0,row.ClosingFee);
        }
        await Conserve();
    }
    [Fact] public async Task LegacyFundingOmitsAffectedRowsWithoutChangingBalances()
    {
        await Order(bob,"Sell",0.1m,50000);await Order(alice,"Buy",0.1m,null);
        var boundary=DateTime.UtcNow;
        await Order(bob,"Buy",0.1m,51000);await Order(alice,"Sell",0.1m,null);
        var balance=await Available(alice,"USDT");await using var db=Db();
        db.FundingEvents.Add(new FundingEvent{InstrumentId="BTC-PERP",Time=boundary,Rate=0.0001m});await db.SaveChangesAsync();
        await db.PnlLifecycles.ExecuteDeleteAsync();await db.PnlProjection.ExecuteDeleteAsync();db.ChangeTracker.Clear();
        var svc=new TradingService(db);await svc.BackfillPnl();
        Assert.Empty((await svc.PnlHistory(alice)).Items);Assert.Empty((await svc.PnlHistory(bob)).Items);
        Assert.Equal(balance,await Available(alice,"USDT"));
    }
}
