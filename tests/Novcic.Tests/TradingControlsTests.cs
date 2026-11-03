using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Tests;
public partial class ProductTests
{
    [Fact] public async Task SpotFeesUseReceivedAssetAndBothCustomersPayOnce()
    {
        await Order(alice,"Sell",0.1m,50000,"BTCUSDT");
        var beforeBtc=await Available(bob,"BTC");var beforeUsd=await Available(alice,"USDT");
        var body=new PlaceOrder("Buy","Market",0.1m,null);var key=Guid.NewGuid().ToString();
        var first=await Cmd(bob,"place",body,s=>s.Place(bob,body),key);
        Assert.Equal(first,await Cmd(bob,"place",body,s=>s.Place(bob,body),key));
        Assert.Equal(beforeBtc+0.0999m,await Available(bob,"BTC"));Assert.Equal(beforeUsd+4995m,await Available(alice,"USDT"));
        await using var db=Db();var trade=await db.Trades.SingleAsync();Assert.Equal(0.0001m,trade.BuyerFee);Assert.Equal(5m,trade.SellerFee);Assert.Equal("BTC",trade.BuyerFeeAsset);
        Assert.Equal(50000m,(await db.Orders.SingleAsync(x=>x.Id==trade.TakerOrderId)).AverageExecutionPrice);
        Assert.Equal(0.0001m,await Available(TradingService.FeeAccount,"BTC"));Assert.Equal(5m,await Available(TradingService.FeeAccount,"USDT"));await Conserve();
    }
    [Fact] public async Task PreviewIsRollbackOnlyAndMaxSpendsInputWithoutExtraSpotFee()
    {
        await Order(alice,"Sell",0.1m,50000,"BTCUSDT");await Order(alice,"Sell",0.2m,60000,"BTCUSDT");
        await using var db=Db();var svc=new TradingService(db);var count=await db.Orders.CountAsync();var journal=await db.Journal.CountAsync();
        var preview=await svc.Preview(bob,new("Buy","Market",null));
        Assert.Equal(0.183333m,preview.MaximumQuantity);Assert.Null(preview.Error);
        Assert.Equal(count,await db.Orders.CountAsync());Assert.Equal(journal,await db.Journal.CountAsync());Assert.Empty(await db.Trades.ToListAsync());
        var order=await Order(bob,"Buy",preview.MaximumQuantity,null,"BTCUSDT");Assert.Equal("Filled",order.Status);
        await Conserve();
    }
    [Fact] public async Task AccountLeverageUpdatesExistingOrdersAndRejectsConflictingOrderSetting()
    {
        var order=await Order(alice,"Buy",0.1m,49000);
        await Cmd(alice,"leverage",new LeverageRequest(5),s=>s.SetLeverage(alice,new(5)));
        await using var db=Db();Assert.Equal(5,(await db.Orders.FindAsync(order.Id))!.Leverage);
        var request=new PlaceOrder("Buy","Limit",0.01m,49000,"BTC-PERP",10);
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"place",request,s=>s.Place(alice,request)));
        Assert.Equal(5,await new TradingService(db).AccountLeverage(alice));
    }
    [Fact] public async Task LeverageFailureRollsBackAccountAndOrders()
    {
        var order=await Order(alice,"Buy",5m,49000);
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"leverage",new LeverageRequest(1),s=>s.SetLeverage(alice,new(1))));
        await using var db=Db();Assert.Equal(10,await new TradingService(db).AccountLeverage(alice));Assert.Equal(10,(await db.Orders.FindAsync(order.Id))!.Leverage);
    }
    [Fact] public async Task FuturesMaxIncludesExactFeesAndNextLotFails()
    {
        await Transfer(alice,"BTC",1,"Unified Trading","Funding");
        await Cmd(alice,"leverage",new LeverageRequest(1),s=>s.SetLeverage(alice,new(1)));
        await Order(bob,"Sell",2m,50000);await Price(50000);
        OrderPreview preview;
        await using(var db=Db())preview=await new TradingService(db).Preview(alice,new("Buy","Market",null,"BTC-PERP"));
        Assert.Equal(0.199890m,preview.MaximumQuantity);Assert.Null(preview.Error);
        var tooLarge=new PlaceOrder("Buy","Market",preview.MaximumQuantity+0.000001m,null,"BTC-PERP");
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"place",tooLarge,s=>s.Place(alice,tooLarge)));
        var exact=tooLarge with {Quantity=preview.MaximumQuantity};
        await Cmd(alice,"place",exact,s=>s.Place(alice,exact));await Conserve();
    }
    [Fact] public async Task PreviewCannotUseLiquidityBeyondOwnOrderOrStaleQuotes()
    {
        await Order(alice,"Sell",0.1m,50000,"BTCUSDT");
        await Order(bob,"Sell",0.2m,51000,"BTCUSDT");
        await using var db=Db();var preview=await new TradingService(db).Preview(alice,new("Buy","Market",null));
        Assert.Equal(0,preview.MaximumQuantity);Assert.NotNull(preview.Error);Assert.Empty(await db.Trades.ToListAsync());
    }
    [Theory]
    [InlineData("Spot","Buy",0.1,50000,50000,0.0001)]
    [InlineData("Spot","Sell",0.1,50000,50000,5)]
    [InlineData("Perpetual","Buy",0.1,50000,50000,2.75)]
    [InlineData("Option","Buy",0.1,1,50000,0.007)]
    public void ExactFeeFormula(string kind,string side,decimal qty,decimal price,decimal index,decimal expected)
        =>Assert.Equal(expected,TradingService.ExecutionFee(kind,side,qty,price,index,new()));
}
