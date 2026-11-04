using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Tests;

public partial class ProductTests
{
    [Theory]
    [InlineData("BTC-PERP",50000,51000)]
    [InlineData("CALL",1000,1100)]
    public async Task EmptyClosingAttemptsDoNotCreateOrdersAndBackoffResetsAfterPartialFill(string symbol,decimal entry,decimal trigger)
    {
        await Order(bob,"Sell",0.1m,entry,symbol);await Order(alice,"Buy",0.1m,null,symbol);
        var protection=await Protect(symbol,trigger,null);
        await Order(bob,"Sell",0.01m,trigger,symbol);await Order(alice,"Buy",0.01m,null,symbol);
        await using var reader=Db();var before=await reader.Orders.CountAsync();
        for(var n=0;n<8;n++)await Attempt(protection.Id);
        Assert.Equal(before,await reader.Orders.CountAsync());
        var job=await reader.Protections.AsNoTracking().SingleAsync(x=>x.Id==protection.Id);
        Assert.Equal(8,job.Attempts);Assert.Equal(1000,job.RetryDelayMs);Assert.Equal("Closing",job.Status);
        await Order(bob,"Buy",0.05m,entry,symbol,reduce:true);await Attempt(protection.Id);
        job=await reader.Protections.AsNoTracking().SingleAsync(x=>x.Id==protection.Id);
        Assert.Equal(100,job.RetryDelayMs);Assert.Equal("Closing",job.Status);
        var execution=await reader.Orders.AsNoTracking().SingleAsync(x=>x.ClosingJobId==job.Id);
        Assert.Equal(0.05m,execution.Quantity-execution.Remaining);Assert.True(execution.ReduceOnly);
        await Attempt(protection.Id);Assert.Equal(1,await reader.Orders.CountAsync(x=>x.ClosingJobId==job.Id));
        await Order(bob,"Buy",0.06m,entry,symbol,reduce:true);await Attempt(protection.Id);
        job=await reader.Protections.AsNoTracking().SingleAsync(x=>x.Id==protection.Id);Assert.Equal("Completed",job.Status);
        Assert.Equal(2,await reader.Orders.CountAsync(x=>x.ClosingJobId==job.Id));await Conserve();
    }

    [Fact] public async Task PriceBlockedClosingCreatesNoOrderButManualEmptyMarketOrderRemainsAudited()
    {
        await Order(bob,"Buy",0.1m,50000);await Order(alice,"Sell",0.1m,null);
        var protection=await Protect(tp:49000,sl:51000);
        await Order(bob,"Buy",0.01m,49000);await Order(alice,"Sell",0.01m,null);
        var passive=await Order(bob,"Sell",0.11m,75000,reduce:true);await Attempt(protection.Id);
        await using var db=Db();Assert.False(await db.Orders.AnyAsync(x=>x.ClosingJobId==protection.Id));
        Assert.Equal("Open",(await db.Orders.FindAsync(passive.Id))!.Status);
        Assert.Equal("Futures price limit reached",(await db.Protections.FindAsync(protection.Id))!.Detail);
        var manual=await Order(alice,"Buy",0.11m,null,reduce:true);
        Assert.NotNull(await db.Orders.FindAsync(manual.Id));Assert.Equal("Cancelled",manual.Status);await Conserve();
    }

