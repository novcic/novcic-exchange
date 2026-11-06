using Microsoft.EntityFrameworkCore;
namespace Novcic.Core;

public record AccountAsset(string Kind, string Asset, decimal WalletBalance, decimal Locked, decimal? UnrealizedPnl, decimal RealizedCash, decimal? Equity, decimal Transferable, decimal? UsdValue);

public partial class TradingService
{
    public async Task<List<AccountAsset>> AccountAssets(Guid user, RiskSnapshot risk)
    {
        var wallets=await db.Wallets.AsNoTracking().Where(w=>w.UserId==user).ToListAsync();
        var hasPositions=await db.Positions.AnyAsync(p=>p.UserId==user&&p.Quantity!=0);
        var result=new List<AccountAsset>();
        foreach(var kind in Kinds)foreach(var asset in Assets){
            var wallet=wallets.SingleOrDefault(w=>w.Kind==kind&&w.Asset==asset);
            var balance=(wallet?.Available??0)+(wallet?.Locked??0);
            var settlement=kind=="Unified Trading"&&asset=="USDT";
            decimal? unrealized=settlement ? hasPositions&&!risk.PriceFresh ? null : risk.UnrealizedPnl : 0;
            var cash=settlement?risk.RealizedCash:0;
            var equity=balance+cash+unrealized;
            var transferable=kind=="Funding"?wallet?.Available??0:asset=="BTC"?risk.TransferableBtc:risk.TransferableUsdt;
            decimal? usd=asset=="USDT"?equity:equity==0?0:risk.PriceFresh?equity*risk.IndexPrice:null;
            result.Add(new(kind,asset,balance,wallet?.Locked??0,unrealized,cash,equity,transferable,usd));
        }
        return result;
    }

    public async Task<decimal?> PublicMark(string instrumentId)
    {
        var instrument=await db.Instruments.FindAsync(instrumentId);
        if(instrument==null||instrument.Kind=="Spot"||instrument.Expiry<=DateTime.UtcNow)return null;
        var index=await db.IndexSamples.OrderByDescending(s=>s.Id).FirstOrDefaultAsync();
        if(index==null||DateTime.UtcNow-index.Time>TimeSpan.FromSeconds(5))return null;
        if(instrument.Kind=="Option"&&await QuoteMark(instrument,index.Price)==null)return null;
        return await Mark(instrument,index.Price);
    }
}
