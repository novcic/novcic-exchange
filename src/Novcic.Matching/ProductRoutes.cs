using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Matching;

public static class ProductRoutes
{
    public static void MapProducts(this WebApplication app)
    {
        app.MapGet("/products", async (ExchangeDb db) => {
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var index=await db.IndexSamples.AsNoTracking().OrderByDescending(x=>x.Id).FirstOrDefaultAsync();
            var instruments=await db.Instruments.AsNoTracking().OrderBy(x=>x.Expiry).ThenBy(x=>x.Id).ToListAsync();
            var pools=await db.LendingPools.AsNoTracking().ToListAsync();
            var amm=await db.LiquidityPools.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==1)??new LiquidityPool();
            return Results.Ok(new {index,priceFresh=index!=null&&DateTime.UtcNow-index.Time<TimeSpan.FromSeconds(5),instruments,pools,amm});
        });
        app.MapGet("/portfolio/{user:guid}", async (Guid user,ExchangeDb db,TradingService svc) => {
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var risk=await svc.Risk(user);
            var positions=await db.Positions.AsNoTracking().Where(x=>x.UserId==user).ToListAsync();
            var loans=await db.Loans.AsNoTracking().Where(x=>x.UserId==user).OrderByDescending(x=>x.CreatedAt).ToListAsync();
            var supplies=await db.Supplies.AsNoTracking().Where(x=>x.UserId==user).ToListAsync();
            var liquidity=await db.LiquidityShares.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==user);
            var schedules=await db.DcaSchedules.AsNoTracking().Where(x=>x.UserId==user).OrderBy(x=>x.NextRun).ToListAsync();
            var events=await db.RiskEvents.AsNoTracking().Where(x=>x.UserId==user).OrderByDescending(x=>x.Id).Take(100).ToListAsync();
            return Results.Ok(new {risk,positions,loans,supplies,liquidity,schedules,events});
        });
        Command<SupplyRequest>(app,"supply",(s,u,r)=>s.SupplyFunds(u,r));
        Command<SupplyRequest>(app,"redeem",(s,u,r)=>s.Redeem(u,r));
        Command<BorrowRequest>(app,"borrow",(s,u,r)=>s.Borrow(u,r));
        Command<RepayRequest>(app,"repay",(s,u,r)=>s.Repay(u,r));
        Command<RepayRequest>(app,"add-collateral",(s,u,r)=>s.AddCollateral(u,r));
        Command<LiquidityRequest>(app,"add-liquidity",(s,u,r)=>s.AddLiquidity(u,r));
        Command<RemoveLiquidityRequest>(app,"remove-liquidity",(s,u,r)=>s.RemoveLiquidity(u,r));
        Command<SwapRequest>(app,"swap",(s,u,r)=>s.Swap(u,r));
        Command<DcaRequest>(app,"dca",(s,u,r)=>s.CreateDca(u,r));
        Command<IdRequest>(app,"toggle-dca",(s,u,r)=>s.ToggleDca(u,r.Id));
        Command<EmptyRequest>(app,"settle-cash",(s,u,_)=>s.SettleCash(u));
        Command<EmptyRequest>(app,"cancel-all",async(s,u,_)=>await s.CancelAll(u));
    }
    private static void Command<T>(WebApplication app,string name,Func<TradingService,Guid,T,Task<object>> action) where T:class =>
        app.MapPost("/"+name+"/{user:guid}",async(Guid user,T body,HttpRequest request,TradingService svc)=>
            Results.Content(await svc.Command(user,request.Headers["Idempotency-Key"].ToString(),name,body,()=>action(svc,user,body)),"application/json"));
}
public record IdRequest(Guid Id);
public record EmptyRequest;
