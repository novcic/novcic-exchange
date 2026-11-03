using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nethereum.Signer;
using Novcic.Core;
using Novcic.Custody;

var fundingCommand=args.FirstOrDefault()=="--local-fund";
var builder = WebApplication.CreateBuilder(fundingCommand?[]:args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore",LogLevel.Warning);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.NumberHandling = Wire.Json.NumberHandling);
builder.Services.AddDbContext<ExchangeDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Exchange")));
builder.Services.AddHttpClient(); builder.Services.AddSingleton<Ethereum>(); builder.Services.AddHostedService<ChainWorker>();
var app = builder.Build();
if(fundingCommand){
    if(args.Length!=5||!Guid.TryParse(args[4],out var id))throw new ArgumentException("Usage: --local-fund ADDRESS BTC|USDT AMOUNT UUID");
    var chain=app.Services.GetRequiredService<Ethereum>();
    if(Ethereum.Long((await chain.Rpc("eth_chainId")).GetString()!)!=31337)throw new InvalidOperationException("Local-chain funding only.");
    var address=args[1].ToLowerInvariant();var asset=args[2];
    if(!System.Text.RegularExpressions.Regex.IsMatch(address,"^0x[0-9a-f]{40}$")||address=="0x0000000000000000000000000000000000000000"||asset is not("BTC" or "USDT"))throw new ArgumentException("Invalid address or asset.");
    var amount=decimal.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture);
    if(amount<=0||amount>1000000000m||decimal.Round(amount,chain.Decimals(asset))!=amount)throw new ArgumentException("Invalid token amount or precision.");
    using(var scope=app.Services.CreateScope()){
        var db=scope.ServiceProvider.GetRequiredService<ExchangeDb>();
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001236)");
        var job=await db.ChainTransfers.FindAsync(id);
        if(job==null){db.ChainTransfers.Add(new ChainTransfer{Id=id,Generation=chain.Generation,Kind="LocalFunding",Asset=asset,Amount=amount,Address=address,Status="Requested"});await db.SaveChangesAsync();}
        else if(job.Kind!="LocalFunding"||job.Address!=address||job.Asset!=asset||job.Amount!=amount)throw new ArgumentException("Funding UUID already belongs to another request.");
        await tx.CommitAsync();
    }
    for(var attempt=0;attempt<120;attempt++){
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ExchangeDb>();var job=await db.ChainTransfers.AsNoTracking().SingleAsync(x=>x.Id==id);
        if(job.Status=="Completed"){Console.WriteLine($"Funded {amount} {asset}; {job.Hash}");return;}
        if(job.Status=="Failed")throw new InvalidOperationException("Funding transaction reverted.");
        await Task.Delay(500);
    }
    throw new TimeoutException($"Funding remains queued. Retry with the same UUID: {id}");
}
app.Use(async (ctx, next) => {
    var supplied = ctx.Request.Headers["X-Internal-Key"].ToString();
    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(builder.Configuration["InternalKey"]!))))
    { ctx.Response.StatusCode = 401; return; }
    await next();
});
app.MapGet("/network", (Ethereum chain) => new {
    network = "Ethereum Local", chainId = "31337", confirmations = "3", generation = chain.Generation,
    tokens = new Dictionary<string, object> { ["BTC"] = new { address = chain.Token("BTC"), decimals = 8 }, ["USDT"] = new { address = chain.Token("USDT"), decimals = 6 } }
});
app.MapPost("/address/{user:guid}", async (Guid user, ExchangeDb db, Ethereum chain) => {
    await using var tx = await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001236)");
    var address = await db.DepositAddresses.FindAsync(user);
    if (address == null) {
        var key = EthECKey.GenerateKey();
        address = new DepositAddress { UserId = user, Address = key.GetPublicAddress().ToLowerInvariant(), EncryptedKey = chain.Encrypt(key.GetPrivateKey()) };
        db.DepositAddresses.Add(address); await db.SaveChangesAsync();
    }
    await tx.CommitAsync(); return Results.Ok(new { address.Address });
});
app.MapGet("/history/{user:guid}", async (Guid user, ExchangeDb db) => await db.ChainTransfers.AsNoTracking()
    .Where(x => x.UserId == user && (x.Kind == "Deposit" || x.Kind == "Withdrawal"))
    .OrderByDescending(x => x.CreatedAt).Take(100)
    .Select(x => new { x.Id, x.Kind, x.Asset, x.Amount, x.Address, x.Status, x.Hash, x.CreatedAt, x.Block, x.Error }).ToListAsync());
app.Run();
