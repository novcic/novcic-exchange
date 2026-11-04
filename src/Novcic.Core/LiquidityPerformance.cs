using Microsoft.EntityFrameworkCore;
namespace Novcic.Core;

public record LiquidityPerformance(decimal? SharePriceUsd,decimal? CurrentValueUsd,decimal? TotalDepositedUsd,decimal? TotalWithdrawnUsd,decimal? PnlUsd,decimal? ReturnPercent,bool CompleteHistory);

public partial class TradingService
{
    public async Task<LiquidityPerformance> LiquidityPerformance(Guid user)
    {
        var pool=await db.LiquidityPools.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==1);
        var shares=await db.LiquidityShares.AsNoTracking().Where(x=>x.UserId==user).Select(x=>x.Shares).SingleOrDefaultAsync();
        var operations=await db.LiquidityOperations.AsNoTracking().Where(x=>x.UserId==user&&x.PoolId==1&&x.Status=="Completed").ToListAsync();
        var legacy=await db.Ledger.AnyAsync(x=>x.UserId==user&&(x.Reason=="Liquidity provision"||x.Reason=="Liquidity withdrawal")&&!db.LiquidityOperations.Any(o=>o.Id==x.ReferenceId));
        var sample=await db.IndexSamples.AsNoTracking().OrderByDescending(x=>x.Id).FirstOrDefaultAsync();
        var fresh=sample!=null&&DateTime.UtcNow-sample.Time<=TimeSpan.FromSeconds(5);
        // LP valuation depends on the pool's exposures, not the holder's personal margin account.
        if(fresh&&pool?.MakerId!=null){
            var options=await (from p in db.Positions join i in db.Instruments on p.InstrumentId equals i.Id
                where p.UserId==pool.MakerId&&p.Quantity!=0&&i.Kind=="Option" select i).ToListAsync();
            foreach(var option in options)if(await QuoteMark(option,sample!.Price)==null){fresh=false;break;}
        }
        var nav=pool!=null&&fresh?(decimal?)await PoolValue(pool,sample!.Price):null;
        return CalculateLiquidityPerformance(shares,pool?.Shares??0,nav,operations,legacy);
    }

    public static LiquidityPerformance CalculateLiquidityPerformance(decimal shares,decimal supply,decimal? nav,IReadOnlyList<LiquidityOperation> operations,bool legacy=false)
    {
        var completed=operations.Where(x=>x.Status=="Completed").ToList();
        var deposits=completed.Where(x=>x.Kind=="Deposit").ToList();
        var withdrawals=completed.Where(x=>x.Kind=="Withdrawal").ToList();
        var reconciled=!legacy&&shares>=0&&shares<=supply&&completed.All(x=>x.Shares>0&&x.Kind is "Deposit" or "Withdrawal")&&deposits.Sum(x=>x.Shares)-withdrawals.Sum(x=>x.Shares)==shares;
        var invested=reconciled&&deposits.All(x=>x.DepositValueUsd.HasValue)?deposits.Sum(x=>x.DepositValueUsd):(decimal?)null;
        var withdrawn=reconciled&&withdrawals.All(x=>x.WithdrawalValueUsd.HasValue)?withdrawals.Sum(x=>x.WithdrawalValueUsd):(decimal?)null;
        var price=supply>0?nav/supply:null;
        var value=shares==0?0:shares>0&&shares<=supply&&nav.HasValue?decimal.Round(nav.Value*shares/supply,8):(decimal?)null;
        var pnl=value+withdrawn-invested;
        return new(price,value,invested,withdrawn,pnl,invested>0?pnl/invested*100:null,reconciled&&invested.HasValue&&withdrawn.HasValue);
    }
}
