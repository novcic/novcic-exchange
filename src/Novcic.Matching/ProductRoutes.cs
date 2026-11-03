using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Matching;

public static class ProductRoutes
{
    public static void MapProducts(this WebApplication app)
    {
        app.MapGet("/products", async (ExchangeDb db, TradingService svc) => {
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var index=await db.IndexSamples.AsNoTracking().OrderByDescending(x=>x.Id).FirstOrDefaultAsync();
            var instruments=await db.Instruments.AsNoTracking().OrderBy(x=>x.Expiry).ThenBy(x=>x.Id).ToListAsync();
            var day=DateTime.UtcNow.AddDays(-1);
            var recent=await db.Trades.AsNoTracking().Where(x=>x.CreatedAt>=day).OrderBy(x=>x.Sequence).ToListAsync();
            var open=await db.Orders.AsNoTracking().Where(x=>x.Status=="Open"||x.Status=="PartiallyFilled").ToListAsync();
            var tickers=instruments.Select(i=>{
                var trades=recent.Where(t=>t.InstrumentId==i.Id).ToList();
                var bids=PublicDepth.Build(open.Where(o=>o.InstrumentId==i.Id&&o.Side=="Buy").Select(o=>(o.Price!.Value,o.Remaining)),true);
                var asks=PublicDepth.Build(open.Where(o=>o.InstrumentId==i.Id&&o.Side=="Sell").Select(o=>(o.Price!.Value,o.Remaining)),false);
                return new {instrumentId=i.Id,lastPrice=trades.LastOrDefault()?.Price,change=trades.Count>0?(trades[^1].Price/trades[0].Price-1)*100:(decimal?)null,volume=trades.Sum(t=>t.Quantity),bid=bids.FirstOrDefault(),ask=asks.FirstOrDefault()};
            }).ToList();
            var pools=await db.LendingPools.AsNoTracking().ToListAsync();
            var amm=await db.LiquidityPools.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==1)??new LiquidityPool();
            if(amm.MakerId.HasValue){var wallets=await db.Wallets.AsNoTracking().Where(w=>w.UserId==amm.MakerId&&w.Kind=="Unified Trading").ToListAsync();amm.Btc=wallets.Where(w=>w.Asset=="BTC").Sum(w=>w.Available+w.Locked);amm.Usdt=wallets.Where(w=>w.Asset=="USDT").Sum(w=>w.Available+w.Locked);}
            var references=await db.OptionReferences.AsNoTracking().ToListAsync();
            var optionAnalytics=instruments.Where(i=>i.Kind=="Option"&&i.Status=="Trading"&&i.Expiry>DateTime.UtcNow).Select(i=>{var iv=OptionPricing.Volatility(references,i.Strike!.Value,i.Expiry!.Value,DateTime.UtcNow);var v=iv.HasValue&&index!=null?OptionPricing.Value(index.Price,i.Strike.Value,(decimal)(i.Expiry.Value-DateTime.UtcNow).Ticks/TimeSpan.TicksPerDay/365,iv.Value,i.OptionSide=="Call"):((decimal Price,decimal Delta)?)null;return new{instrumentId=i.Id,iv,mark=v?.Price,delta=v?.Delta};}).ToList();
            return Results.Ok(new {optionAnalytics,index,priceFresh=index!=null&&DateTime.UtcNow-index.Time<TimeSpan.FromSeconds(5),instruments,tickers,pools,amm=new{btc=amm.Btc,usdt=amm.Usdt,shares=amm.Shares,feesEarned=amm.FeesEarned,feesEarnedBtc=amm.FeesEarnedBtc,nav=index==null?(decimal?)null:await svc.PoolValue(amm,index.Price)}});
        });
        app.MapGet("/portfolio/{user:guid}", async (Guid user,ExchangeDb db,TradingService svc) => {
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var risk=await svc.Risk(user);
            var positions=await svc.PositionViews(user,risk);
            var loans=await db.Loans.AsNoTracking().Where(x=>x.UserId==user).OrderByDescending(x=>x.CreatedAt).ToListAsync();
            var supplies=await db.Supplies.AsNoTracking().Where(x=>x.UserId==user).ToListAsync();
            var liquidity=await db.LiquidityShares.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==user);
            var schedules=await db.DcaSchedules.AsNoTracking().Where(x=>x.UserId==user).OrderBy(x=>x.NextRun).ToListAsync();
            var events=await db.RiskEvents.AsNoTracking().Where(x=>x.UserId==user).OrderByDescending(x=>x.Id).Take(100).ToListAsync();
            var liquidityRequests=await db.LiquidityOperations.AsNoTracking().Where(x=>x.UserId==user).OrderByDescending(x=>x.CreatedAt).Take(100).ToListAsync();
            return Results.Ok(new {leverage=await svc.AccountLeverage(user),risk,positions,loans,supplies,liquidity,liquidityRequests,schedules,events});
        });
        Command<ProtectionRequest>(app,"position-protection",(s,u,r)=>s.SetProtection(u,r));
        Command<StopClosingRequest>(app,"stop-closing",(s,u,r)=>s.StopClosing(u,r));
        Command<AmendOrderRequest>(app,"amend-order",(s,u,r)=>s.AmendOrder(u,r));
        Command<LeverageRequest>(app,"leverage",(s,u,r)=>s.SetLeverage(u,r));
        Command<SupplyRequest>(app,"supply",(s,u,r)=>s.SupplyFunds(u,r));
        Command<SupplyRequest>(app,"redeem",(s,u,r)=>s.Redeem(u,r));
        Command<BorrowRequest>(app,"borrow",(s,u,r)=>s.Borrow(u,r));
        Command<RepayRequest>(app,"repay",(s,u,r)=>s.Repay(u,r));
        Command<RepayRequest>(app,"add-collateral",(s,u,r)=>s.AddCollateral(u,r));
        Command<QueueDeposit>(app,"liquidity-deposit",(s,u,r)=>s.QueueLiquidity(u,r));
        Command<QueueWithdrawal>(app,"liquidity-withdraw",(s,u,r)=>s.QueueRedemption(u,r));
        Command<IdRequest>(app,"liquidity-cancel",(s,u,r)=>s.CancelLiquidity(u,r.Id));
        foreach(var retired in new[]{"add-liquidity","remove-liquidity","swap"})app.MapPost("/"+retired+"/{user:guid}",()=>Results.Json(new{error="Use queued liquidity requests or the spot trading terminal."},statusCode:410));
        Command<DcaRequest>(app,"dca",(s,u,r)=>s.CreateDca(u,r));
        Command<IdRequest>(app,"toggle-dca",(s,u,r)=>s.ToggleDca(u,r.Id));
        Command<EmptyRequest>(app,"settle-cash",(s,u,_)=>s.SettleCash(u));
        app.MapPost("/cancel-all/{user:guid}",async(Guid user,HttpRequest req,TradingService svc)=>{
            var products=req.Headers.TryGetValue("X-Key-Products",out var header)?header.ToString().Split(','):null;
            return Results.Content(await svc.Command(user,req.Headers["Idempotency-Key"].ToString(),"cancel-all",new{products},()=>svc.CancelAll(user,products)),"application/json");
        });
    }
    private static void Command<T>(WebApplication app,string name,Func<TradingService,Guid,T,Task<object>> action) where T:class =>
        app.MapPost("/"+name+"/{user:guid}",async(Guid user,T body,HttpRequest request,TradingService svc)=>
            Results.Content(await svc.Command(user,request.Headers["Idempotency-Key"].ToString(),name,body,()=>action(svc,user,body)),"application/json"));
}
public record IdRequest(Guid Id);
public record EmptyRequest;
