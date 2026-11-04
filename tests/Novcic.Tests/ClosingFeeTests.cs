using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Tests;

public partial class ProductTests
{
    [Fact] public async Task TriggeredProtectionClosesWithoutWalletUsdt()
    {
        await Order(bob,"Sell",0.1m,50000);await Order(alice,"Buy",0.1m,null);var job=await Protect();
        await Order(bob,"Sell",0.01m,51000);await Order(alice,"Buy",0.01m,null);
        foreach(var user in new[]{alice,bob})await Transfer(user,"USDT",await Available(user,"USDT"),"Unified Trading","Funding");
        await Order(bob,"Buy",0.11m,51000,reduce:true);await Attempt(job.Id);
        await using var db=Db();Assert.Equal("Completed",(await db.Protections.FindAsync(job.Id))!.Status);
        Assert.Equal(0,(await db.Positions.FindAsync(alice,"BTC-PERP"))!.Quantity);
        Assert.Equal("Filled",(await db.Orders.SingleAsync(x=>x.ClosingJobId==job.Id)).Status);await Conserve();
    }

    [Theory]
    [InlineData("BTC-PERP",50000,true)]
    [InlineData("BTC-PERP",50000,false)]
    [InlineData("FUT",50000,true)]
    [InlineData("CALL",1000,true)]
    public async Task ReduceOnlyMakerAndTakerCloseWithoutCashUsdtAndChargeFeesOnce(string symbol,decimal price,bool longPosition)
    {
        if(symbol=="FUT"){await using var setup=Db();setup.Instruments.Add(new Instrument{Id="FUT",Kind="Future",Expiry=DateTime.UtcNow.AddDays(1)});await setup.SaveChangesAsync();}
        var side=longPosition?"Buy":"Sell";var opposite=longPosition?"Sell":"Buy";
        await Order(bob,opposite,0.1m,price,symbol);await Order(alice,side,0.1m,null,symbol);
        foreach(var user in new[]{alice,bob})await Transfer(user,"USDT",await Available(user,"USDT"),"Unified Trading","Funding");
        await using var reader=Db();var svc=new TradingService(reader);
        var before=await svc.Risk(bob);
        var maker=await Order(bob,side,0.1m,price,symbol,reduce:true);
        reader.ChangeTracker.Clear();var resting=await svc.Risk(bob);
        Assert.Equal(before.InitialMargin,resting.InitialMargin);Assert.Equal(before.Restricted,resting.Restricted);
        var preview=await svc.Preview(alice,new(opposite,"Market",null,symbol,true,0.1m));
        Assert.Equal(0.1m,preview.MaximumQuantity);Assert.Null(preview.Error);
        var request=new PlaceOrder(opposite,"Market",0.1m,null,symbol,null,true);var key=Guid.NewGuid().ToString();
        var response=await Cmd(alice,"place",request,s=>s.Place(alice,request),key);
        Assert.Equal(response,await Cmd(alice,"place",request,s=>s.Place(alice,request),key));
        var order=JsonSerializer.Deserialize<Order>(response,Wire.Json)!;Assert.Equal("Filled",order.Status);
        reader.ChangeTracker.Clear();
        Assert.All(await reader.Positions.Where(x=>x.InstrumentId==symbol).ToListAsync(),p=>Assert.Equal(0,p.Quantity));
        var trade=await reader.Trades.SingleAsync(x=>x.TakerOrderId==order.Id);
        Assert.True(trade.BuyerFee>0);Assert.True(trade.SellerFee>0);
        Assert.Equal(trade.BuyerFee+trade.SellerFee,(await reader.MarginBalances.FindAsync(TradingService.FeeAccount))!.Cash);
        Assert.Equal(0,await Available(alice,"USDT"));Assert.Equal(0,await Available(bob,"USDT"));
        Assert.Equal(2,await reader.Journal.CountAsync(x=>x.ReferenceId==trade.Id&&x.Reason=="Execution fee accrued"&&x.Account.StartsWith("margin:")));
        Assert.Equal(await reader.ClearingPools.Select(x=>x.Cash).SingleAsync(),await reader.MarginBalances.SumAsync(x=>x.Cash));await Conserve();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.01)]
    public async Task ClosingFeeClaimsPreserveLpSplitAndDoNotMintWalletFunds(decimal cashLeft)
    {
        var deposit=new QueueDeposit(0.1m,5000);await Cmd(bob,"liquidity-deposit",deposit,s=>s.QueueLiquidity(bob,deposit));await Cycle();
        await Order(alice,"Buy",0.001m,null);
        await Transfer(alice,"USDT",await Available(alice,"USDT")-cashLeft,"Unified Trading","Funding");
        await Cmd(TradingService.AmmAccount,"cancel-all",new{},s=>s.CancelAll(TradingService.AmmAccount));
        // An expired spot bid cannot make optional debt conversion roll back a close.
        var stale=new PlaceOrder("Buy","Limit",0.001m,50000,"BTCUSDT");
        var staleOrder=JsonSerializer.Deserialize<Order>(await Cmd(TradingService.AmmAccount,"place",stale,s=>s.Place(TradingService.AmmAccount,stale)),Wire.Json)!;
        await using(var setup=Db())await setup.Orders.Where(x=>x.Id==staleOrder.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.QuoteValidatedAt,DateTime.UtcNow.AddSeconds(-10)));
        var resting=new PlaceOrder("Buy","Limit",0.001m,50000,"BTC-PERP",null,true);
        await Cmd(TradingService.AmmAccount,"place",resting,s=>s.Place(TradingService.AmmAccount,resting));
        await using var db=Db();var svc=new TradingService(db);var before=(await svc.LiquidityFees(bob)).Usdt;
        var feeWallet=(await db.Wallets.FindAsync(TradingService.FeeAccount,"Unified Trading","USDT"))!.Available;
        var poolWallet=(await db.Wallets.FindAsync(TradingService.AmmAccount,"Unified Trading","USDT"))!.Available;
        var close=await Order(alice,"Sell",0.001m,null,reduce:true);
        db.ChangeTracker.Clear();var trade=await db.Trades.SingleAsync(x=>x.TakerOrderId==close.Id);
        var fee=trade.SellerFee!.Value;var lp=decimal.Floor(fee*0.8m*100000000m)/100000000m;
        Assert.Equal(0.0275m,fee);Assert.Equal(lp,(await svc.LiquidityFees(bob)).Usdt-before);
        Assert.Equal(cashLeft,(await db.Wallets.FindAsync(TradingService.FeeAccount,"Unified Trading","USDT"))!.Available-feeWallet+(await db.Wallets.FindAsync(TradingService.AmmAccount,"Unified Trading","USDT"))!.Available-poolWallet);
        Assert.Equal(-fee+cashLeft,await db.Journal.Where(x=>x.ReferenceId==trade.Id&&x.Account==$"margin:{alice}"&&x.Reason=="Execution fee accrued").SumAsync(x=>x.Delta));
        var earned=(await svc.LiquidityFees(bob)).Usdt;
        await Transfer(alice,"USDT",10);
        await Cmd(TradingService.AmmAccount,"settle-cash",new{},s=>s.SettleCash(TradingService.AmmAccount));
        db.ChangeTracker.Clear();Assert.Equal(earned,(await svc.LiquidityFees(bob)).Usdt);
        Assert.Equal((await db.ClearingPools.SingleAsync()).Cash,await db.MarginBalances.SumAsync(x=>x.Cash));await Conserve();
    }

    [Fact] public async Task OpeningStillRequiresFeeFundsAndReduceOnlyCannotReverseAPosition()
    {
        await Order(bob,"Sell",0.1m,50000);await Order(alice,"Buy",0.1m,null);
        await Transfer(alice,"USDT",await Available(alice,"USDT"),"Unified Trading","Funding");
        await Order(bob,"Sell",0.01m,50000);
        await Assert.ThrowsAsync<RuleException>(()=>Order(alice,"Buy",0.01m,null));
        await Assert.ThrowsAsync<RuleException>(()=>Order(alice,"Sell",0.2m,null,reduce:true));
        await using var db=Db();Assert.Equal(0.1m,(await db.Positions.FindAsync(alice,"BTC-PERP"))!.Quantity);
        Assert.False(await db.Journal.AnyAsync(x=>x.Reason=="Execution fee accrued"));await Conserve();
    }

    [Fact] public async Task UnderwaterReduceOnlyPositionCanCloseWithZeroUsdt()
    {
        await Order(bob,"Buy",1,50000);await Order(alice,"Sell",1,null);
        foreach(var user in new[]{alice,bob})await Transfer(user,"USDT",await Available(user,"USDT"),"Unified Trading","Funding");
        await Price(2000000);
        await using var db=Db();var svc=new TradingService(db);Assert.True((await svc.Risk(alice)).Restricted);
        await Order(bob,"Sell",1,2000000,reduce:true);
        var closed=await Order(alice,"Buy",1,null,reduce:true);Assert.Equal("Filled",closed.Status);
        db.ChangeTracker.Clear();Assert.Equal(0,(await db.Positions.FindAsync(alice,"BTC-PERP"))!.Quantity);
        Assert.True((await db.MarginBalances.FindAsync(alice))!.Cash<0);
        await Conserve();
    }
}
