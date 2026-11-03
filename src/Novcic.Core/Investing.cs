using Microsoft.EntityFrameworkCore;
namespace Novcic.Core;
public record SupplyRequest(string Asset,decimal Amount);
public record BorrowRequest(string Asset,decimal Amount,string CollateralAsset,decimal Collateral);
public record RepayRequest(Guid LoanId,decimal Amount);
public record LiquidityRequest(decimal Btc,decimal Usdt);
public record RemoveLiquidityRequest(decimal Shares);
public record SwapRequest(string Asset,decimal Amount,decimal MinimumReceived,DateTime Deadline);
public record DcaRequest(decimal Amount,int Hours,decimal? Budget);
public partial class TradingService
{
    private static decimal Down(decimal n)=>decimal.Floor(n*100000000m)/100000000m;
    private static decimal Sqrt(decimal n){if(n<=0)return 0;var x=n>1?n/2:1;for(int i=0;i<100;i++){var next=(x+n/x)/2;if(Math.Abs(next-x)<0.000000000001m)return next;x=next;}return x;}
    private void SystemPost(string account,string asset,decimal delta,Guid id)
    {
        if(delta==0)return;
        db.Journal.Add(new JournalLine{Account=account,Asset=asset,Delta=delta,ReferenceId=id});
        db.Journal.Add(new JournalLine{Account="system:clearing",Asset=asset,Delta=-delta,ReferenceId=id});
    }
    private async Task<LendingPool> Pool(string asset)
    {
        if(!Assets.Contains(asset))throw new RuleException("Unsupported pool asset.");
        var pool=await db.LendingPools.FindAsync(asset);
        if(pool==null){pool=new LendingPool{Asset=asset};db.LendingPools.Add(pool);await db.SaveChangesAsync();}
        return pool;
    }
    public async Task Accrue(LendingPool pool)
    {
        var now=DateTime.UtcNow;var seconds=(decimal)(now.Ticks-pool.AccruedAt.Ticks)/TimeSpan.TicksPerSecond;
        if(seconds<=0)return;
        var rate=0.02m+0.1m*(pool.Cash+pool.Debt>0?pool.Debt/(pool.Cash+pool.Debt):0);
        foreach(var loan in await db.Loans.Where(x=>x.Asset==pool.Asset&&x.Status=="Active").ToListAsync()) {
            var accrued=(loan.Principal+loan.Interest)*rate*seconds/31536000m+loan.InterestRemainder;
            var interest=Down(accrued);loan.InterestRemainder=decimal.Round(accrued-interest,20,MidpointRounding.ToZero);
            var reserve=interest*0.1m+pool.ReserveRemainder;var bookedReserve=Down(reserve);pool.ReserveRemainder=reserve-bookedReserve;
            loan.Interest+=interest;pool.Debt+=interest;pool.Reserves+=bookedReserve;
            SystemPost("loans:receivable",pool.Asset,interest,loan.Id);SystemPost("earn:interest",pool.Asset,-interest,loan.Id);
        }
        pool.AccruedAt=now;
    }
    public async Task<object> SupplyFunds(Guid user,SupplyRequest r)
    {
        Amount(r.Amount,8,"Supply amount");var pool=await Pool(r.Asset);await Accrue(pool);
        var shares=pool.Shares==0?r.Amount:Down(r.Amount*pool.Shares/(pool.Cash+pool.Debt-pool.Reserves));
        if(shares<=0)throw new RuleException("Supply amount is too small.");
        var supplied=await db.Supplies.FindAsync(user,r.Asset);if(supplied==null){supplied=new Supply{UserId=user,Asset=r.Asset};db.Supplies.Add(supplied);}
        var id=Guid.NewGuid();Move(await Wallet(user,r.Asset),-r.Amount,0,"Earn subscription",id);SystemPost("earn:cash",r.Asset,r.Amount,id);
        pool.Cash+=r.Amount;pool.Shares+=shares;supplied.Shares+=shares;return supplied;
    }
    public async Task<object> Redeem(Guid user,SupplyRequest r)
    {
        Amount(r.Amount,8,"Shares");var pool=await Pool(r.Asset);await Accrue(pool);
        var supply=await db.Supplies.FindAsync(user,r.Asset)??throw new RuleException("No subscription found.");
        if(supply.Shares-supply.QueuedShares<r.Amount)throw new RuleException("Insufficient unqueued shares.");
        supply.QueuedShares+=r.Amount;await db.SaveChangesAsync();await RedeemQueue(pool);return supply;
    }
    public async Task RedeemQueue(LendingPool pool)
    {
        foreach(var supply in await db.Supplies.Where(x=>x.Asset==pool.Asset&&x.QueuedShares>0).OrderBy(x=>x.UserId).ToListAsync()) {
            if(pool.Shares==0)break;
            var unit=(pool.Cash+pool.Debt-pool.Reserves)/pool.Shares;
            if(unit<=0)continue;
            var shares=Math.Min(supply.QueuedShares,Down(Math.Max(0,pool.Cash-pool.Reserves)/unit));
            if(shares<=0)continue;var amount=Down(shares*unit);var id=Guid.NewGuid();
            Move(await Wallet(supply.UserId,pool.Asset),amount,0,"Earn redemption",id);SystemPost("earn:cash",pool.Asset,-amount,id);
            pool.Cash-=amount;pool.Shares-=shares;supply.Shares-=shares;supply.QueuedShares-=shares;
        }
    }
    public async Task<object> Borrow(Guid user,BorrowRequest r)
    {
        Amount(r.Amount,8,"Loan amount");Amount(r.Collateral,8,"Collateral");
        if(!Assets.Contains(r.CollateralAsset)||r.Asset==r.CollateralAsset)throw new RuleException("Select the other asset as collateral.");
        var index=(await FreshIndex()).Price;var pool=await Pool(r.Asset);await Accrue(pool);
        if(pool.Cash-pool.Reserves<r.Amount)throw new RuleException("Insufficient lending-pool liquidity.");
        var debtUsd=r.Amount*(r.Asset=="BTC"?index:1);var collateralUsd=r.Collateral*(r.CollateralAsset=="BTC"?index:1);
        if(debtUsd>collateralUsd*0.6m)throw new RuleException("Initial LTV cannot exceed 60%.");
        var loan=new Loan{UserId=user,Asset=r.Asset,CollateralAsset=r.CollateralAsset,Principal=r.Amount,Collateral=r.Collateral};
        Move(await Wallet(user,r.CollateralAsset),-r.Collateral,0,"Loan collateral",loan.Id);SystemPost("loans:collateral",r.CollateralAsset,r.Collateral,loan.Id);
        Move(await Wallet(user,r.Asset),r.Amount,0,"Loan disbursement",loan.Id);SystemPost("earn:cash",r.Asset,-r.Amount,loan.Id);
        SystemPost("loans:receivable",r.Asset,r.Amount,loan.Id);SystemPost("earn:claim",r.Asset,-r.Amount,loan.Id);
        pool.Cash-=r.Amount;pool.Debt+=r.Amount;db.Loans.Add(loan);return loan;
    }
    public async Task<object> Repay(Guid user,RepayRequest r)
    {
        var loan=await db.Loans.SingleOrDefaultAsync(x=>x.Id==r.LoanId&&x.UserId==user&&x.Status=="Active")??throw new RuleException("Active loan not found.");
        var pool=await Pool(loan.Asset);await Accrue(pool);Amount(r.Amount,8,"Repayment");
        var debt=loan.Principal+loan.Interest;var amount=Math.Min(r.Amount,debt);var collateral=amount==debt?loan.Collateral:Down(loan.Collateral*amount/debt);
        Move(await Wallet(user,loan.Asset),-amount,0,"Loan repayment",loan.Id);SystemPost("earn:cash",loan.Asset,amount,loan.Id);
        Move(await Wallet(user,loan.CollateralAsset),collateral,0,"Collateral release",loan.Id);SystemPost("loans:collateral",loan.CollateralAsset,-collateral,loan.Id);
        SystemPost("loans:receivable",loan.Asset,-amount,loan.Id);SystemPost("earn:claim",loan.Asset,amount,loan.Id);
        var interest=Math.Min(amount,loan.Interest);loan.Interest-=interest;loan.Principal-=amount-interest;loan.Collateral-=collateral;pool.Debt-=amount;pool.Cash+=amount;
        if(loan.Principal+loan.Interest==0){loan.Status="Repaid";loan.InterestRemainder=0;}await RedeemQueue(pool);return loan;
    }
    public async Task<object> AddCollateral(Guid user,RepayRequest r)
    {
        var loan=await db.Loans.SingleOrDefaultAsync(x=>x.Id==r.LoanId&&x.UserId==user&&x.Status=="Active")??throw new RuleException("Active loan not found.");
        Amount(r.Amount,8,"Collateral");Move(await Wallet(user,loan.CollateralAsset),-r.Amount,0,"Additional loan collateral",loan.Id);SystemPost("loans:collateral",loan.CollateralAsset,r.Amount,loan.Id);loan.Collateral+=r.Amount;return loan;
    }
    private async Task<LiquidityPool> Amm()
    {var pool=await db.LiquidityPools.FindAsync(1);if(pool==null){pool=new LiquidityPool();db.LiquidityPools.Add(pool);}return pool;}
    public async Task<object> AddLiquidity(Guid user,LiquidityRequest r)
    {
        Amount(r.Btc,8,"BTC");Amount(r.Usdt,8,"USDT");var pool=await Amm();decimal shares,btc,usd;
        if(pool.Shares==0){btc=r.Btc;usd=r.Usdt;shares=Down(Sqrt(btc*usd));}
        else{shares=Down(Math.Min(r.Btc/pool.Btc,r.Usdt/pool.Usdt)*pool.Shares);btc=decimal.Ceiling(shares*pool.Btc/pool.Shares*100000000m)/100000000m;usd=decimal.Ceiling(shares*pool.Usdt/pool.Shares*100000000m)/100000000m;}
        if(shares<=0||btc<=0||usd<=0)throw new RuleException("Liquidity contribution is too small.");
        var id=Guid.NewGuid();Move(await Wallet(user,"BTC"),-btc,0,"Liquidity provision",id);Move(await Wallet(user,"USDT"),-usd,0,"Liquidity provision",id);
        SystemPost("amm:reserves","BTC",btc,id);SystemPost("amm:reserves","USDT",usd,id);
        pool.Btc+=btc;pool.Usdt+=usd;pool.Shares+=shares;
        var holder=await db.LiquidityShares.FindAsync(user);if(holder==null){holder=new LiquidityShare{UserId=user};db.LiquidityShares.Add(holder);}holder.Shares+=shares;
        return new{btc,usdt=usd,shares};
    }
    public async Task<object> RemoveLiquidity(Guid user,RemoveLiquidityRequest r)
    {
        Amount(r.Shares,8,"LP shares");var pool=await Amm();var holder=await db.LiquidityShares.FindAsync(user)??throw new RuleException("No LP shares.");
        if(r.Shares>holder.Shares)throw new RuleException("Insufficient LP shares.");
        var btc=Down(r.Shares*pool.Btc/pool.Shares);var usd=Down(r.Shares*pool.Usdt/pool.Shares);var id=Guid.NewGuid();
        Move(await Wallet(user,"BTC"),btc,0,"Liquidity withdrawal",id);Move(await Wallet(user,"USDT"),usd,0,"Liquidity withdrawal",id);
        SystemPost("amm:reserves","BTC",-btc,id);SystemPost("amm:reserves","USDT",-usd,id);
        pool.Btc-=btc;pool.Usdt-=usd;pool.Shares-=r.Shares;holder.Shares-=r.Shares;return new{btc,usdt=usd};
    }
    public async Task<object> Swap(Guid user,SwapRequest r)
    {
        Amount(r.Amount,8,"Swap amount");if(!Assets.Contains(r.Asset)||r.MinimumReceived<0||r.Deadline<DateTime.UtcNow||r.Deadline>DateTime.UtcNow.AddHours(1))throw new RuleException("Invalid asset, minimum received, or deadline.");
        var pool=await Amm();var input=r.Asset=="BTC"?pool.Btc:pool.Usdt;var output=r.Asset=="BTC"?pool.Usdt:pool.Btc;
        if(input<=0||output<=0)throw new RuleException("Pool has no liquidity.");
        var received=Down(output*r.Amount*0.997m/(input+r.Amount*0.997m));
        if(received<r.MinimumReceived||received<=0)throw new RuleException("Minimum received would not be met.");
        var other=r.Asset=="BTC"?"USDT":"BTC";var id=Guid.NewGuid();
        Move(await Wallet(user,r.Asset),-r.Amount,0,"AMM swap",id);Move(await Wallet(user,other),received,0,"AMM swap",id);
        SystemPost("amm:reserves",r.Asset,r.Amount,id);SystemPost("amm:reserves",other,-received,id);
        if(r.Asset=="BTC"){pool.Btc+=r.Amount;pool.Usdt-=received;}else{pool.Usdt+=r.Amount;pool.Btc-=received;}
        return new{received,asset=other,fee=r.Amount*0.003m};
    }
    public async Task<object> CreateDca(Guid user,DcaRequest r)
    {
        Amount(r.Amount,6,"Investment");if(r.Hours is not(1 or 24 or 168)||r.Budget<r.Amount)throw new RuleException("Choose hourly/daily/weekly and a budget covering at least one run.");
        var schedule=new DcaSchedule{UserId=user,Amount=r.Amount,Hours=r.Hours,Budget=r.Budget,NextRun=DateTime.UtcNow.AddHours(r.Hours)};db.DcaSchedules.Add(schedule);return await Task.FromResult(schedule);
    }
    public async Task<object> ToggleDca(Guid user,Guid id)
    {var schedule=await db.DcaSchedules.SingleOrDefaultAsync(x=>x.Id==id&&x.UserId==user)??throw new RuleException("Schedule not found.");schedule.Paused=!schedule.Paused;if(!schedule.Paused)schedule.NextRun=DateTime.UtcNow.AddHours(schedule.Hours);return schedule;}
}
