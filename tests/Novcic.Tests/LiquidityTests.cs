using Microsoft.EntityFrameworkCore;
using Novcic.Core;
using System.Text.Json;
namespace Novcic.Tests;
public partial class ProductTests
{
    private Task<string> Cycle()=>Cmd(alice,"amm-cycle",new{},async s=>{await s.ProcessLiquidity();return new{};});
    [Fact] public async Task QueuedDepositActivatesOnceAndFundsActualBooks()
    {
        var r=new QueueDeposit(0.1m,5000);var key=Guid.NewGuid().ToString();
        var initial=await Cmd(alice,"liquidity-deposit",r,s=>s.QueueLiquidity(alice,r),key);
        Assert.Equal(initial,await Cmd(alice,"liquidity-deposit",r,s=>s.QueueLiquidity(alice,r),key));
        await using(var before=Db()){Assert.False(await before.LiquidityShares.AnyAsync());Assert.Equal("Pending",(await before.LiquidityOperations.SingleAsync()).Status);}
        await Price(50000);await Cycle();
        await using(var after=Db()){Assert.Equal("Completed",(await after.LiquidityOperations.SingleAsync()).Status);Assert.True(await after.Orders.AnyAsync(x=>x.UserId==TradingService.AmmAccount&&x.Status=="Open"&&x.InstrumentId=="BTCUSDT"));}
        await Order(bob,"Buy",0.001m,null,"BTCUSDT");
        await using(var after=Db()){Assert.True(await after.Trades.AnyAsync(x=>x.SellerId==TradingService.AmmAccount));Assert.True((await after.LiquidityPools.SingleAsync()).FeesEarned>0);}
        await Conserve();
    }
    [Fact] public async Task PoolQuotesAndExecutesFuturesAndOptionsWithCoveredCollateral()
    {
        var r=new QueueDeposit(0.1m,5000);await Cmd(alice,"liquidity-deposit",r,s=>s.QueueLiquidity(alice,r));
        await using(var db=Db()){db.Instruments.Add(new Instrument{Id="FUTURE",Kind="Future",Expiry=DateTime.UtcNow.AddDays(2)});await db.SaveChangesAsync();}
        await Price(50000);await Cycle();
        foreach(var instrument in new[]{"BTC-PERP","FUTURE","CALL","PUT"}){
            await using(var db=Db())Assert.True(await db.Orders.AnyAsync(o=>o.UserId==TradingService.AmmAccount&&o.InstrumentId==instrument&&o.Side=="Sell"&&o.Status=="Open"));
            await Order(bob,"Buy",0.000001m,null,instrument);
        }
        await using(var db=Db()){Assert.Equal(4,await db.Positions.CountAsync(x=>x.UserId==TradingService.AmmAccount&&x.Quantity<0));Assert.True((await db.LiquidityPools.SingleAsync()).FeesEarned>0);}
        await Conserve();
    }
    [Fact] public async Task SmallWithdrawalDoesNotRequireEveryPositionToClose()
    {
        var deposit=new QueueDeposit(0.1m,5000);await Cmd(alice,"liquidity-deposit",deposit,s=>s.QueueLiquidity(alice,deposit));await Cycle();
        await Order(bob,"Buy",0.000001m,null,"BTC-PERP");
        var exit=new QueueWithdrawal(1,"USDT");await Cmd(alice,"liquidity-withdraw",exit,s=>s.QueueRedemption(alice,exit));await Price(50000);await Cycle();
        await using(var db=Db()){Assert.Equal("Completed",(await db.LiquidityOperations.SingleAsync(x=>x.Kind=="Withdrawal")).Status);Assert.True(await db.Positions.AnyAsync(x=>x.UserId==TradingService.AmmAccount&&x.Quantity!=0));}
        await Conserve();
    }
    [Fact] public async Task LoanLiquidationExecutesThroughIntegratedOrderBook()
    {
        var deposit=new QueueDeposit(0.1m,5000);await Cmd(alice,"liquidity-deposit",deposit,s=>s.QueueLiquidity(alice,deposit));await Cycle();
        var supply=new SupplyRequest("USDT",1000);await Cmd(alice,"supply",supply,s=>s.SupplyFunds(alice,supply));
        var borrow=new BorrowRequest("USDT",600,"BTC",0.02m);var loan=JsonSerializer.Deserialize<Loan>(await Cmd(bob,"borrow",borrow,s=>s.Borrow(bob,borrow)),Wire.Json)!;
        await Price(30000);await Cmd(bob,"liquidate-loan",new{loan.Id},async s=>{await s.LiquidateLoan(loan.Id);return new{};});
        await using(var db=Db()){Assert.True((await db.Loans.FindAsync(loan.Id))!.Principal<600);Assert.True(await db.Trades.AnyAsync(t=>t.SellerId==loan.Id&&t.BuyerId==TradingService.AmmAccount));}
        await Conserve();
    }
    [Fact] public async Task StalePoolQuotesCannotExecute()
    {
        var r=new QueueDeposit(0.1m,5000);await Cmd(alice,"liquidity-deposit",r,s=>s.QueueLiquidity(alice,r));await Cycle();
        await using(var db=Db())await db.Orders.Where(o=>o.UserId==TradingService.AmmAccount).ExecuteUpdateAsync(s=>s.SetProperty(o=>o.CreatedAt,DateTime.UtcNow.AddMinutes(-1)));
        await Assert.ThrowsAsync<RuleException>(()=>Order(bob,"Buy",0.001m,null,"BTCUSDT"));await Conserve();
    }
    [Fact] public async Task UnbalancedDepositWaitsAndCancellationReturnsEscrow()
    {
        var before=await Available(alice,"USDT");var r=new QueueDeposit(0,1000);
        var response=await Cmd(alice,"liquidity-deposit",r,s=>s.QueueLiquidity(alice,r));var id=JsonDocument.Parse(response).RootElement.GetProperty("id").GetGuid();
        await Cycle();await using(var db=Db()){Assert.Equal("Pending",(await db.LiquidityOperations.SingleAsync()).Status);Assert.False(await db.LiquidityShares.AnyAsync());}
        await Cmd(alice,"liquidity-cancel",new{id},s=>s.CancelLiquidity(alice,id));Assert.Equal(before,await Available(alice,"USDT"));await Conserve();
    }
    [Fact] public async Task WithdrawalLocksSharesAndWaitsForChosenAsset()
    {
        var deposit=new QueueDeposit(0.1m,5000);await Cmd(alice,"liquidity-deposit",deposit,s=>s.QueueLiquidity(alice,deposit));await Cycle();
        decimal shares;await using(var db=Db())shares=(await db.LiquidityShares.SingleAsync()).Shares;
        var r=new QueueWithdrawal(shares,"USDT");await Cmd(alice,"liquidity-withdraw",r,s=>s.QueueRedemption(alice,r));
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"liquidity-withdraw",r,s=>s.QueueRedemption(alice,r)));
        await Price(50000);await Cycle();await using(var db=Db()){Assert.Equal("Pending",(await db.LiquidityOperations.SingleAsync(x=>x.Kind=="Withdrawal")).Status);Assert.Equal(shares,(await db.LiquidityShares.SingleAsync()).Shares);}
        await Order(bob,"Buy",0.1m,null,"BTCUSDT");await Price(50000);await Cycle();
        await using(var db=Db()){Assert.Equal("Completed",(await db.LiquidityOperations.SingleAsync(x=>x.Kind=="Withdrawal")).Status);Assert.Equal(0,(await db.LiquidityShares.SingleAsync()).Shares);}
        await Conserve();
    }
}
public class MarketDataTests
{
    [Fact] public void DepthAggregatesExactlyAndExposesOnlyLevels()
    {
        var levels=PublicDepth.Build(new[]{(100m,0.1m),(99m,0.3m),(100m,0.2m)},true);
        Assert.Equal(new DepthLevel(100,0.3m,0.3m),levels[0]);Assert.Equal(new DepthLevel(99,0.3m,0.6m),levels[1]);
        Assert.Equal(new[]{"Price","Quantity","Total"},typeof(DepthLevel).GetProperties().Select(x=>x.Name));
    }
    [Fact] public void DecimalOptionPricingMatchesReferenceAndPutCallParity()
    {
        var call=OptionPricing.Value(100,100,1,0.2m,true);var put=OptionPricing.Value(100,100,1,0.2m,false);
        Assert.InRange(call.Price,7.9655m,7.9657m);Assert.InRange(call.Delta,0.5398m,0.5399m);Assert.Equal(call.Price,put.Price);
    }
    [Fact] public void VolatilityRejectsStaleAndExtrapolatedInputs()
    {
        var now=DateTime.UtcNow;var row=new OptionReference{Strike=100,Expiry=now.AddDays(30),Time=now,Iv=0.5m};
        Assert.Equal(0.5m,OptionPricing.Volatility([row],100,row.Expiry,now));Assert.Null(OptionPricing.Volatility([row],110,row.Expiry,now));Assert.Null(OptionPricing.Volatility([row],100,row.Expiry,now.AddMinutes(2)));
    }
}
