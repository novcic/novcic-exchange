using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Tests;

public partial class ProductTests
{
    private async Task<decimal> FundingBalance(string asset){await using var db=Db();return(await db.Wallets.FindAsync(alice,"Funding",asset))!.Available;}
    [Theory]
    [InlineData("Funding",0.002,100)]
    [InlineData("Funding",0.002,0)]
    [InlineData("Funding",0,100)]
    [InlineData("Unified Trading",0.002,100)]
    [InlineData(null,0.002,100)]
    public async Task LiquiditySourceDebitsAndRefundsOnlySelectedAccount(string? source,decimal btc,decimal usdt)
    {
        var fundingBtc=await FundingBalance("BTC");var fundingUsdt=await FundingBalance("USDT");
        var tradingBtc=await Available(alice,"BTC");var tradingUsdt=await Available(alice,"USDT");
        var r=new QueueDeposit(btc,usdt,source);var key=Guid.NewGuid().ToString();
        var response=await Cmd(alice,"liquidity-deposit",r,s=>s.QueueLiquidity(alice,r),key);
        Assert.Equal(response,await Cmd(alice,"liquidity-deposit",r,s=>s.QueueLiquidity(alice,r),key));
        var op=JsonSerializer.Deserialize<LiquidityOperation>(response,Wire.Json)!;
        Assert.Equal(source??"Unified Trading",op.SourceAccount);
        Assert.Equal(fundingBtc-(source=="Funding"?btc:0),await FundingBalance("BTC"));
        Assert.Equal(fundingUsdt-(source=="Funding"?usdt:0),await FundingBalance("USDT"));
        Assert.Equal(tradingBtc-(source=="Funding"?0:btc),await Available(alice,"BTC"));
        Assert.Equal(tradingUsdt-(source=="Funding"?0:usdt),await Available(alice,"USDT"));
        var changed=r with{SourceAccount=source=="Funding"?"Unified Trading":"Funding"};
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"liquidity-deposit",changed,s=>s.QueueLiquidity(alice,changed),key));
        var cancel=new{op.Id};var cancelKey=Guid.NewGuid().ToString();
        var cancelled=await Cmd(alice,"liquidity-cancel",cancel,s=>s.CancelLiquidity(alice,op.Id),cancelKey);
        Assert.Equal(cancelled,await Cmd(alice,"liquidity-cancel",cancel,s=>s.CancelLiquidity(alice,op.Id),cancelKey));
        Assert.Equal(fundingBtc,await FundingBalance("BTC"));Assert.Equal(fundingUsdt,await FundingBalance("USDT"));
        Assert.Equal(tradingBtc,await Available(alice,"BTC"));Assert.Equal(tradingUsdt,await Available(alice,"USDT"));await Conserve();
    }

    [Fact] public async Task LiquidityFundingFailureRollsBackBothAssetsAndRejectsUnknownSources()
    {
        var btc=await FundingBalance("BTC");var usdt=await FundingBalance("USDT");
        var r=new QueueDeposit(0.01m,usdt+1,"Funding");var key=Guid.NewGuid().ToString();
        var error=await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"liquidity-deposit",r,s=>s.QueueLiquidity(alice,r),key));
        Assert.Contains("USDT in Funding",error.Message);
        foreach(var source in new[]{"", "funding", "Another account"}){
            var invalid=new QueueDeposit(0,1,source);
            await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"liquidity-deposit",invalid,s=>s.QueueLiquidity(alice,invalid)));
        }
        await using var db=Db();Assert.False(await db.LiquidityOperations.AnyAsync());Assert.False(await db.Receipts.AnyAsync(x=>x.UserId==alice&&x.Key==key));
        Assert.False(await db.Ledger.AnyAsync(x=>x.Reason=="Queued liquidity deposit"||x.Reason=="Liquidity escrow"));
        Assert.Equal(btc,await FundingBalance("BTC"));Assert.Equal(usdt,await FundingBalance("USDT"));await Conserve();
    }

    [Fact] public async Task LiquidityFundingWorksIndependentlyOfRestrictedTradingCollateral()
    {
        await Order(bob,"Buy",2,50000);await Order(alice,"Sell",2,null);await Price(2000000);
        await using var db=Db();var service=new TradingService(db);Assert.True((await service.Risk(alice)).Restricted);
        var tradingBtc=await Available(alice,"BTC");var tradingUsdt=await Available(alice,"USDT");
        var r=new QueueDeposit(0.001m,100,"Funding");
        var op=JsonSerializer.Deserialize<LiquidityOperation>(await Cmd(alice,"liquidity-deposit",r,s=>s.QueueLiquidity(alice,r)),Wire.Json)!;
        var fromTrading=r with{SourceAccount="Unified Trading"};
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"liquidity-deposit",fromTrading,s=>s.QueueLiquidity(alice,fromTrading)));
        await Cmd(alice,"liquidity-cancel",new{op.Id},s=>s.CancelLiquidity(alice,op.Id));
        Assert.Equal(tradingBtc,await Available(alice,"BTC"));Assert.Equal(tradingUsdt,await Available(alice,"USDT"));await Conserve();
    }

    [Fact] public async Task LiquidityFundingActivatesSharesThroughExistingPoolFlow()
    {
        var btc=await Available(alice,"BTC");var usdt=await Available(alice,"USDT");var r=new QueueDeposit(0.1m,5000,"Funding");
        await Cmd(alice,"liquidity-deposit",r,s=>s.QueueLiquidity(alice,r));await Cycle();
        await using var db=Db();var op=await db.LiquidityOperations.SingleAsync();Assert.Equal("Completed",op.Status);Assert.Equal("Funding",op.SourceAccount);
        Assert.Equal(10000,op.DepositValueUsd);Assert.True((await db.LiquidityShares.FindAsync(alice))!.Shares>0);
        Assert.Equal(btc,await Available(alice,"BTC"));Assert.Equal(usdt,await Available(alice,"USDT"));await Conserve();
    }

    [Fact] public async Task LiquidityFundingCancellationReturnsConvertedAssetsToFunding()
    {
        var btc=await FundingBalance("BTC");var usdt=await FundingBalance("USDT");var r=new QueueDeposit(0,1000,"Funding");
        var op=JsonSerializer.Deserialize<LiquidityOperation>(await Cmd(alice,"liquidity-deposit",r,s=>s.QueueLiquidity(alice,r)),Wire.Json)!;
        await Cycle();await Order(bob,"Sell",0.005m,null,"BTCUSDT");
        await Cmd(alice,"liquidity-cancel",new{op.Id},s=>s.CancelLiquidity(alice,op.Id));
        Assert.Equal(btc+0.005m,await FundingBalance("BTC"));Assert.Equal(usdt-250,await FundingBalance("USDT"));
        await using var db=Db();Assert.False(await db.Orders.AnyAsync(x=>x.UserId==op.Id&&(x.Status=="Open"||x.Status=="PartiallyFilled")));await Conserve();
    }

    [Fact] public async Task LiquiditySourceOmissionPreservesLegacyIdempotencyFingerprint()
    {
        var r=JsonSerializer.Deserialize<QueueDeposit>("{\"btc\":\"0\",\"usdt\":\"100\"}",Wire.Json)!;
        var legacy=new{btc=r.Btc,usdt=r.Usdt};Assert.Equal(JsonSerializer.Serialize(legacy,Wire.Json),JsonSerializer.Serialize(r,Wire.Json));
        var key=Guid.NewGuid().ToString();var response=await Cmd(alice,"liquidity-deposit",legacy,s=>s.QueueLiquidity(alice,r),key);
        Assert.Equal(response,await Cmd(alice,"liquidity-deposit",r,s=>s.QueueLiquidity(alice,r),key));await Conserve();
    }
}
