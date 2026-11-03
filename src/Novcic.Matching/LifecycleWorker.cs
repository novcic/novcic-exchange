using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Matching;
public class LifecycleWorker(IServiceScopeFactory scopes,ILogger<LifecycleWorker> log):BackgroundService
{
    private async Task Run(Func<TradingService,Task> action,CancellationToken stop)
    {
        using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ExchangeDb>();
        await using var tx=await db.Database.BeginTransactionAsync(stop);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001234)",stop);
        await action(scope.ServiceProvider.GetRequiredService<TradingService>());
        await db.SaveChangesAsync(stop);await scope.ServiceProvider.GetRequiredService<TradingService>().AdvanceRevision();await tx.CommitAsync(stop);
    }
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
        while(await timer.WaitForNextTickAsync(stop))try {
            await Run(s=>s.ProcessMarkets(),stop);
            try{await Run(s=>s.ProcessLiquidity(),stop);}catch(RuleException e){log.LogWarning("AMM cycle deferred: {Reason}",e.Message);}
            using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ExchangeDb>();
            var users=await db.Positions.Where(x=>x.Quantity!=0).Select(x=>x.UserId).Distinct().ToListAsync(stop);
            var loans=await db.Loans.Where(x=>x.Status=="Active").Select(x=>x.Id).ToListAsync(stop);
            var schedules=await db.DcaSchedules.Where(x=>!x.Paused&&x.NextRun<=DateTime.UtcNow).Select(x=>x.Id).ToListAsync(stop);
            foreach(var user in users)try{await Run(s=>s.LiquidatePositions(user),stop);}catch(RuleException e){log.LogDebug("Liquidation deferred: {Reason}",e.Message);}
            foreach(var loan in loans)try{await Run(s=>s.LiquidateLoan(loan),stop);}catch(RuleException e){log.LogDebug("Loan liquidation deferred: {Reason}",e.Message);}
            foreach(var id in schedules)try{await Run(s=>s.RunDca(id),stop);}catch(RuleException e){
                await using var tx=await db.Database.BeginTransactionAsync(stop);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001234)",stop);
                var schedule=await db.DcaSchedules.FindAsync([id],stop);
                if(schedule!=null&&schedule.NextRun<=DateTime.UtcNow){schedule.NextRun=DateTime.UtcNow.AddHours(schedule.Hours);schedule.LastOutcome="Suspended: "+e.Message;await db.SaveChangesAsync(stop);}
                await tx.CommitAsync(stop);
            }
        }catch(Exception e) when(!stop.IsCancellationRequested){log.LogWarning(e,"Market lifecycle deferred.");}
    }
}
