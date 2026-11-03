using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;
using Novcic.Matching;

if (args.Contains("--healthcheck"))
{
    try { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) }; Environment.ExitCode = (await client.GetAsync("http://localhost:8080/health")).IsSuccessStatusCode ? 0 : 1; }
    catch { Environment.ExitCode = 1; }
    return;
}
var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.NumberHandling = Wire.Json.NumberHandling);
builder.Services.AddDbContext<ExchangeDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Exchange") ?? throw new InvalidOperationException("Database connection is required.")));
var fees=new AmmSettings(builder.Configuration.GetValue("Amm:SpotFee",0.001m),builder.Configuration.GetValue("Amm:FuturesFee",0.00055m),builder.Configuration.GetValue("Amm:OptionFee",0.0003m),builder.Configuration.GetValue("Amm:OptionPremiumCap",0.07m),builder.Configuration.GetValue("Amm:LpShare",0.8m),builder.Configuration.GetValue("Amm:DerivativeLevels",5));
if(new[]{fees.SpotFee,fees.FuturesFee,fees.OptionFee,fees.OptionPremiumCap,fees.LpShare}.Any(x=>x<0||x>1))throw new InvalidOperationException("AMM fee settings must be between zero and one.");
if(fees.DerivativeLevels<1||fees.DerivativeLevels>20)throw new InvalidOperationException("Derivative quote levels must be between 1 and 20.");
builder.Services.AddSingleton(fees);
builder.Services.AddScoped<TradingService>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<IndexWorker>();
builder.Services.AddHostedService<OptionReferenceWorker>();
builder.Services.AddHostedService<LifecycleWorker>();
var key = builder.Configuration["InternalKey"] ?? throw new InvalidOperationException("InternalKey is required.");
var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<ExchangeDb>().Database.MigrateAsync();
app.Use(async (context, next) => {
    if (context.Request.Path == "/health") { await next(); return; }
    var supplied = context.Request.Headers["X-Internal-Key"].ToString();
    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
    { context.Response.StatusCode = 401; return; }
    try { await next(); }
    catch (RuleException ex) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
});
app.MapGet("/health", async (ExchangeDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/config", () => new { symbol="BTC/USDT", quantityStep="0.000001", priceTick="0.01", fees="0", feeModel="counterparty-dependent", userToUserFee="0", amm=new { fees.SpotFee, fees.FuturesFee, fees.OptionFee, fees.OptionPremiumCap, fees.LpShare, fees.DerivativeLevels } });
app.MapGet("/revision", async (ExchangeDb db) => new { revision = await db.Revisions.Where(x=>x.Id==1).Select(x=>x.Revision).SingleAsync() });
app.MapGet("/balances/{user:guid}", (Guid user, TradingService svc) => svc.Balances(user));
app.MapGet("/ledger/{user:guid}", async (Guid user, ExchangeDb db) => await db.Ledger.AsNoTracking().Where(x => x.UserId == user).OrderByDescending(x => x.Id).Take(100).ToListAsync());
app.MapGet("/orders/{user:guid}", async (Guid user, ExchangeDb db) => await db.Orders.AsNoTracking().Where(x => x.UserId == user).OrderByDescending(x => x.Sequence).Take(200).ToListAsync());
app.MapGet("/trades/{user:guid}", async (Guid user, ExchangeDb db) => await db.Trades.AsNoTracking().Where(x => x.BuyerId == user || x.SellerId == user).OrderByDescending(x => x.Sequence).Take(200).ToListAsync());
app.MapGet("/market", async (string? instrument,ExchangeDb db) => {
    var symbol=instrument??"BTCUSDT";
    await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
    var orders = await db.Orders.AsNoTracking().Where(x => x.InstrumentId==symbol && x.Remaining > 0 && (x.Status == "Open" || x.Status == "PartiallyFilled")).OrderBy(x => x.Sequence)
        .Select(x => new { x.Id, x.Sequence, x.Side, x.Price, Quantity = x.Remaining, x.CreatedAt }).ToListAsync();
    var recent = await db.Trades.AsNoTracking().Where(x=>x.InstrumentId==symbol).OrderByDescending(x => x.Sequence).Take(100).ToListAsync();
    var day = DateTime.UtcNow.AddHours(-24);
    var daily = await db.Trades.AsNoTracking().Where(x => x.InstrumentId==symbol && x.CreatedAt >= day).OrderBy(x => x.Sequence).ToListAsync();
    var revision=await db.Revisions.Where(x=>x.Id==1).Select(x=>x.Revision).SingleAsync();
    await snapshot.CommitAsync();
    return Results.Ok(new {
        symbol, revision, quantityAsset = "BTC",
        bids = PublicDepth.Build(orders.Where(x => x.Side == "Buy").Select(x => (x.Price!.Value, x.Quantity)), true),
        asks = PublicDepth.Build(orders.Where(x => x.Side == "Sell").Select(x => (x.Price!.Value, x.Quantity)), false),
        trades = recent.Select(x => new { x.Id, x.Price, x.Quantity, x.TakerSide, x.CreatedAt }),
        lastPrice = recent.FirstOrDefault()?.Price, high = daily.Select(x => (decimal?)x.Price).Max(), low = daily.Select(x => (decimal?)x.Price).Min(),
        volume = daily.Sum(x => x.Quantity), change = daily.Count > 0 ? (recent[0].Price - daily[0].Price) / daily[0].Price * 100 : (decimal?)null,
        serverTime = DateTime.UtcNow
    });
});
app.MapGet("/candles", async (string? interval,string? instrument, ExchangeDb db) => {
    var symbol=instrument??"BTCUSDT";
    if (!Candles.Intervals.TryGetValue(interval ?? "1m", out var seconds)) return Results.BadRequest(new { error = "Unsupported candle interval." });
    var since = DateTime.UtcNow.AddSeconds(-seconds * 501L);
    var trades = await db.Trades.AsNoTracking().Where(x => x.InstrumentId==symbol && x.CreatedAt >= since).OrderBy(x => x.Sequence).ToListAsync();
    var previous = await db.Trades.AsNoTracking().Where(x => x.InstrumentId==symbol && x.CreatedAt < since).OrderByDescending(x => x.Sequence).FirstOrDefaultAsync();
    if (previous != null) trades.Insert(0, previous);
    return Results.Ok(Candles.Build(trades, seconds, DateTime.UtcNow));
});
app.MapPost("/faucet/{user:guid}", async (Guid user, HttpRequest req, TradingService svc, IConfiguration config) => {
    if (!config.GetValue<bool>("FaucetEnabled")) return Results.NotFound();
    return Results.Content(await svc.Command(user, req.Headers["Idempotency-Key"].ToString(), "faucet", new { }, () => svc.Faucet(user)), "application/json");
});
app.MapPost("/transfer/{user:guid}", async (Guid user, TransferRequest body, HttpRequest req, TradingService svc) =>
    Results.Content(await svc.Command(user, req.Headers["Idempotency-Key"].ToString(), "transfer", body, () => svc.Transfer(user, body)), "application/json"));
app.MapPost("/orders/{user:guid}", async (Guid user, PlaceOrder body, HttpRequest req, TradingService svc) =>
    Results.Content(await svc.Command(user, req.Headers["Idempotency-Key"].ToString(), "place", body, () => svc.Place(user, body)), "application/json"));
app.MapPost("/cancel/{user:guid}/{id:guid}", async (Guid user, Guid id, HttpRequest req, TradingService svc) =>
    Results.Content(await svc.Command(user, req.Headers["Idempotency-Key"].ToString(), "cancel", new { id }, () => svc.Cancel(user, id)), "application/json"));
app.MapPost("/deposit-credit/{user:guid}/{id:guid}", async (Guid user, Guid id, HttpRequest req, TradingService svc) =>
    Results.Content(await svc.Command(user, req.Headers["Idempotency-Key"].ToString(), "deposit-credit", new { id }, () => svc.CreditDeposit(user, id)), "application/json"));
app.MapPost("/withdraw/{user:guid}", async (Guid user, WithdrawalRequest body, HttpRequest req, TradingService svc, IConfiguration config) =>
    Results.Content(await svc.Command(user, req.Headers["Idempotency-Key"].ToString(), "withdraw", body, () => svc.Withdraw(user, body, config["ChainGeneration"] ?? throw new RuleException("Chain is not configured."))), "application/json"));
app.MapPost("/withdraw-finish/{user:guid}/{id:guid}/{success:bool}", async (Guid user, Guid id, bool success, HttpRequest req, TradingService svc) =>
    Results.Content(await svc.Command(user, req.Headers["Idempotency-Key"].ToString(), "withdraw-finish", new { id, success }, () => svc.FinishWithdrawal(user, id, success)), "application/json"));
app.MapProducts();
app.Run();
