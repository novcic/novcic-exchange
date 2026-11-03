using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Novcic.Core;
namespace Novcic.Tests;

[Collection("database")]
public partial class ProductTests : IAsyncLifetime
{
    private readonly string connection=Environment.GetEnvironmentVariable("PRODUCT_TEST_DB")??"Host=127.0.0.1;Port=54330;Database=novcic_products_tests;Username=novcic;Password=novcic-local-demo";
    private readonly Guid alice=Guid.NewGuid(),bob=Guid.NewGuid();
    private ExchangeDb Db()=>new(new DbContextOptionsBuilder<ExchangeDb>().UseNpgsql(connection).Options);
    public async Task InitializeAsync()
    {
        if(!new Npgsql.NpgsqlConnectionStringBuilder(connection).Database!.EndsWith("_tests"))throw new InvalidOperationException("Disposable test database required.");
        await using var db=Db();await db.Database.EnsureDeletedAsync();await db.Database.EnsureCreatedAsync();
        db.Instruments.AddRange(new Instrument(),new Instrument{Id="BTC-PERP",Kind="Perpetual"},new Instrument{Id="CALL",Kind="Option",OptionSide="Call",Strike=50000,Expiry=DateTime.UtcNow.AddDays(1)},new Instrument{Id="PUT",Kind="Option",OptionSide="Put",Strike=50000,Expiry=DateTime.UtcNow.AddDays(1)});
        foreach(var expiry in new[]{DateTime.UtcNow.AddMinutes(1),DateTime.UtcNow.AddDays(10)})foreach(var strike in new[]{10000m,100000m})db.OptionReferences.Add(new OptionReference{Symbol=$"fixture-{expiry.Ticks}-{strike}",Expiry=expiry,Strike=strike,Iv=0.5m,Time=DateTime.UtcNow});
        db.IndexSamples.Add(new IndexSample{Time=DateTime.UtcNow,Price=50000,Spot=50000});await db.SaveChangesAsync();
        foreach(var user in new[]{alice,bob}){
            await Cmd(user,"faucet",new{},s=>s.Faucet(user));
            await Transfer(user,"BTC",1);await Transfer(user,"USDT",10000);
        }
    }
    public Task DisposeAsync()=>Task.CompletedTask;
    private async Task<string> Cmd(Guid user,string op,object body,Func<TradingService,Task<object>> action,string? key=null)
    {await using var db=Db();return await new TradingService(db).Command(user,key??Guid.NewGuid().ToString(),op,body,()=>action(new TradingService(db)));}
    private Task<string> Transfer(Guid u,string asset,decimal amount,string from="Funding",string to="Unified Trading")
    {var r=new TransferRequest(asset,from,to,amount);return Cmd(u,"transfer",r,s=>s.Transfer(u,r));}
    private async Task<Order> Order(Guid u,string side,decimal qty,decimal? price,string instrument="BTC-PERP",bool reduce=false)
    {var r=new PlaceOrder(side,price==null?"Market":"Limit",qty,price,instrument,instrument=="BTCUSDT"?1:10,reduce);return JsonSerializer.Deserialize<Order>(await Cmd(u,"place",r,s=>s.Place(u,r)),Wire.Json)!;}
    private async Task<decimal> Available(Guid u,string asset){await using var db=Db();return(await db.Wallets.SingleAsync(x=>x.UserId==u&&x.Asset==asset&&x.Kind=="Unified Trading")).Available;}
    private async Task Price(decimal value,bool stale=false){await using var db=Db();await db.IndexSamples.ExecuteDeleteAsync();db.IndexSamples.Add(new IndexSample{Price=value,Spot=value,Time=DateTime.UtcNow.AddSeconds(stale?-60:0)});await db.SaveChangesAsync();}
    private async Task Conserve()
    {
        await using var db=Db();var wallets=await db.Wallets.ToListAsync();var lending=await db.LendingPools.ToListAsync();var loans=await db.Loans.ToListAsync();var amm=await db.LiquidityPools.SingleOrDefaultAsync();var clearing=await db.ClearingPools.SingleOrDefaultAsync();
        foreach(var asset in new[]{"BTC","USDT"}){
            var total=wallets.Where(x=>x.Asset==asset).Sum(x=>x.Available+x.Locked)+lending.Where(x=>x.Asset==asset).Sum(x=>x.Cash)+loans.Where(x=>x.CollateralAsset==asset).Sum(x=>x.Collateral)+(asset=="BTC"?amm?.Btc??0:(amm?.Usdt??0)+(clearing?.Cash??0));
            Assert.Equal(asset=="BTC"?200m:2000000m,total);
        }
        var journal=await db.Journal.ToListAsync();Assert.All(journal.GroupBy(x=>new{x.ReferenceId,x.Asset}),x=>Assert.Equal(0,x.Sum(y=>y.Delta)));
    }
    [Fact] public async Task DerivativesAreSeparateAndProfitRequiresFundedSettlement()
    {
        await Order(bob,"Sell",0.1m,50000);await Order(alice,"Buy",0.1m,null);
        await Order(bob,"Buy",0.1m,51000,reduce:true);await Order(alice,"Sell",0.1m,null,reduce:true);
        await Cmd(alice,"settle-cash",new{},s=>s.SettleCash(alice));
        Assert.Equal(10100,await Available(alice,"USDT"));Assert.Equal(9900,await Available(bob,"USDT"));
        await using var db=Db();Assert.All(await db.Positions.ToListAsync(),p=>{Assert.Equal(0,p.Quantity);Assert.Equal(0,p.Cost);});await Conserve();
    }
    [Fact] public async Task PositionCostSurvivesFractionalEntriesAndCompleteClose()
    {
        await Order(bob,"Sell",0.000001m,50000.01m);await Order(alice,"Buy",0.000001m,null);
        await Order(bob,"Sell",0.000002m,50000.02m);await Order(alice,"Buy",0.000002m,null);
        await Order(bob,"Buy",0.000003m,50000.03m,reduce:true);await Order(alice,"Sell",0.000003m,null,reduce:true);
        await Cmd(alice,"settle-cash",new{},s=>s.SettleCash(alice));Assert.Equal(10000.00000004m,await Available(alice,"USDT"));await Conserve();
    }
    [Fact] public async Task StaleFeedBlocksNewRiskButAllowsCollateralTopUpAndCancellation()
    {
        var order=await Order(alice,"Buy",1,50000);await Price(50000,true);
        await Assert.ThrowsAsync<RuleException>(()=>Order(bob,"Sell",1,50000));
        await Transfer(alice,"USDT",1);await Cmd(alice,"cancel",new{order.Id},s=>s.Cancel(alice,order.Id));await Conserve();
    }
    [Fact] public async Task CoveredCallsPreventCollateralBeingReusedOrTransferred()
    {
        await Order(alice,"Sell",1,1000,"CALL");
        await Assert.ThrowsAsync<RuleException>(()=>Transfer(alice,"BTC",0.000001m,"Unified Trading","Funding"));
        var supply=new SupplyRequest("BTC",0.01m);await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"supply",supply,s=>s.SupplyFunds(alice,supply)));
        await Conserve();
    }
    [Fact] public async Task FullyCollateralizedPutRejectsUnderfundedWriter()
    {
        await Transfer(alice,"BTC",1,"Unified Trading","Funding");
        await Assert.ThrowsAsync<RuleException>(()=>Order(alice,"Sell",1,1000,"PUT"));await Conserve();
    }
    [Fact] public async Task EmptyDerivativeMarketCannotInventFills()
    {
        var order=await Order(alice,"Buy",1,null);Assert.Equal("Cancelled",order.Status);Assert.Equal(1,order.Remaining);
        await using var db=Db();Assert.Empty(await db.Trades.ToListAsync());await Conserve();
    }
    [Fact] public async Task EarnRedemptionQueuesUntilBorrowerRepays()
    {
        var supply=new SupplyRequest("USDT",1000);await Cmd(alice,"supply",supply,s=>s.SupplyFunds(alice,supply));
        var borrow=new BorrowRequest("USDT",600,"BTC",0.02m);var loan=JsonSerializer.Deserialize<Loan>(await Cmd(bob,"borrow",borrow,s=>s.Borrow(bob,borrow)),Wire.Json)!;
        await Cmd(alice,"redeem",supply,s=>s.Redeem(alice,supply));
        await using(var db=Db()){Assert.True((await db.Supplies.FindAsync(alice,"USDT"))!.QueuedShares>0);}
        var repay=new RepayRequest(loan.Id,1000);await Cmd(bob,"repay",repay,s=>s.Repay(bob,repay));
        await using(var db=Db()){Assert.Equal(0,(await db.Supplies.FindAsync(alice,"USDT"))!.QueuedShares);Assert.Equal("Repaid",(await db.Loans.FindAsync(loan.Id))!.Status);}
        await Conserve();
    }
    [Fact] public async Task LendingRejectsUnfundedPoolAndExcessLtv()
    {
        var r=new BorrowRequest("USDT",600,"BTC",0.02m);await Assert.ThrowsAsync<RuleException>(()=>Cmd(bob,"borrow",r,s=>s.Borrow(bob,r)));
        var supply=new SupplyRequest("USDT",1000);await Cmd(alice,"supply",supply,s=>s.SupplyFunds(alice,supply));
        r=r with{Amount=601};await Assert.ThrowsAsync<RuleException>(()=>Cmd(bob,"borrow",r,s=>s.Borrow(bob,r)));await Conserve();
    }
    [Fact] public async Task InterestCarriesSubAtomicAmountsAcrossAccrualRuns()
    {
        var supply=new SupplyRequest("USDT",1);await Cmd(alice,"supply",supply,s=>s.SupplyFunds(alice,supply));
        var r=new BorrowRequest("USDT",0.1m,"BTC",0.00001m);await Cmd(bob,"borrow",r,s=>s.Borrow(bob,r));
        await using(var db=Db()){var pool=await db.LendingPools.FindAsync("USDT");pool!.AccruedAt=DateTime.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();}
        await Cmd(alice,"accrue",new{},async s=>{await using var other=Db();return new{};});
        await using(var db=Db()){var pool=await db.LendingPools.FindAsync("USDT");await new TradingService(db).Accrue(pool!);await db.SaveChangesAsync();var loan=await db.Loans.SingleAsync();Assert.True(loan.InterestRemainder>0);Assert.True(loan.InterestRemainder<0.00000001m);}
        await Conserve();
    }
    [Fact] public async Task AmmSwapConservesAssetsAndEnforcesSlippageAndDeadline()
    {
        var add=new LiquidityRequest(0.1m,5000);await Cmd(alice,"add-liquidity",add,s=>s.AddLiquidity(alice,add));
        var swap=new SwapRequest("USDT",100,0.01m,DateTime.UtcNow.AddMinutes(1));await Assert.ThrowsAsync<RuleException>(()=>Cmd(bob,"swap",swap,s=>s.Swap(bob,swap)));
        swap=swap with{MinimumReceived=0.001m};await Cmd(bob,"swap",swap,s=>s.Swap(bob,swap));
        await using(var db=Db()){var p=await db.LiquidityPools.SingleAsync();Assert.True(p.Btc*p.Usdt>=500m);}
        swap=swap with{Deadline=DateTime.UtcNow.AddSeconds(-1)};await Assert.ThrowsAsync<RuleException>(()=>Cmd(bob,"swap",swap,s=>s.Swap(bob,swap)));await Conserve();
    }
    [Fact] public async Task ConcurrentAmmSwapsCannotOverdrawOrReplay()
    {
        var add=new LiquidityRequest(0.1m,5000);await Cmd(alice,"add-liquidity",add,s=>s.AddLiquidity(alice,add));
        var swap=new SwapRequest("USDT",100,0,DateTime.UtcNow.AddMinutes(1));var key=Guid.NewGuid().ToString();
        var responses=await Task.WhenAll(Cmd(bob,"swap",swap,s=>s.Swap(bob,swap),key),Cmd(bob,"swap",swap,s=>s.Swap(bob,swap),key));Assert.Equal(responses[0],responses[1]);Assert.Equal(9900,await Available(bob,"USDT"));await Conserve();
    }
    [Fact] public async Task DcaExecutesOneDueRunAgainstRealOrdersAndDoesNotCatchUp()
    {
        await Order(bob,"Sell",0.1m,50000,"BTCUSDT");var request=new DcaRequest(100,1,200);
        var dca=JsonSerializer.Deserialize<DcaSchedule>(await Cmd(alice,"dca",request,s=>s.CreateDca(alice,request)),Wire.Json)!;
        await using(var db=Db()){(await db.DcaSchedules.FindAsync(dca.Id))!.NextRun=DateTime.UtcNow.AddDays(-2);await db.SaveChangesAsync();}
        await Cmd(alice,"dca-run",new{dca.Id},async s=>{await s.RunDca(dca.Id);return new{};});
        await Cmd(alice,"dca-run",new{dca.Id},async s=>{await s.RunDca(dca.Id);return new{};});
        Assert.Equal(1.002m,await Available(alice,"BTC"));Assert.Equal(9900,await Available(alice,"USDT"));await Conserve();
    }
    [Fact] public async Task MigrationBuildsEmptyDatabaseAndHasNoPendingModelChanges()
    {await using var db=Db();await db.Database.EnsureDeletedAsync();await db.Database.MigrateAsync();Assert.False(db.Database.HasPendingModelChanges());Assert.True(await db.Database.CanConnectAsync());}
    [Fact] public async Task LiquidationReducesOnlyAgainstActualRestingLiquidity()
    {
        await Transfer(alice,"BTC",1,"Unified Trading","Funding");await Transfer(alice,"USDT",9400,"Unified Trading","Funding");
        await Order(bob,"Sell",0.1m,50000);await Order(alice,"Buy",0.1m,null);
        await Price(40000);await Order(bob,"Buy",0.1m,40000,reduce:true);
        await Cmd(alice,"liquidate",new{},async s=>{await s.LiquidatePositions(alice);return new{};});
        await using var db=Db();Assert.Equal(0.075m,(await db.Positions.FindAsync(alice,"BTC-PERP"))!.Quantity);Assert.Single(await db.RiskEvents.ToListAsync());await Conserve();
    }
    [Fact] public async Task LoanLiquidationUsesPoolReservesAndReturnsSurplusCollateral()
    {
        var supply=new SupplyRequest("USDT",1000);await Cmd(alice,"supply",supply,s=>s.SupplyFunds(alice,supply));
        var add=new LiquidityRequest(0.1m,5000);await Cmd(alice,"add-liquidity",add,s=>s.AddLiquidity(alice,add));
        var borrow=new BorrowRequest("USDT",600,"BTC",0.02m);var loan=JsonSerializer.Deserialize<Loan>(await Cmd(bob,"borrow",borrow,s=>s.Borrow(bob,borrow)),Wire.Json)!;
        await Price(30000);await Cmd(bob,"liquidate-loan",new{loan.Id},async s=>{await s.LiquidateLoan(loan.Id);return new{};});
        await using var db=Db();var result=(await db.Loans.FindAsync(loan.Id))!;Assert.Equal("Liquidated",result.Status);Assert.Equal(0,result.Principal+result.Interest+result.Collateral);Assert.True(await Available(bob,"BTC")>0.98m);await Conserve();
    }
    [Theory]
    [InlineData("Option")]
    [InlineData("Future")]
    public async Task ExpirySettlesOnceUsingCompleteTwap(string kind)
    {
        var instrument=kind=="Option"?"CALL":"DELIVERY";
        if(kind=="Future"){await using var db=Db();db.Instruments.Add(new Instrument{Id=instrument,Kind="Future",Expiry=DateTime.UtcNow.AddDays(1)});await db.SaveChangesAsync();}
        await Order(bob,"Sell",0.1m,kind=="Option"?1000:50000,instrument);await Order(alice,"Buy",0.1m,null,instrument);
        var expiry=DateTime.UtcNow.AddSeconds(-2);
        await using(var db=Db()){
            (await db.Instruments.FindAsync(instrument))!.Expiry=expiry;
            for(var t=expiry.AddMinutes(-30);t<=expiry;t=t.AddSeconds(4))db.IndexSamples.Add(new IndexSample{Time=t,Price=52000,Spot=52000});
            await db.SaveChangesAsync();
        }
        await Cmd(alice,"lifecycle",new{},async s=>{await s.ProcessMarkets();return new{};});
        await Cmd(alice,"lifecycle",new{},async s=>{await s.ProcessMarkets();return new{};});
        await using var verify=Db();Assert.Equal("Settled",(await verify.Instruments.FindAsync(instrument))!.Status);Assert.Equal(0,(await verify.Positions.FindAsync(alice,instrument))!.Quantity);Assert.Equal(200,(await verify.MarginBalances.FindAsync(alice))!.Cash);
        await Conserve();
    }
    [Fact] public async Task ExpiryDefersWhenTwapCoverageIsMissing()
    {
        await Order(bob,"Sell",0.1m,1000,"CALL");await Order(alice,"Buy",0.1m,null,"CALL");
        await using(var db=Db()){(await db.Instruments.FindAsync("CALL"))!.Expiry=DateTime.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();}
        await Cmd(alice,"lifecycle",new{},async s=>{await s.ProcessMarkets();return new{};});
        await using var verify=Db();Assert.Equal("Trading",(await verify.Instruments.FindAsync("CALL"))!.Status);Assert.Equal(0.1m,(await verify.Positions.FindAsync(alice,"CALL"))!.Quantity);await Conserve();
    }
    [Fact] public async Task UnfundedRealizedCashCannotCreateSpendableTokens()
    {
        await using(var db=Db()){db.MarginBalances.Add(new MarginBalance{UserId=alice,Cash=100});await db.SaveChangesAsync();}
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"settle-cash",new{},s=>s.SettleCash(alice)));
        Assert.Equal(10000,await Available(alice,"USDT"));await Conserve();
    }
    [Fact] public async Task FundingChargesBoundaryPositionsOnceAndRespectsRateCap()
    {
        await Order(bob,"Sell",0.1m,50500);await Order(alice,"Buy",0.1m,null);
        var now=DateTime.UtcNow;var boundary=new DateTime(now.Ticks/(TimeSpan.TicksPerHour*8)*(TimeSpan.TicksPerHour*8),DateTimeKind.Utc);
        await using(var db=Db()){
            await db.IndexSamples.ExecuteDeleteAsync();await db.Trades.ExecuteUpdateAsync(x=>x.SetProperty(t=>t.CreatedAt,boundary.AddHours(-1)));
            for(var t=boundary.AddHours(-8);t<=boundary;t=t.AddSeconds(4))db.IndexSamples.Add(new IndexSample{Time=t,Price=50000,Spot=50000});
            db.IndexSamples.Add(new IndexSample{Time=now,Price=50000,Spot=50000});await db.SaveChangesAsync();
        }
        await Cmd(alice,"funding",new{},async s=>{await s.ProcessMarkets();return new{};});
        await Cmd(alice,"funding",new{},async s=>{await s.ProcessMarkets();return new{};});
        Assert.Equal(9997.5m,await Available(alice,"USDT"));await using var verify=Db();Assert.Equal(0.0005m,(await verify.FundingEvents.SingleAsync()).Rate);Assert.Equal(2.5m,(await verify.MarginBalances.FindAsync(bob))!.Cash);await Conserve();
    }
    [Fact] public async Task VersionedUpgradePreservesExistingExactBalances()
    {
        await using var db=Db();await db.Database.EnsureDeletedAsync();
        var migrator=db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync("20261103124042_InitialSpot");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO exchange.\"Wallets\" (\"UserId\",\"Kind\",\"Asset\",\"Available\",\"Locked\") VALUES ({alice},'Funding','BTC',0.12345678,0.00000001)");
        await migrator.MigrateAsync();var wallet=await db.Wallets.SingleAsync();Assert.Equal(0.12345678m,wallet.Available);Assert.Equal(0.00000001m,wallet.Locked);
    }
    [Fact] public async Task SpotCannotBypassCollateralChecksUsingDerivativeFlags()
    {
        await Assert.ThrowsAsync<RuleException>(()=>Order(alice,"Buy",0.1m,50000,"BTCUSDT",true));
        var request=new PlaceOrder("Buy","Limit",0.1m,50000,"BTCUSDT",10);
        await Assert.ThrowsAsync<RuleException>(()=>Cmd(alice,"place",request,s=>s.Place(alice,request)));await Conserve();
    }
}
