using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Tests;

public partial class ProductTests
{
    [Fact] public async Task OrdersMayReachOneHundredPercentInitialRateButCannotExceedIt()
    {
        await Transfer(alice,"BTC",1,"Unified Trading","Funding");
        await Transfer(alice,"USDT",9497.25m,"Unified Trading","Funding");
        await Order(alice,"Buy",0.1m,50000);
        await using(var db=Db()){
            var risk=await new TradingService(db).Risk(alice);
            Assert.Equal(100,risk.InitialMarginRate);Assert.Equal(0,risk.AvailableBalance);Assert.False(risk.Restricted);
        }
        await Assert.ThrowsAsync<RuleException>(()=>Order(alice,"Buy",0.000001m,50000));
        await Conserve();
    }

    [Fact] public async Task LosingUsdtShortHasNegativeCoinEquityAndPositiveBtcBackedAccountEquity()
    {
        await Transfer(alice,"BTC",0.98m,"Unified Trading","Funding");
        await Order(bob,"Buy",0.01m,50000);await Order(alice,"Sell",0.01m,null);
        await Transfer(alice,"USDT",await Available(alice,"USDT"),"Unified Trading","Funding");
        await Price(55000);
        await using var db=Db();var svc=new TradingService(db);var risk=await svc.Risk(alice);
        var assets=await svc.AccountAssets(alice,risk);
        var usdt=Assert.Single(assets,a=>a.Kind=="Unified Trading"&&a.Asset=="USDT");
        var btc=Assert.Single(assets,a=>a.Kind=="Unified Trading"&&a.Asset=="BTC");
        Assert.Equal(0,usdt.WalletBalance);Assert.Equal(-44.5m,usdt.Equity);Assert.Equal(-44.5m,usdt.UnrealizedPnl);
        Assert.Equal(0.02m,btc.Equity);Assert.Equal(1100,btc.UsdValue);
        Assert.Equal(1100,risk.TotalWalletBalance);Assert.Equal(1055.5m,risk.TotalEquity);
        Assert.Equal(945.5m,risk.MarginBalance);Assert.Equal(891.05m,risk.AvailableBalance);
        Assert.Equal(risk.TotalEquity,assets.Where(a=>a.Kind=="Unified Trading").Sum(a=>a.UsdValue));
        Assert.False(risk.Restricted);await Conserve();
    }

    [Fact] public async Task LiquidationStartsAtExactlyOneHundredPercentMaintenanceRate()
    {
        await Order(bob,"Sell",0.1m,50000);await Order(alice,"Buy",0.1m,null);
        await Transfer(alice,"BTC",1,"Unified Trading","Funding");
        await Transfer(alice,"USDT",(await Available(alice,"USDT"))-1020,"Unified Trading","Funding");
        await using(var db=Db())await db.Trades.ExecuteUpdateAsync(s=>s.SetProperty(t=>t.CreatedAt,DateTime.UtcNow.AddMinutes(-5)));
        await Price(40000);await Order(bob,"Buy",0.1m,40000,reduce:true);
        await using(var db=Db()){
            var risk=await new TradingService(db).Risk(alice);
            Assert.Equal(20,risk.MarginBalance);Assert.Equal(20,risk.MaintenanceMargin);
            Assert.Equal(100,risk.MaintenanceMarginRate);Assert.True(risk.Restricted);Assert.Equal(-380m,risk.AvailableBalance);
        }
        await Cmd(alice,"liquidate",new{},async s=>{await s.LiquidatePositions(alice);return new{};});
        await using(var db=Db())Assert.Equal(0.075m,(await db.Positions.FindAsync(alice,"BTC-PERP"))!.Quantity);
        await Conserve();
    }

    [Fact] public async Task StalePricesHideMarksRatesAndUnrealizedCoinEquity()
    {
        await Order(bob,"Sell",0.1m,50000);await Order(alice,"Buy",0.1m,null);
        await Price(50000,true);
        await using var db=Db();var svc=new TradingService(db);var risk=await svc.Risk(alice);
        Assert.Null(await svc.PublicMark("BTC-PERP"));Assert.Null(risk.TotalEquity);
        Assert.Null(risk.InitialMarginRate);Assert.Null(risk.MaintenanceMarginRate);Assert.Null(risk.AvailableBalance);
        var assets=await svc.AccountAssets(alice,risk);
        Assert.Null(assets.Single(a=>a.Kind=="Unified Trading"&&a.Asset=="USDT").Equity);
        Assert.NotNull(assets.Single(a=>a.Kind=="Funding"&&a.Asset=="USDT").Equity);
    }

    [Fact] public async Task PublicMarkMatchesPositionValuationRatherThanLastTrade()
    {
        await Order(bob,"Sell",0.1m,54000);await Order(alice,"Buy",0.1m,null);
        await using var db=Db();var svc=new TradingService(db);var mark=await svc.PublicMark("BTC-PERP");
        Assert.Equal(50500,mark);Assert.Equal(mark,(await svc.PositionViews(alice,await svc.Risk(alice))).Single().MarkPrice);
        Assert.Null(await svc.PublicMark("BTCUSDT"));
    }
}

public class MarginRateTests
{
    [Theory]
    [InlineData(504.22,605.15,30.25,-100.93)]
    [InlineData(529.21,602.19,30.10,-72.98)]
    [InlineData(100,100,5,0)]
    [InlineData(200,100,5,100)]
    public void RatesAndAvailableBalanceUseMarginBalance(decimal balance,decimal initial,decimal maintenance,decimal available)
    {
        var risk=new RiskSnapshot(balance,initial,maintenance,0,0,0,0,50000,true,false);
        Assert.Equal(initial/balance*100,risk.InitialMarginRate);
        Assert.Equal(maintenance/balance*100,risk.MaintenanceMarginRate);
        Assert.Equal(available,risk.AvailableBalance);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void NonpositiveMarginDoesNotProduceNegativeOrInfiniteRates(decimal balance)
    {
        var risk=new RiskSnapshot(balance,100,5,0,0,0,0,50000,true,true);
        Assert.Null(risk.InitialMarginRate);Assert.Null(risk.MaintenanceMarginRate);Assert.Equal(balance-100,risk.AvailableBalance);
        Assert.Equal(0,(risk with{InitialMargin=0,MaintenanceMargin=0}).MaintenanceMarginRate);
    }
}
