using Novcic.Core;
using Microsoft.EntityFrameworkCore;
namespace Novcic.Tests;

public class LiquidityAttributionTests
{
    private static readonly Guid A=Guid.NewGuid(),B=Guid.NewGuid();
    private static readonly DateTime Start=new(2026,11,1,0,0,0,DateTimeKind.Utc);
    private static LedgerEntry Fee(int second,string asset,decimal amount)=>new(){Id=second,Asset=asset,AvailableDelta=amount,CreatedAt=Start.AddSeconds(second)};
    private static LiquidityOperation Change(Guid user,int second,string kind,decimal shares)=>new(){UserId=user,Kind=kind,Shares=shares,Status="Completed",CompletedAt=Start.AddSeconds(second)};

    [Fact] public void FeesFollowOwnershipAtAccrualRatherThanCurrentOwnership()
    {
        var current=new Dictionary<Guid,decimal>{{A,50},{B,100}};
        var fees=new[]{Fee(1,"USDT",1),Fee(3,"USDT",1),Fee(4,"BTC",0.000003m)};
        var changes=new[]{Change(B,2,"Deposit",50)};
        var a=TradingService.AttributeLiquidityFees(A,150,current,changes,fees,null);
        var b=TradingService.AttributeLiquidityFees(B,150,current,changes,fees,null);
        Assert.Equal(0.83333333m,a.Usdt);Assert.Equal(1.16666666m,b.Usdt);
        Assert.Equal(0.000001m,a.Btc);Assert.Equal(0.000002m,b.Btc);Assert.True(a.CompleteHistory);
    }

    [Fact] public void NewLpDoesNotReceiveEarlierFeesAndExitedLpKeepsHistoricalAttribution()
    {
        var changes=new[]{Change(B,2,"Deposit",50),Change(B,4,"Withdrawal",50)};
        var fees=new[]{Fee(1,"USDT",10),Fee(3,"USDT",10),Fee(5,"USDT",10)};
        var result=TradingService.AttributeLiquidityFees(B,50,new Dictionary<Guid,decimal>{{A,50},{B,0}},changes,fees,null);
        Assert.Equal(5,result.Usdt);Assert.Equal(Start.AddSeconds(2),result.Since);
    }

    [Fact] public void LegacyCutoffExcludesUnsupportedEarlierAttribution()
    {
        var result=TradingService.AttributeLiquidityFees(A,100,new Dictionary<Guid,decimal>{{A,25},{B,75}},[],[Fee(1,"USDT",100),Fee(3,"USDT",8)],Start.AddSeconds(2));
        Assert.Equal(2,result.Usdt);Assert.Equal(Start.AddSeconds(2),result.Since);Assert.False(result.CompleteHistory);
    }

    [Fact] public void InconsistentOwnershipReturnsUnavailableInsteadOfAnInventedReward()
    {
        var result=TradingService.AttributeLiquidityFees(A,100,new Dictionary<Guid,decimal>{{A,50}},[],[Fee(1,"USDT",10)],null);
        Assert.Null(result.Usdt);Assert.Null(result.Btc);
        result=TradingService.AttributeLiquidityFees(A,50,new Dictionary<Guid,decimal>{{A,50}},[Change(A,2,"Deposit",60)],[Fee(1,"USDT",10)],null);
        Assert.Null(result.Usdt);
    }

    [Fact] public void FractionalAttributionIsRoundedOnlyAfterSummingFees()
    {
        var result=TradingService.AttributeLiquidityFees(A,3,new Dictionary<Guid,decimal>{{A,1},{B,2}},[],Enumerable.Range(1,6).Select(i=>Fee(i,"BTC",0.00000001m)).ToList(),null);
        Assert.Equal(0.00000002m,result.Btc);
    }
}

public partial class ProductTests
{
    [Fact] public async Task LiquidityFeeReportingReadsActualFeesWithoutChangingBalancesOrShares()
    {
        var deposit=new QueueDeposit(0.1m,5000);await Cmd(alice,"liquidity-deposit",deposit,s=>s.QueueLiquidity(alice,deposit));await Cycle();
        await Order(bob,"Buy",0.001m,null,"BTCUSDT");
        await using var db=Db();var service=new TradingService(db);
        var before=await db.Wallets.AsNoTracking().Select(x=>new{x.UserId,x.Asset,x.Kind,x.Available,x.Locked}).ToListAsync();
        var shares=(await db.LiquidityShares.AsNoTracking().SingleAsync()).Shares;
        var report=await service.LiquidityFees(alice);
        Assert.Equal(0.00000080m,report.Btc);Assert.Equal(0,report.Usdt);Assert.True(report.CompleteHistory);
        Assert.Equal(0,(await service.LiquidityFees(bob)).Btc);
        Assert.Equal(shares,(await db.LiquidityShares.AsNoTracking().SingleAsync()).Shares);
        Assert.Equal(before,await db.Wallets.AsNoTracking().Select(x=>new{x.UserId,x.Asset,x.Kind,x.Available,x.Locked}).ToListAsync());
        Assert.False(db.ChangeTracker.HasChanges());await Conserve();
    }
}
