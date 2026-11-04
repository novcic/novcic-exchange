using Microsoft.EntityFrameworkCore;
using System.Numerics;
namespace Novcic.Core;

public record LiquidityFeeEarnings(decimal? Btc,decimal? Usdt,DateTime? Since,bool CompleteHistory);

public partial class TradingService
{
    // Read-only attribution. These fees remain in pool NAV; they are not an extra claim.
    public async Task<LiquidityFeeEarnings> LiquidityFees(Guid user)
    {
        var pool=await db.LiquidityPools.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==1);
        if(pool==null)return new(0,0,null,true);
        var holdings=await db.LiquidityShares.AsNoTracking().ToDictionaryAsync(x=>x.UserId,x=>x.Shares);
        var operations=await db.LiquidityOperations.AsNoTracking().Where(x=>x.PoolId==1&&x.Status=="Completed").ToListAsync();
        if(holdings.GetValueOrDefault(user)==0&&!operations.Any(x=>x.UserId==user))return new(0,0,null,true);
        // Legacy direct share changes lack a share-quantity event. Reconstruct only
        // the period after the last such change, from current holdings backwards.
        var legacyCutoff=await db.Ledger.Where(x=>(x.Reason=="Liquidity provision"||x.Reason=="Liquidity withdrawal")&&!db.LiquidityOperations.Any(o=>o.Id==x.ReferenceId)).MaxAsync(x=>(DateTime?)x.CreatedAt);
        var fees=await db.Ledger.AsNoTracking().Where(x=>x.UserId==AmmAccount&&x.Reason=="LP fee income").OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).ToListAsync();
        // Cash fee claims enter NAV at accrual. Their later funded settlement is
        // a cash-to-wallet conversion, not a second fee-earning event.
        var account=$"margin:{AmmAccount}";
        fees.AddRange(await db.Journal.AsNoTracking().Where(x=>x.Account==account&&x.Reason=="LP fee income accrued")
            .Select(x=>new LedgerEntry{Id=x.Id,Asset=x.Asset,AvailableDelta=x.Delta,CreatedAt=x.CreatedAt}).ToListAsync());
        return AttributeLiquidityFees(user,pool.Shares,holdings,operations,fees,legacyCutoff);
    }

    public static LiquidityFeeEarnings AttributeLiquidityFees(Guid user,decimal totalShares,IReadOnlyDictionary<Guid,decimal> current,IReadOnlyList<LiquidityOperation> operations,IReadOnlyList<LedgerEntry> fees,DateTime? legacyCutoff)
    {
        LiquidityFeeEarnings Unknown()=>new(null,null,null,false);
        if(current.Values.Any(x=>x<0)||current.Values.Sum()!=totalShares||operations.Any(x=>x.CompletedAt==null||x.Shares<=0||x.Kind is not("Deposit" or "Withdrawal")))return Unknown();
        var changes=operations.Where(x=>legacyCutoff==null||x.CompletedAt>legacyCutoff).OrderBy(x=>x.CompletedAt).ThenBy(x=>x.Id).ToList();
        var holdings=current.ToDictionary();
        foreach(var change in changes){var delta=change.Kind=="Deposit"?change.Shares:-change.Shares;holdings[change.UserId]=holdings.GetValueOrDefault(change.UserId)-delta;}
        if(holdings.Values.Any(x=>x<0))return Unknown();
        var supply=holdings.Values.Sum();var own=holdings.GetValueOrDefault(user);
        var orderedFees=fees.OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).ToList();
        var complete=legacyCutoff==null||orderedFees.All(x=>x.CreatedAt>legacyCutoff);
        DateTime? since=own>0?(complete?orderedFees.FirstOrDefault()?.CreatedAt:legacyCutoff):null;
        BigInteger btcNumerator=0,btcDenominator=1,usdNumerator=0,usdDenominator=1;var cursor=0;
        foreach(var fee in orderedFees){
            if(legacyCutoff.HasValue&&fee.CreatedAt<=legacyCutoff.Value)continue;
            while(cursor<changes.Count&&changes[cursor].CompletedAt<=fee.CreatedAt){
                var change=changes[cursor++];var delta=change.Kind=="Deposit"?change.Shares:-change.Shares;
                supply+=delta;holdings[change.UserId]=holdings.GetValueOrDefault(change.UserId)+delta;
                if(supply<0||holdings[change.UserId]<0)return Unknown();
                own=holdings.GetValueOrDefault(user);if(own>0&&since==null)since=change.CompletedAt;
            }
            var amount=fee.AvailableDelta+fee.LockedDelta;
            if(amount<0||supply<=0||own>supply||fee.Asset is not("BTC" or "USDT"))return Unknown();
            // Ownership can earn fractions of one ledger unit. Sum rational units
            // exactly before truncating, rather than losing dust on every fill.
            var numerator=new BigInteger(amount*100000000m)*new BigInteger(own*100000000m);
            var denominator=new BigInteger(supply*100000000m);
            if(fee.Asset=="BTC")AddFeeFraction(ref btcNumerator,ref btcDenominator,numerator,denominator);
            else AddFeeFraction(ref usdNumerator,ref usdDenominator,numerator,denominator);
        }
        return new((decimal)(btcNumerator/btcDenominator)/100000000m,(decimal)(usdNumerator/usdDenominator)/100000000m,since,complete);
    }

    private static void AddFeeFraction(ref BigInteger total,ref BigInteger divisor,BigInteger value,BigInteger denominator)
    {
        total=total*denominator+value*divisor;divisor*=denominator;
        var common=BigInteger.GreatestCommonDivisor(total,divisor);total/=common;divisor/=common;
    }
}
