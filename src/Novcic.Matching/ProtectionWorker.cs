using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Matching;
public class ProtectionWorker(IServiceScopeFactory scopes,ILogger<ProtectionWorker> log):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        while(await timer.WaitForNextTickAsync(stop))try{
            using var read=scopes.CreateScope();var reader=read.ServiceProvider.GetRequiredService<ExchangeDb>();
            var ids=await reader.Protections.Where(x=>x.Status=="Closing"&&x.NextAttemptAt<=DateTime.UtcNow).OrderBy(x=>x.NextAttemptAt).Select(x=>x.Id).Take(100).ToListAsync(stop);
            foreach(var id in ids){
                using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ExchangeDb>();
                await using var tx=await db.Database.BeginTransactionAsync(stop);
                // Join the same FIFO lock queue as commands. Repeated try-lock polling can
                // starve closing indefinitely while the liquidity worker holds this lock.
                // This worker has only one awaited attempt, so it cannot build a backlog.
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001234)",stop);
                await scope.ServiceProvider.GetRequiredService<TradingService>().AttemptClose(id);
                await db.SaveChangesAsync(stop);await scope.ServiceProvider.GetRequiredService<TradingService>().AdvanceRevision();await tx.CommitAsync(stop);
            }
        }catch(Exception e)when(!stop.IsCancellationRequested){log.LogWarning(e,"Position closing cycle deferred");}
    }
}
