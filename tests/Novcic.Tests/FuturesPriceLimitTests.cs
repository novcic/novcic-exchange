using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Tests;

public partial class ProductTests
{
    public static IEnumerable<object[]> FuturesLimitCases()
    {
        foreach(var kind in new[]{"Perpetual","Future"})
        foreach(var (side,price,allowed) in new[]{("Buy",25000m,true),("Sell",75000m,true),("Buy",55000m,true),("Sell",45000m,true),("Buy",55000.01m,false),("Sell",44999.99m,false),("Buy",75000m,false),("Sell",25000m,false)})
            yield return [kind,side,price,allowed];
    }

    [Theory,MemberData(nameof(FuturesLimitCases))]
    public async Task FuturesLimitsAdmitPassivePricesAndRejectOnlyTheAdverseSide(string kind,string side,decimal price,bool allowed)
    {
        var symbol=kind=="Future"?"DELIVERY":"BTC-PERP";
        if(kind=="Future"){
            await using var db=Db();db.Instruments.Add(new Instrument{Id=symbol,Kind=kind,Expiry=DateTime.UtcNow.AddDays(1)});await db.SaveChangesAsync();
        }
        if(allowed){var order=await Order(alice,side,0.01m,price,symbol);Assert.Equal("Open",order.Status);Assert.Equal(price,order.Price);}
        else {
            var error=await Assert.ThrowsAsync<RuleException>(()=>Order(alice,side,0.01m,price,symbol));
            Assert.Contains(side=="Buy"?"110%":"90%",error.Message);
            await using var db=Db();Assert.Empty(await db.Orders.ToListAsync());
        }
        await Conserve();
    }

    [Theory]
    [InlineData("Buy","Sell",55000,75000)]
    [InlineData("Sell","Buy",45000,25000)]
    public async Task MarketFillsAtTheBoundaryThenStopsWithoutCancellingPassiveLiquidity(string takerSide,string makerSide,decimal boundary,decimal passive)
    {
        await Order(bob,makerSide,0.004m,boundary);
        var resting=await Order(bob,makerSide,0.006m,passive);
        var order=await Order(alice,takerSide,0.01m,null);
        Assert.Equal("Cancelled",order.Status);Assert.Equal(0.006m,order.Remaining);Assert.Equal("Futures price limit reached",order.CancelReason);
        await using(var db=Db()){
            var trade=await db.Trades.SingleAsync();Assert.Equal(boundary,trade.Price);Assert.Equal(0.004m,trade.Quantity);
            Assert.Equal("Open",(await db.Orders.FindAsync(resting.Id))!.Status);
        }
        // Once the reference supports it, the same passive order is executable.
        await Price(passive);var later=await Order(alice,takerSide,0.006m,null);Assert.Equal("Filled",later.Status);
        await using(var db=Db())Assert.Equal("Filled",(await db.Orders.FindAsync(resting.Id))!.Status);
        await Conserve();
    }

    [Theory]
    [InlineData("Buy","Sell",75000)]
    [InlineData("Sell","Buy",25000)]
    public async Task ReferenceChangesCancelOnlyMakersOutsideTheirOwnSideLimit(string makerSide,string takerSide,decimal oldReference)
    {
        await Price(oldReference);var stale=await Order(bob,makerSide,0.01m,oldReference);
        await Price(50000);var valid=await Order(bob,makerSide,0.01m,50000);
        var order=await Order(alice,takerSide,0.01m,null);Assert.Equal("Filled",order.Status);
        await using(var db=Db()){
            var cancelled=(await db.Orders.FindAsync(stale.Id))!;Assert.Equal("Cancelled",cancelled.Status);Assert.Equal("Risk or price-band restriction",cancelled.CancelReason);
            Assert.Equal("Filled",(await db.Orders.FindAsync(valid.Id))!.Status);Assert.Equal(50000,(await db.Trades.SingleAsync()).Price);
        }
        await Conserve();
    }

    [Theory]
    [InlineData("Buy","Sell",75000,44999.99)]
    [InlineData("Sell","Buy",25000,55000.01)]
    public async Task ReduceOnlyLimitsUseTheSameDirectionalRules(string entrySide,string closeSide,decimal passive,decimal invalid)
    {
        await Order(bob,closeSide,0.01m,50000);await Order(alice,entrySide,0.01m,null);
        var close=await Order(alice,closeSide,0.01m,passive,reduce:true);Assert.Equal("Open",close.Status);Assert.True(close.ReduceOnly);
        await Assert.ThrowsAsync<RuleException>(()=>Order(alice,closeSide,0.01m,invalid,reduce:true));
        await Conserve();
    }

    [Theory]
    [InlineData("Sell",75000,44999.99)]
    [InlineData("Buy",25000,55000.01)]
    public async Task AmendAcceptsPassivePricesAndRollsBackAdverseReplacements(string side,decimal passive,decimal invalid)
    {
        var order=await Order(alice,side,0.01m,50000);
        var request=new AmendOrderRequest("BTC-PERP",order.Id,order.Version,passive);
        var amended=JsonSerializer.Deserialize<Order>(await Cmd(alice,"amend-order",request,s=>s.AmendOrder(alice,request)),Wire.Json)!;
        Assert.Equal(passive,amended.Price);Assert.Equal(order.Id,amended.ReplacesId);
        var rejected=new AmendOrderRequest("BTC-PERP",amended.Id,amended.Version,invalid);
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"amend-order",rejected,s=>s.AmendOrder(alice,rejected)));
        await using(var db=Db()){
            var unchanged=(await db.Orders.FindAsync(amended.Id))!;Assert.Equal("Open",unchanged.Status);Assert.Equal(amended.Version,unchanged.Version);Assert.Equal(passive,unchanged.Price);
        }
        await Conserve();
    }

    [Theory]
    [InlineData("Buy")]
    [InlineData("Sell")]
    public async Task InventorySkewedAmmKeepsPassiveQuotesBeyondTheOppositeSideLimit(string inventorySide)
    {
        var deposit=new QueueDeposit(0.1m,5000);await Cmd(alice,"liquidity-deposit",deposit,s=>s.QueueLiquidity(alice,deposit));await Cycle();
        // Build real pool inventory through a participant fill, leaving ample margin.
        await Cmd(alice,"cancel-pool-quotes",new{},s=>s.CancelAll(TradingService.AmmAccount));
        var opposite=inventorySide=="Buy"?"Sell":"Buy";await Order(bob,opposite,0.03m,50000);
        var fill=new PlaceOrder(inventorySide,"Market",0.03m,null,"BTC-PERP",1);
        await Cmd(TradingService.AmmAccount,"place",fill,s=>s.Place(TradingService.AmmAccount,fill));
        await Price(50000);await Cycle();
        await using(var db=Db()){
            var quotes=await db.Orders.Where(o=>o.UserId==TradingService.AmmAccount&&o.InstrumentId=="BTC-PERP"&&o.Status=="Open").ToListAsync();
            Assert.Contains(quotes,o=>o.Side=="Buy");Assert.Contains(quotes,o=>o.Side=="Sell");
            Assert.All(quotes.Where(o=>o.Side=="Buy"),o=>Assert.True(o.Price<=54500));
            Assert.All(quotes.Where(o=>o.Side=="Sell"),o=>Assert.True(o.Price>=45500));
            Assert.Contains(quotes,o=>inventorySide=="Buy"?o.Side=="Buy"&&o.Price<45000:o.Side=="Sell"&&o.Price>55000);
        }
        await Conserve();
    }
}
