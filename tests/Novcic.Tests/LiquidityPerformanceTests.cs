using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Novcic.Core;
namespace Novcic.Tests;

public class LiquidityPerformanceTests
{
    private static LiquidityOperation Deposit(decimal shares,decimal? usd)=>new(){Status="Completed",Kind="Deposit",Shares=shares,DepositValueUsd=usd};
    private static LiquidityOperation Withdraw(decimal shares,decimal? usd)=>new(){Status="Completed",Kind="Withdrawal",Shares=shares,WithdrawalValueUsd=usd};

    [Fact] public void PerformanceIncludesWithdrawalsAndNeverAddsCompoundedFeesAgain()
    {
        var report=TradingService.CalculateLiquidityPerformance(50,100,1200,[Deposit(100,1000),Withdraw(50,650)]);
        Assert.Equal(12,report.SharePriceUsd);Assert.Equal(600,report.CurrentValueUsd);
        Assert.Equal(1000,report.TotalDepositedUsd);Assert.Equal(650,report.TotalWithdrawnUsd);
        Assert.Equal(250,report.PnlUsd);Assert.Equal(25,report.ReturnPercent);Assert.True(report.CompleteHistory);
        var exited=TradingService.CalculateLiquidityPerformance(0,0,null,[Deposit(100,1000),Withdraw(100,900)]);
        Assert.Equal(-100,exited.PnlUsd);Assert.Equal(-10,exited.ReturnPercent);
    }

    [Fact] public void DepositBasisDoesNotMoveWithBtcAndPendingRequestsDoNotChangePerformance()
    {
        var pending=Deposit(20,500);pending.Status="Pending";
        var cancelled=Withdraw(10,500);cancelled.Status="Cancelled";
        var report=TradingService.CalculateLiquidityPerformance(20,100,2000,[Deposit(20,500),pending,cancelled]);
        Assert.Equal(500,report.TotalDepositedUsd);Assert.Equal(-100,report.PnlUsd);
        report=TradingService.CalculateLiquidityPerformance(20,100,4000,[Deposit(20,500)]);
        Assert.Equal(500,report.TotalDepositedUsd);Assert.Equal(300,report.PnlUsd);
    }

    [Fact] public void StaleValuationAndIncompleteLegacyHistoryRemainUnavailable()
    {
        var report=TradingService.CalculateLiquidityPerformance(20,100,null,[Deposit(20,500)]);
        Assert.Null(report.CurrentValueUsd);Assert.Null(report.PnlUsd);Assert.Equal(500,report.TotalDepositedUsd);
        foreach(var history in new[]{new[]{Deposit(20,null)},new[]{Deposit(10,250)}}){
            report=TradingService.CalculateLiquidityPerformance(20,100,2000,history);
            Assert.Equal(400,report.CurrentValueUsd);Assert.Null(report.PnlUsd);Assert.False(report.CompleteHistory);
        }
        report=TradingService.CalculateLiquidityPerformance(20,100,2000,[Deposit(20,500)],legacy:true);
        Assert.Null(report.TotalDepositedUsd);Assert.Null(report.PnlUsd);
    }
}

public partial class ProductTests
{
    [Fact] public async Task LiquidityPerformanceSavesHistoricalBasisAndDoesNotMutateAccounting()
    {
        var request=new QueueDeposit(0.1m,5000);await Cmd(alice,"liquidity-deposit",request,s=>s.QueueLiquidity(alice,request));await Cycle();
        await using var db=Db();var svc=new TradingService(db);
        Assert.Equal(10000,(await db.LiquidityOperations.SingleAsync()).DepositValueUsd);
        var report=await svc.LiquidityPerformance(alice);Assert.Equal(0,report.PnlUsd);
        var shares=(await db.LiquidityShares.SingleAsync()).Shares;
        await Price(55000);db.ChangeTracker.Clear();
        report=await svc.LiquidityPerformance(alice);
        Assert.Equal(10000,report.TotalDepositedUsd);Assert.Equal(500,report.PnlUsd);
        Assert.Equal(shares,(await db.LiquidityShares.SingleAsync()).Shares);Assert.False(db.ChangeTracker.HasChanges());await Conserve();
        await Price(55000,true);db.ChangeTracker.Clear();
        report=await svc.LiquidityPerformance(alice);Assert.Null(report.CurrentValueUsd);Assert.Equal(10000,report.TotalDepositedUsd);
    }

    [Fact] public async Task HistoricalLiquidityBackfillUsesEventPricesAndIncludesWithdrawalDust()
    {
        await using var db=Db();var time=DateTime.UtcNow.AddDays(-1);
        db.IndexSamples.AddRange(new IndexSample{Time=time.AddSeconds(-1),Price=50000,Spot=50000},new IndexSample{Time=time.AddSeconds(1),Price=90000,Spot=90000});
        var btc=new LiquidityOperation{UserId=alice,Btc=0.1m,Usdt=100,CreatedAt=time};
        var stale=new LiquidityOperation{UserId=alice,Btc=0.1m,CreatedAt=time.AddSeconds(-20)};
        var usd=new LiquidityOperation{UserId=alice,Usdt=19750,CreatedAt=time.AddDays(-1)};
        var withdrawal=new LiquidityOperation{UserId=alice,Kind="Withdrawal",Asset="USDT",Received=500,Status="Completed",CompletedAt=time};
        db.LiquidityOperations.AddRange(btc,stale,usd,withdrawal);
        db.Ledger.Add(new LedgerEntry{UserId=alice,Asset="BTC",AvailableDelta=0.00000010m,Reason="Liquidity withdrawal dust",ReferenceId=withdrawal.Id});
        await db.SaveChangesAsync();
        var migration=new Novcic.Core.Migrations.LiquidityPerformanceAndClosingBackoff();
        foreach(var sql in migration.UpOperations.OfType<SqlOperation>())await db.Database.ExecuteSqlRawAsync(sql.Sql);
        db.ChangeTracker.Clear();
        Assert.Equal(5100,(await db.LiquidityOperations.FindAsync(btc.Id))!.DepositValueUsd);
        Assert.Null((await db.LiquidityOperations.FindAsync(stale.Id))!.DepositValueUsd);
        Assert.Equal(19750,(await db.LiquidityOperations.FindAsync(usd.Id))!.DepositValueUsd);
        Assert.Equal(500.005m,(await db.LiquidityOperations.FindAsync(withdrawal.Id))!.WithdrawalValueUsd);
    }
}
