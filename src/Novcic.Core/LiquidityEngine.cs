using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
namespace Novcic.Core;
public record AmmSettings(decimal SpotFee=0.001m,decimal FuturesFee=0.00055m,decimal OptionFee=0.0003m,decimal OptionPremiumCap=0.07m,decimal LpShare=0.8m,int DerivativeLevels=5);
public record QueueDeposit(decimal Btc,decimal Usdt,[property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? SourceAccount=null);
public record QueueWithdrawal(decimal Shares,string Asset);
public partial class TradingService
{
    private List<Order> priorQuotes = [];
    public static readonly Guid AmmAccount=Guid.Parse("00000000-0000-4000-8000-000000000001");
    public static readonly Guid FeeAccount=Guid.Parse("00000000-0000-4000-8000-000000000002");
    public static decimal ExecutionFee(string kind, string side, decimal quantity, decimal price, decimal index, AmmSettings rates) => Down(
        kind=="Spot" ? quantity*(side=="Buy"?1:price)*rates.SpotFee :
        kind=="Option" ? Math.Min(quantity*index*rates.OptionFee,quantity*price*rates.OptionPremiumCap) : quantity*price*rates.FuturesFee);
    private async Task<decimal> ChargeExecutionFee(Guid user, Guid counterparty, Instrument instrument, string side, decimal quantity, decimal price, Guid reference,bool reduceOnly=false)
    {
        if(user==AmmAccount||await db.LiquidityOperations.AnyAsync(x=>x.Id==user)||await db.Loans.AnyAsync(x=>x.Id==user))return 0;
        var rates=settings??new AmmSettings();
        var asset=instrument.Kind=="Spot"&&side=="Buy"?"BTC":"USDT";
        var fee=ExecutionFee(instrument.Kind,side,quantity,price,instrument.Kind=="Option"?(await FreshIndex()).Price:0,rates);
        var lp=counterparty==AmmAccount?Down(fee*rates.LpShare):0;
        await EnsureWallets(FeeAccount);
        var wallet=await Wallet(user,asset);
        var paid=reduceOnly&&instrument.Kind!="Spot"?Math.Min(wallet.Available,fee):fee;
        var paidLp=paid==fee?lp:fee>0?Down(lp*paid/fee):0;
        // Closing cannot require new collateral. Unfunded fees are matched cash
        // liabilities/receivables, not fabricated wallet assets or waived fees.
        if(paid>0)Move(wallet,-paid,0,"Execution fee",reference);
        if(fee>paid)await Cash(user,paid-fee,reference,"Execution fee accrued");
        if(paid>paidLp)Move(await Wallet(FeeAccount,asset),paid-paidLp,0,"Exchange fee income",reference);
        var exchangeClaim=fee-lp-(paid-paidLp);
        if(exchangeClaim>0)await Cash(FeeAccount,exchangeClaim,reference,"Exchange fee income accrued");
        if(lp>0){
            if(paidLp>0)Move(await Wallet(AmmAccount,asset),paidLp,0,"LP fee income",reference);
            if(lp>paidLp)await Cash(AmmAccount,lp-paidLp,reference,"LP fee income accrued");
            var pool=await Amm();if(asset=="BTC")pool.FeesEarnedBtc+=lp;else pool.FeesEarned+=lp;
        }
        return fee;
    }
    public async Task<object> QueueLiquidity(Guid user,QueueDeposit r)
    {
        var source=r.SourceAccount??"Unified Trading";
        if(!Kinds.Contains(source))throw new RuleException("Choose Funding or Unified Trading as the source account.");
        if(r.Btc<0||r.Usdt<0||r.Btc+r.Usdt<=0)throw new RuleException("Enter a positive contribution in either asset.");
        if(r.Btc>0)Amount(r.Btc,8,"BTC");if(r.Usdt>0)Amount(r.Usdt,8,"USDT");
        var op=new LiquidityOperation{UserId=user,SourceAccount=source,Btc=r.Btc,Usdt=r.Usdt,DepositValueUsd=Down(r.Usdt+(r.Btc>0?r.Btc*(await FreshIndex()).Price:0))};
        await EnsureWallets(op.Id);
        foreach(var (asset,amount) in new[]{("BTC",r.Btc),("USDT",r.Usdt)}){
            Move(await Wallet(user,asset,source),-amount,0,"Queued liquidity deposit",op.Id);
            Move(await Wallet(op.Id,asset),amount,0,"Liquidity escrow",op.Id);
        }
        db.LiquidityOperations.Add(op);await db.SaveChangesAsync();if(source=="Unified Trading")await CheckRisk(user);return op;
    }
    public async Task<object> QueueRedemption(Guid user,QueueWithdrawal r)
    {
        Amount(r.Shares,8,"Shares");if(!Assets.Contains(r.Asset))throw new RuleException("Choose BTC or USDT.");
        var holding=await db.LiquidityShares.FindAsync(user)??throw new RuleException("No LP shares.");
        var locked=await db.LiquidityOperations.Where(x=>x.UserId==user&&x.Kind=="Withdrawal"&&x.Status=="Pending").SumAsync(x=>x.Shares);
        if(r.Shares>holding.Shares-locked)throw new RuleException("Insufficient unlocked LP shares.");
        var op=new LiquidityOperation{UserId=user,Kind="Withdrawal",Asset=r.Asset,Shares=r.Shares};db.LiquidityOperations.Add(op);return op;
    }
    public async Task<object> CancelLiquidity(Guid user,Guid id)
    {
        var op=await db.LiquidityOperations.SingleOrDefaultAsync(x=>x.Id==id&&x.UserId==user)??throw new RuleException("Request not found.");
        if(op.Status!="Pending")throw new RuleException("Request is already final.");
        if(op.Kind=="Deposit"){
            await CancelAll(op.Id);await db.SaveChangesAsync();
            foreach(var asset in Assets){var escrow=await Wallet(op.Id,asset);var amount=escrow.Available;Move(escrow,-amount,0,"Escrow return",id);Move(await Wallet(user,asset,op.SourceAccount),amount,0,"Cancelled liquidity deposit",id);}
        }
        op.Status="Cancelled";op.CompletedAt=DateTime.UtcNow;op.Detail="Unprocessed request cancelled; completed conversions remain final.";return op;
    }
    public async Task<decimal> PoolValue(LiquidityPool pool,decimal index)
    {
        if(pool.MakerId==null)return pool.Btc*index+pool.Usdt;
        var btc=await Wallet(AmmAccount,"BTC");var usd=await Wallet(AmmAccount,"USDT");
        // Nullable cash must not erase the wallet value.
        decimal nav=(btc.Available+btc.Locked)*index+usd.Available+usd.Locked+((await db.MarginBalances.FindAsync(AmmAccount))?.Cash??0);
        foreach(var pos in await db.Positions.Where(x=>x.UserId==AmmAccount&&x.Quantity!=0).ToListAsync()){
            var instrument=(await db.Instruments.FindAsync(pos.InstrumentId))!;
            var mark=await Mark(instrument,index);nav+=instrument.Kind=="Option"?pos.Quantity*mark:pos.Quantity*mark-pos.Cost;
        }
        return nav;
    }
    public async Task ProcessLiquidity()
    {
        var pool=await Amm();await EnsureWallets(AmmAccount);await EnsureWallets(FeeAccount);
        priorQuotes=await db.Orders.Where(x=>x.UserId==AmmAccount&&(x.Status=="Open"||x.Status=="PartiallyFilled")).OrderBy(x=>x.Sequence).ToListAsync();
        await CancelAll(AmmAccount);await db.SaveChangesAsync();
        if(pool.MakerId==null){
            var id=Guid.NewGuid();Move(await Wallet(AmmAccount,"BTC"),pool.Btc,0,"AMM reserve migration",id);Move(await Wallet(AmmAccount,"USDT"),pool.Usdt,0,"AMM reserve migration",id);
            SystemPost("amm:reserves","BTC",-pool.Btc,id);SystemPost("amm:reserves","USDT",-pool.Usdt,id);pool.Btc=0;pool.Usdt=0;pool.MakerId=AmmAccount;
        }
        var sample=await db.IndexSamples.OrderByDescending(x=>x.Id).FirstOrDefaultAsync();
        if(sample==null||DateTime.UtcNow-sample.Time>TimeSpan.FromSeconds(5))return;
        var index=sample.Price;
        foreach(var position in await db.Positions.Where(x=>x.UserId==AmmAccount&&x.Quantity!=0).ToListAsync()){
            var i=(await db.Instruments.FindAsync(position.InstrumentId))!;
            if(i.Kind=="Option"&&await QuoteMark(i,index)==null)return;
        }
        // Positive cash claims remain claims until funded; never mint liquidity from P&L.
        var cash=await db.MarginBalances.FindAsync(AmmAccount);
        if(cash?.Cash>0&&(await Clearing()).Cash>0)await SettleCash(AmmAccount);
        foreach(var op in await db.LiquidityOperations.Where(x=>x.Status=="Pending"&&x.Kind=="Deposit").OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).ToListAsync()){
            await CancelAll(op.Id);await db.SaveChangesAsync();
            var btc=await Wallet(op.Id,"BTC");var usd=await Wallet(op.Id,"USDT");var total=btc.Available*index+usd.Available;
            var difference=btc.Available*index-usd.Available;
            if(Math.Abs(difference)>Math.Max(index*0.000002m,total*0.0001m)){
                var side=difference>0?"Sell":"Buy";var qty=decimal.Floor(Math.Abs(difference)/2/index*1000000m)/1000000m;
                await MakerOrder(op.Id,"BTCUSDT",side,qty,index,false);op.Detail="Waiting for counterparties to balance the contribution 50:50";continue;
            }
            var nav=await PoolValue(pool,index);if(pool.Shares>0&&nav<=0){op.Detail="Pool valuation unavailable";continue;}
            var shares=Down(pool.Shares==0?total:total*pool.Shares/nav);if(shares<=0){op.Detail="Contribution below share precision";continue;}
            foreach(var asset in Assets){var from=await Wallet(op.Id,asset);var amount=from.Available;Move(from,-amount,0,"Accepted liquidity deposit",op.Id);Move(await Wallet(AmmAccount,asset),amount,0,"Pool contribution",op.Id);}
            var holder=await db.LiquidityShares.FindAsync(op.UserId);if(holder==null){holder=new LiquidityShare{UserId=op.UserId};db.LiquidityShares.Add(holder);}holder.Shares+=shares;pool.Shares+=shares;op.Shares=shares;op.Status="Completed";op.CompletedAt=DateTime.UtcNow;op.Detail="Contribution balanced and activated";
            await db.SaveChangesAsync();
        }
        var withdrawal=await db.LiquidityOperations.Where(x=>x.Status=="Pending"&&x.Kind=="Withdrawal").OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).FirstOrDefaultAsync();
        if(withdrawal!=null){
            var positions=await db.Positions.Where(x=>x.UserId==AmmAccount&&x.Quantity!=0).ToListAsync();
            foreach(var pos in positions){var i=(await db.Instruments.FindAsync(pos.InstrumentId))!;var mark=await QuoteMark(i,index);if(mark!=null)await MakerOrder(AmmAccount,i.Id,pos.Quantity>0?"Sell":"Buy",Math.Abs(pos.Quantity),mark.Value,true);}
            var nav=await PoolValue(pool,index);var value=pool.Shares>0?nav*withdrawal.Shares/pool.Shares:0;
            var amount=Down(withdrawal.Asset=="BTC"?value/index:value);var wallet=await Wallet(AmmAccount,withdrawal.Asset);
            var risk=await Risk(AmmAccount);
            var remainingBuffer=Math.Max(0,nav-value)*0.2m;
            var redeemable=withdrawal.Asset=="BTC"?Math.Min(risk.TransferableBtc,Math.Max(0,(risk.Equity-risk.InitialMargin-remainingBuffer)/index/0.9m)):Math.Min(risk.TransferableUsdt,Math.Max(0,risk.Equity-risk.InitialMargin-remainingBuffer));
            // A final withdrawal may leave less BTC than the market quantity step.
            // Return that dust in kind instead of stranding the last LP's shares.
            decimal dust=0;
            if(withdrawal.Asset=="USDT"&&withdrawal.Shares==pool.Shares&&positions.Count==0){
                var btcDust=await Wallet(AmmAccount,"BTC");
                if(btcDust.Locked==0&&btcDust.Available>0&&btcDust.Available<0.000001m){dust=btcDust.Available;amount=Down(value-dust*index);}
            }
            if(risk.PriceFresh&&!risk.Restricted&&amount>0&&wallet.Available>=amount&&redeemable>=amount){
                if(dust>0){Move(await Wallet(AmmAccount,"BTC"),-dust,0,"LP dust redemption",withdrawal.Id);Move(await Wallet(withdrawal.UserId,"BTC"),dust,0,"Liquidity withdrawal dust",withdrawal.Id);}
                Move(wallet,-amount,0,"LP redemption",withdrawal.Id);Move(await Wallet(withdrawal.UserId,withdrawal.Asset),amount,0,"Liquidity withdrawal",withdrawal.Id);
                withdrawal.WithdrawalValueUsd=Down((withdrawal.Asset=="BTC"?amount*index:amount)+dust*index);
                var holding=(await db.LiquidityShares.FindAsync(withdrawal.UserId))!;holding.Shares-=withdrawal.Shares;pool.Shares-=withdrawal.Shares;withdrawal.Received=amount;withdrawal.Status="Completed";withdrawal.CompletedAt=DateTime.UtcNow;withdrawal.Detail=dust>0?"Redeemed at execution value; sub-step BTC remainder returned in BTC":"Redeemed at execution value";
            }else{
                withdrawal.Detail="Waiting for counterparty fills and released collateral";
                var side=withdrawal.Asset=="USDT"?"Sell":"Buy";var qty=withdrawal.Asset=="USDT"?Math.Max(0,amount-wallet.Available)/index:Math.Max(0,amount-wallet.Available);
                await MakerOrder(AmmAccount,"BTCUSDT",side,qty,index,false);await db.SaveChangesAsync();return;
            }
        }
        await QuotePool(pool,index);
    }
    private async Task<decimal?> QuoteMark(Instrument i,decimal index)
    {
        if(i.Expiry<=DateTime.UtcNow)return null;
        if(i.Kind!="Option")return index;
        var iv=OptionPricing.Volatility(await db.OptionReferences.AsNoTracking().ToListAsync(),i.Strike!.Value,i.Expiry!.Value,DateTime.UtcNow);
        return iv==null?null:OptionPricing.Value(index,i.Strike.Value,(decimal)(i.Expiry.Value-DateTime.UtcNow).Ticks/TimeSpan.TicksPerDay/365,iv.Value,i.OptionSide=="Call").Price;
    }
    private async Task MakerOrder(Guid actor,string instrument,string side,decimal quantity,decimal price,bool reduce)
    {
        price=side=="Buy"?decimal.Floor(price*100)/100:decimal.Ceiling(price*100)/100;
        quantity=decimal.Floor(quantity*1000000m)/1000000m;if(price<=0||quantity<=0)return;
        // Post-only: pool orders never cross or manufacture trades.
        var crossing=await db.Orders.AnyAsync(x=>x.InstrumentId==instrument&&x.Side!=side&&(x.Status=="Open"||x.Status=="PartiallyFilled")&&(side=="Buy"?x.Price<=price:x.Price>=price));if(crossing)return;
        var asset=side=="Sell"?"BTC":"USDT";
        if(instrument=="BTCUSDT"){
            var available=(await Wallet(actor,asset)).Available;if(actor==AmmAccount){var risk=await Risk(actor);available=Math.Min(available,asset=="BTC"?risk.TransferableBtc:risk.TransferableUsdt);}
            quantity=Math.Min(quantity,decimal.Floor((side=="Buy"?available/price:available)*1000000m)/1000000m);if(quantity<=0)return;
        }
        var previous=actor==AmmAccount?priorQuotes.FirstOrDefault(x=>x.Status=="Cancelled"&&x.InstrumentId==instrument&&x.Side==side&&x.Price==price&&x.Remaining==quantity&&x.ReduceOnly==reduce):null;
        if(previous!=null){
            // Cancellation and restoration occur in the same locked transaction. Keep time priority.
            if(instrument=="BTCUSDT"){
                var reserved=side=="Buy"?quantity*price:quantity;
                Move(await Wallet(actor,asset),-reserved,reserved,"AMM quote reserve",previous.Id);previous.Reserved=reserved;
            }
            previous.Status=previous.Quantity==previous.Remaining?"Open":"PartiallyFilled";
            previous.CancelReason=null;previous.QuoteValidatedAt=DateTime.UtcNow;
            await db.SaveChangesAsync();await CheckRisk(actor);return;
        }
        var placed=(Order)await Place(actor,new PlaceOrder(side,"Limit",quantity,price,instrument,1,reduce));
        placed.QuoteValidatedAt=DateTime.UtcNow;await db.SaveChangesAsync();
    }
    private async Task QuotePool(LiquidityPool pool,decimal index)
    {
        if(pool.Shares<=0)return;
        var nav=await PoolValue(pool,index);if(nav<=0)return;
        var btc=(await Wallet(AmmAccount,"BTC")).Available;var usd=(await Wallet(AmmAccount,"USDT")).Available;
        if(btc>0&&usd>0){
            // Secant prices of x*y=k: each discrete order covers its own curve segment.
            var step=decimal.Floor(Math.Min(btc*0.001m,usd/index*0.001m)*1000000m)/1000000m;
            for(var level=1;level<=10&&step>0;level++){
                var before=(level-1)*step;var after=level*step;
                await MakerOrder(AmmAccount,"BTCUSDT","Buy",step,btc*usd/((btc+before)*(btc+after)),false);
                if(after<btc)await MakerOrder(AmmAccount,"BTCUSDT","Sell",step,btc*usd/((btc-before)*(btc-after)),false);
            }
        }
        var instruments=await db.Instruments.Where(i=>i.Kind!="Spot"&&i.Status=="Trading"&&(i.Expiry==null||i.Expiry>DateTime.UtcNow)).ToListAsync();
        var budget=nav*0.2m/Math.Max(1,instruments.Count)/2;
        foreach(var i in instruments){
            var mark=await QuoteMark(i,index);if(mark==null||mark<=0)continue;
            var position=await db.Positions.FindAsync(AmmAccount,i.Id);var inventory=position?.Quantity??0;
            // A virtual constant-product inventory curve, bounded by real collateral.
            var virtualBase=Math.Max(nav/index,0.000001m);var center=mark.Value*virtualBase*virtualBase/((virtualBase+Math.Clamp(inventory,-virtualBase/2,virtualBase/2))*(virtualBase+Math.Clamp(inventory,-virtualBase/2,virtualBase/2)));
            foreach(var side in new[]{"Buy","Sell"}){
                var risk=await Risk(AmmAccount);var free=Math.Max(0,risk.Equity-risk.InitialMargin-nav*0.2m);var amount=Math.Min(budget,free);
                var qty=amount/Math.Max(index,mark.Value);if(i.Kind=="Option"&&side=="Sell")qty=Math.Min(qty,i.OptionSide=="Call"?risk.TransferableBtc:risk.TransferableUsdt/i.Strike!.Value);
                var levels=Math.Clamp((settings??new AmmSettings()).DerivativeLevels,1,20);
                var step=decimal.Floor(qty/levels*1000000m)/1000000m;if(step<=0)continue;
                var quotes=new Dictionary<decimal,decimal>();
                for(var level=1;level<=levels;level++){
                    var before=(level-1)*step;var after=level*step;if(side=="Sell"&&after>=virtualBase)break;
                    // Each level consumes a separate segment of the same curve and total budget.
                    var quote=side=="Buy"?center*virtualBase*virtualBase/((virtualBase+before)*(virtualBase+after))*0.999m:center*virtualBase*virtualBase/((virtualBase-before)*(virtualBase-after))*1.001m;
                    if(i.Kind!="Option")quote=side=="Buy"?Math.Min(quote,index*1.09m):Math.Max(quote,index*0.91m);
                    quote=side=="Buy"?decimal.Floor(quote*100)/100:decimal.Ceiling(quote*100)/100;
                    quotes[quote]=quotes.GetValueOrDefault(quote)+step;
                }
                foreach(var quote in quotes)await MakerOrder(AmmAccount,i.Id,side,quote.Value,quote.Key,false);
            }
        }
    }
}