    private async Task<PositionProtection> Protect(string instrument="BTC-PERP",decimal? tp=51000,decimal? sl=49000)
    {
        await using var db=Db();var p=(await db.Positions.FindAsync(alice,instrument))!;
        var old=await db.Protections.SingleOrDefaultAsync(x=>x.UserId==alice&&x.LifecycleId==p.LifecycleId);
        var r=new ProtectionRequest(instrument,p.LifecycleId,p.Version,old?.Version??0,tp,sl);
        return JsonSerializer.Deserialize<PositionProtection>(await Cmd(alice,"position-protection",r,s=>s.SetProtection(alice,r)),Wire.Json)!;
    }
    private async Task Attempt(Guid id)
    {
        await using var db=Db();await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001234)");
        var p=(await db.Protections.FindAsync(id))!;p.NextAttemptAt=DateTime.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();
        await new TradingService(db).AttemptClose(id);await db.SaveChangesAsync();await tx.CommitAsync();
    }
    [Theory]
    [InlineData("BTC-PERP",50000,51000,49000)]
    [InlineData("CALL",1000,1100,900)]
    public async Task ProtectionIsOcoAndResumesPartialCloseAcrossContexts(string symbol,decimal entry,decimal tp,decimal sl)
    {
        await Order(bob,"Sell",0.1m,entry,symbol);await Order(alice,"Buy",0.1m,null,symbol);
        var protection=await Protect(symbol,tp,sl);
        // One trade reaches TP, then moves back before the worker: crossing must not be lost.
        await Order(bob,"Sell",0.01m,tp,symbol);await Order(alice,"Buy",0.01m,null,symbol);
        await Order(bob,"Sell",0.01m,entry,symbol);await Order(alice,"Buy",0.01m,null,symbol);
        await Attempt(protection.Id); // No buyers. The process remains durable, not a phantom fill.
        await using(var db=Db()){
            var job=(await db.Protections.FindAsync(protection.Id))!;Assert.Equal("Closing",job.Status);Assert.Equal("Take profit",job.TriggerLeg);Assert.Equal(1,job.Attempts);
            Assert.Equal(0.12m,(await db.Positions.FindAsync(alice,symbol))!.Quantity);
        }
        await Order(bob,"Buy",0.05m,entry,symbol,reduce:true);await Attempt(protection.Id);
        await using(var db=Db())Assert.Equal(0.07m,(await db.Positions.FindAsync(alice,symbol))!.Quantity);
        await Order(bob,"Buy",0.07m,entry,symbol,reduce:true);await Attempt(protection.Id);
        await using(var db=Db()){
            Assert.Equal(0,(await db.Positions.FindAsync(alice,symbol))!.Quantity);
            Assert.Equal("Completed",(await db.Protections.FindAsync(protection.Id))!.Status);
            Assert.All(await db.Orders.Where(o=>o.ClosingJobId==protection.Id).ToListAsync(),o=>Assert.True(o.ReduceOnly));
        }
        await Order(bob,"Sell",0.02m,entry,symbol);await Order(alice,"Buy",0.02m,null,symbol);await Attempt(protection.Id);
        await using(var db=Db()){var p=(await db.Positions.FindAsync(alice,symbol))!;Assert.NotEqual(protection.LifecycleId,p.LifecycleId);Assert.Equal(0.02m,p.Quantity);}
        await Conserve();
    }
    [Fact] public async Task StopLossAndUserStopAreVersionedAndStalePricesDeferWithoutAccountingChanges()
    {
        await Order(bob,"Sell",0.1m,50000);await Order(alice,"Buy",0.1m,null);var protection=await Protect();
        await Order(bob,"Sell",0.01m,49000);await Order(alice,"Buy",0.01m,null);
        await Price(50000,true);var before=await Available(alice,"USDT");await Attempt(protection.Id);
        await using var db=Db();var job=(await db.Protections.FindAsync(protection.Id))!;
        Assert.Equal("Stop loss",job.TriggerLeg);Assert.Equal("Closing",job.Status);Assert.Contains("stale",job.Detail);Assert.Equal(before,await Available(alice,"USDT"));
        var stop=new StopClosingRequest(job.InstrumentId,job.Id,job.Version);
        await Cmd(alice,"stop-closing",stop,s=>s.StopClosing(alice,stop));await Attempt(job.Id);
        await db.Entry(job).ReloadAsync();Assert.Equal("Cancelled",job.Status);Assert.Equal(1,job.Attempts);
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"stop-closing",stop,s=>s.StopClosing(alice,stop)));
    }
    [Fact] public async Task ProtectionRejectsStalePositionAndCannotSurviveReversal()
    {
        await Order(bob,"Sell",0.1m,50000);await Order(alice,"Buy",0.1m,null);var protection=await Protect();
        await using var db=Db();var before=(await db.Positions.FindAsync(alice,"BTC-PERP"))!;
        var stale=new ProtectionRequest("BTC-PERP",before.LifecycleId,before.Version,protection.Version,52000,48000);
        await Order(bob,"Buy",0.15m,50000);await Order(alice,"Sell",0.15m,null);
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"position-protection",stale,s=>s.SetProtection(alice,stale)));
        Assert.Equal("Cancelled",(await db.Protections.FindAsync(protection.Id))!.Status);
        await Attempt(protection.Id);await db.Entry(before).ReloadAsync();Assert.Equal(-0.05m,before.Quantity);
    }
    [Fact] public async Task AmendIsAtomicPreservesFillsAndRejectsStaleVersions()
    {
        var order=await Order(alice,"Buy",0.1m,50000,"BTCUSDT");await Order(bob,"Sell",0.04m,null,"BTCUSDT");
        var stale=new AmendOrderRequest("BTCUSDT",order.Id,order.Version,49000);
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"amend-order",stale,s=>s.AmendOrder(alice,stale)));
        await using var db=Db();var latest=(await db.Orders.FindAsync(order.Id))!;
        var invalid=stale with{Version=latest.Version,Price=9000000};
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"amend-order",invalid,s=>s.AmendOrder(alice,invalid)));
        await db.Entry(latest).ReloadAsync();Assert.Equal("PartiallyFilled",latest.Status);Assert.Equal(0.06m,latest.Remaining);
        var valid=invalid with{Price=49000};var key=Guid.NewGuid().ToString();
        var response=await Cmd(alice,"amend-order",valid,s=>s.AmendOrder(alice,valid),key);
        Assert.Equal(response,await Cmd(alice,"amend-order",valid,s=>s.AmendOrder(alice,valid),key));
        var next=JsonSerializer.Deserialize<Order>(response,Wire.Json)!;Assert.Equal(0.06m,next.Quantity);Assert.Equal(order.Id,next.ReplacesId);Assert.True(next.Sequence>order.Sequence);
        await db.Entry(latest).ReloadAsync();Assert.Equal(2000,latest.FilledNotional);Assert.Equal("Amended",latest.CancelReason);await Conserve();
    }
    [Fact] public async Task OptionBasisAndPositionMetricsReconcileWithoutChangingCash()
    {
        await Order(bob,"Sell",0.02m,1000,"CALL");await Order(alice,"Buy",0.02m,null,"CALL");
        await Order(bob,"Sell",0.01m,1300,"CALL");await Order(alice,"Buy",0.01m,null,"CALL");
        await Order(bob,"Buy",0.01m,1200,"CALL",true);await Order(alice,"Sell",0.01m,null,"CALL",true);
        await using var db=Db();var svc=new TradingService(db);var p=(await db.Positions.FindAsync(alice,"CALL"))!;
        Assert.Equal(22,p.Cost);Assert.Equal(1100,p.EntryPrice);Assert.Equal(1,p.TradeRealizedPnl);
        var cash=(await db.MarginBalances.FindAsync(alice))!.Cash;p.AnalyticsReady=false;p.Cost=0;p.EntryPrice=1200;
        await svc.InitializePositionAnalytics(p,(await db.Instruments.FindAsync("CALL"))!);await db.SaveChangesAsync();
        Assert.Equal(22,p.Cost);Assert.Equal(1100,p.EntryPrice);Assert.Equal(cash,(await db.MarginBalances.FindAsync(alice))!.Cash);
        var row=Assert.Single(await svc.PositionViews(alice,await svc.Risk(alice)));Assert.Null(row.LiquidationPrice);Assert.Equal(1200,row.LastPrice);Assert.Equal(row.MarkPrice*0.02m-22,row.UnrealizedPnl);await Conserve();
    }
    [Theory]
    [InlineData(49000,"Take profit")]
    [InlineData(51000,"Stop loss")]
    public async Task ShortProtectionUsesReversedTriggersAndConcurrentWorkersCannotOverclose(decimal trigger,string leg)
    {
        await Order(bob,"Buy",0.1m,50000);await Order(alice,"Sell",0.1m,null);
        var protection=await Protect(tp:49000,sl:51000);
        await Order(bob,"Buy",0.01m,trigger);await Order(alice,"Sell",0.01m,null);
        await Order(bob,"Sell",0.11m,50000,reduce:true);
        await Task.WhenAll(Attempt(protection.Id),Attempt(protection.Id));
        await using var db=Db();var job=(await db.Protections.FindAsync(protection.Id))!;
        Assert.Equal(leg,job.TriggerLeg);Assert.Equal("Completed",job.Status);Assert.Equal(1,job.Attempts);
        Assert.Equal(0,(await db.Positions.FindAsync(alice,"BTC-PERP"))!.Quantity);await Conserve();
    }
    [Fact] public async Task ExpiredContractStopsProtectionWithoutCreatingAnOrder()
    {
        await using(var db=Db()){db.Instruments.Add(new Instrument{Id="FUT",Kind="Future",Expiry=DateTime.UtcNow.AddDays(1)});await db.SaveChangesAsync();}
        await Order(bob,"Sell",0.1m,50000,"FUT");await Order(alice,"Buy",0.1m,null,"FUT");var protection=await Protect("FUT");
        await Order(bob,"Sell",0.01m,51000,"FUT");await Order(alice,"Buy",0.01m,null,"FUT");
        await using(var db=Db()){(await db.Instruments.FindAsync("FUT"))!.Expiry=DateTime.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();}
        await Attempt(protection.Id);await using(var db=Db()){
            Assert.Equal("Completed",(await db.Protections.FindAsync(protection.Id))!.Status);
            Assert.False(await db.Orders.AnyAsync(x=>x.ClosingJobId==protection.Id));Assert.Equal(0.11m,(await db.Positions.FindAsync(alice,"FUT"))!.Quantity);
        }
    }
    [Fact] public async Task PositionScenarioAndReturnRespectDirectionAndMissingPrices()
    {
        await Order(bob,"Sell",0.1m,50000);await Order(alice,"Buy",0.1m,null);
        await using var db=Db();var svc=new TradingService(db);
        var risk=(await svc.Risk(alice)) with{Equity=100,MaintenanceMargin=25};
        var row=Assert.Single(await svc.PositionViews(alice,risk));
        Assert.Equal(500,row.EntryMargin);Assert.Equal(0,row.UnrealizedPnl);Assert.Equal(0,row.ReturnOnMarginPercent);
        var threshold=row.LiquidationPrice!.Value;
        Assert.InRange(100+0.1m*(threshold-50000)-(25+0.0005m*(threshold-50000)),-0.00000001m,0.00000001m);
        var unavailable=Assert.Single(await svc.PositionViews(alice,risk with{PriceFresh=false}));
        Assert.Null(unavailable.MarkPrice);Assert.Null(unavailable.LiquidationPrice);Assert.Null(unavailable.UnrealizedPnl);Assert.Equal(50000,unavailable.LastPrice);
    }
    [Fact] public async Task ReturnedOrderVersionIsStableAfterDatabaseRoundTrip()
    {
        var old=await Order(alice,"Buy",0.1m,49000,"BTCUSDT");
        var request=new AmendOrderRequest("BTCUSDT",old.Id,old.Version,48000);
        var response=JsonSerializer.Deserialize<Order>(await Cmd(alice,"amend-order",request,s=>s.AmendOrder(alice,request)),Wire.Json)!;
        Assert.Equal(48000,response.Price);
    }
}
