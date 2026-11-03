using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nethereum.Signer;
using Novcic.Core;
using Novcic.Custody;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.NumberHandling = Wire.Json.NumberHandling);
builder.Services.AddDbContext<ExchangeDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Exchange")));
builder.Services.AddHttpClient(); builder.Services.AddSingleton<Ethereum>(); builder.Services.AddHostedService<ChainWorker>();
var app = builder.Build();
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
