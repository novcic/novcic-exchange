using Microsoft.EntityFrameworkCore;
namespace Novcic.Core;

public partial class TradingService
{
    public async Task LiquidateLoan(Guid id)
    {
        var loan=await db.Loans.FindAsync(id);
        if(loan==null||loan.Status!="Active")return;
        var index=(await FreshIndex()).Price;
        var lending=await Pool(loan.Asset);await Accrue(lending);
        var debt=loan.Principal+loan.Interest;
        var debtUsd=debt*(loan.Asset=="BTC"?index:1m);
        var collateralUsd=loan.Collateral*(loan.CollateralAsset=="BTC"?index:1m);
        if(debtUsd<collateralUsd*0.8m)return;
        var pool=await Amm();
        var input=loan.CollateralAsset=="BTC"?pool.Btc:pool.Usdt;
        var output=loan.Asset=="BTC"?pool.Btc:pool.Usdt;
        if(input<=0||output<=0)return;
        var required=debt<output?decimal.Ceiling(input*debt/(output-debt)/0.997m*100000000m)/100000000m:loan.Collateral;
        var sold=Math.Min(required,loan.Collateral);
        var received=Down(output*sold*0.997m/(input+sold*0.997m));
        var receivedUsd=received*(loan.Asset=="BTC"?index:1m);
        var soldUsd=sold*(loan.CollateralAsset=="BTC"?index:1m);
        if(received<=0||receivedUsd<soldUsd*0.9m)return;
        var repaid=Math.Min(received,debt);
        if(loan.CollateralAsset=="BTC"){pool.Btc+=sold;pool.Usdt-=received;}else{pool.Usdt+=sold;pool.Btc-=received;}
        loan.Collateral-=sold;
        SystemPost("loans:collateral",loan.CollateralAsset,-sold,id);
        SystemPost("amm:reserves",loan.CollateralAsset,sold,id);
        SystemPost("amm:reserves",loan.Asset,-received,id);
        SystemPost("earn:cash",loan.Asset,repaid,id);
        SystemPost("loans:receivable",loan.Asset,-repaid,id);
        SystemPost("earn:claim",loan.Asset,repaid,id);
        lending.Cash+=repaid;lending.Debt-=repaid;
        var interest=Math.Min(repaid,loan.Interest);loan.Interest-=interest;loan.Principal-=repaid-interest;
        if(received>repaid)Move(await Wallet(loan.UserId,loan.Asset),received-repaid,0,"Liquidation surplus",id);
        if(loan.Principal+loan.Interest==0){
            Move(await Wallet(loan.UserId,loan.CollateralAsset),loan.Collateral,0,"Liquidation collateral return",id);
            SystemPost("loans:collateral",loan.CollateralAsset,-loan.Collateral,id);
            loan.Collateral=0;loan.Status="Liquidated";loan.InterestRemainder=0;
        }else if(loan.Collateral==0){
            loan.BadDebt=loan.Principal+loan.Interest;lending.Debt-=loan.BadDebt;
            lending.Reserves-=Math.Min(lending.Reserves,loan.BadDebt);
            SystemPost("loans:receivable",loan.Asset,-loan.BadDebt,id);
            SystemPost("earn:credit-loss",loan.Asset,loan.BadDebt,id);
            loan.Status="Defaulted";loan.Principal=0;loan.Interest=0;loan.InterestRemainder=0;
        }
        db.RiskEvents.Add(new RiskEvent{UserId=loan.UserId,Type="Loan liquidation",Detail=$"Sold {sold} {loan.CollateralAsset}; repaid {repaid} {loan.Asset}. Status: {loan.Status}."});
        await RedeemQueue(lending);
    }
}
