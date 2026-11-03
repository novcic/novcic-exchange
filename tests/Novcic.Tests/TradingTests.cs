using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;

namespace Novcic.Tests;

[CollectionDefinition("database", DisableParallelization = true)]
public class DatabaseCollection { }
[Collection("database")]
public class TradingTests : IAsyncLifetime
{
    private readonly string connection = Environment.GetEnvironmentVariable("TEST_DB") ?? "Host=127.0.0.1;Port=54329;Database=novcic_tests;Username=novcic;Password=novcic-local-demo";
    private ExchangeDb Db() => new(new DbContextOptionsBuilder<ExchangeDb>().UseNpgsql(connection).Options);
    private readonly Guid alice = Guid.NewGuid(); private readonly Guid bob = Guid.NewGuid();
    public async Task InitializeAsync()
    {
        if (!new Npgsql.NpgsqlConnectionStringBuilder(connection).Database!.EndsWith("_tests")) throw new InvalidOperationException("Tests require a disposable database ending in _tests.");
        await using var db = Db(); await db.Database.EnsureDeletedAsync(); await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE exchange.\"Receipts\", exchange.\"Ledger\", exchange.\"Trades\", exchange.\"Orders\", exchange.\"Wallets\" RESTART IDENTITY");
        await Credit(alice); await Credit(bob);
    }
    public Task DisposeAsync() => Task.CompletedTask;
    private async Task<string> Command(Guid user, string op, object body, Func<TradingService, Task<object>> action, string? key = null)
    { await using var db = Db(); var svc = new TradingService(db); return await svc.Command(user, key ?? Guid.NewGuid().ToString(), op, body, () => action(svc)); }
    private async Task Credit(Guid user)
    {
        await Command(user, "faucet", new { }, s => s.Faucet(user));
        await Transfer(user, "BTC", "100"); await Transfer(user, "USDT", "1000000");
    }
    private Task<string> Transfer(Guid user, string asset, string amount)
    { var r = new TransferRequest(asset, "Funding", "Unified Trading", decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)); return Command(user, "transfer", r, s => s.Transfer(user, r)); }
    private async Task<Order> Place(Guid user, string side, string type, decimal qty, decimal? price = null, string? key = null)
    { var r = new PlaceOrder(side, type, qty, price); return JsonSerializer.Deserialize<Order>(await Command(user, "place", r, s => s.Place(user, r), key), Wire.Json)!; }
    private Task<string> Cancel(Guid user, Guid id) => Command(user, "cancel", new { id }, s => s.Cancel(user, id));
    private async Task<Wallet> Balance(Guid user, string asset, string kind = "Unified Trading")
    { await using var db = Db(); return (await db.Wallets.AsNoTracking().SingleAsync(x => x.UserId == user && x.Kind == kind && x.Asset == asset)); }
    private async Task AssertConserved()
    {
        await using var db = Db(); var wallets = await db.Wallets.AsNoTracking().ToListAsync();
        Assert.Equal(200m, wallets.Where(x => x.Asset == "BTC").Sum(x => x.Available + x.Locked));
        Assert.Equal(2_000_000m, wallets.Where(x => x.Asset == "USDT").Sum(x => x.Available + x.Locked));
        Assert.All(wallets, x => { Assert.True(x.Available >= 0); Assert.True(x.Locked >= 0); });
        foreach (var w in wallets)
        {
            var entries = await db.Ledger.Where(x => x.UserId == w.UserId && x.Asset == w.Asset && x.Wallet == w.Kind).ToListAsync();
            Assert.Equal(w.Available, entries.Sum(x => x.AvailableDelta)); Assert.Equal(w.Locked, entries.Sum(x => x.LockedDelta));
        }
    }
    [Fact]
    public async Task TradingSequenceDeductsFeesFromReceivedAssets()
    {
        await Place(bob, "Sell", "Limit", 10, 50000); await Place(alice, "Buy", "Limit", 10, 50000);
        await Place(bob, "Buy", "Limit", 2, 49000); await Place(alice, "Sell", "Market", 2);
        await Place(bob, "Sell", "Limit", 5, 50000); await Place(alice, "Buy", "Limit", 5, 50000);
        Assert.Equal(112.985m, (await Balance(alice, "BTC")).Available); Assert.Equal(347902m, (await Balance(alice, "USDT")).Available);
        Assert.Equal(0, (await Balance(alice, "USDT")).Locked); await AssertConserved();
    }
    [Fact]
    public async Task CrossingLimitUsesMakerPriceAndReleasesPriceImprovement()
    {
        await Place(bob, "Sell", "Limit", 1, 100.01m); var order = await Place(alice, "Buy", "Limit", 2, 110.02m);
        Assert.Equal("PartiallyFilled", order.Status); Assert.Equal(1, order.Remaining); Assert.Equal(110.02m, order.Reserved);
        var usd = await Balance(alice, "USDT"); Assert.Equal(999789.97m, usd.Available); Assert.Equal(110.02m, usd.Locked);
        await Cancel(alice, order.Id); usd = await Balance(alice, "USDT"); Assert.Equal(999899.99m, usd.Available); Assert.Equal(0, usd.Locked); await AssertConserved();
    }
    [Fact]
    public async Task PriceTimePriorityAndMultiLevelMarketPartialFill()
    {
        var first = await Place(bob, "Sell", "Limit", 1, 100); var second = await Place(bob, "Sell", "Limit", 2, 100); var third = await Place(bob, "Sell", "Limit", 1, 101);
        var order = await Place(alice, "Buy", "Market", 5);
        Assert.Equal("Cancelled", order.Status); Assert.Equal(1, order.Remaining); Assert.Equal(0, order.Reserved);
        await using var db = Db(); var fills = await db.Trades.OrderBy(x => x.Sequence).ToListAsync();
        Assert.Equal(new[] { first.Id, second.Id, third.Id }, fills.Select(x => x.MakerOrderId));
        Assert.Equal(103.996m, (await Balance(alice, "BTC")).Available); Assert.Equal(999599m, (await Balance(alice, "USDT")).Available); await AssertConserved();
    }
    [Fact]
    public async Task SellMarketReleasesUnfilledBtc()
    {
        await Place(bob, "Buy", "Limit", 1, 100); var order = await Place(alice, "Sell", "Market", 5);
        Assert.Equal(4, order.Remaining); Assert.Equal("Cancelled", order.Status); Assert.Equal(99, (await Balance(alice, "BTC")).Available); Assert.Equal(0, (await Balance(alice, "BTC")).Locked); await AssertConserved();
    }
    [Fact]
    public async Task InsufficientFundsRollsBackAllWrites()
    {
        await Assert.ThrowsAsync<RuleException>(() => Place(alice, "Buy", "Limit", 100, 50000));
        await using var db = Db(); Assert.Empty(await db.Orders.ToListAsync()); Assert.Equal(1_000_000, (await Balance(alice, "USDT")).Available); await AssertConserved();
    }
    [Fact]
    public async Task DuplicateConcurrentRequestIsAppliedOnce()
    {
        var key = Guid.NewGuid().ToString(); var results = await Task.WhenAll(Place(alice, "Buy", "Limit", 1, 100, key), Place(alice, "Buy", "Limit", 1, 100, key));
        Assert.Equal(results[0].Id, results[1].Id); Assert.Equal(100, (await Balance(alice, "USDT")).Locked);
        await Assert.ThrowsAsync<RuleException>(() => Place(alice, "Buy", "Limit", 2, 100, key)); await AssertConserved();
    }
    [Fact]
    public async Task ConcurrentOrdersCannotSpendTheSameFunds()
    {
        async Task<bool> Attempt() { try { await Place(alice, "Buy", "Limit", 1, 600000); return true; } catch (RuleException) { return false; } }
        var results = await Task.WhenAll(Attempt(), Attempt()); Assert.Single(results, x => x); Assert.Equal(600000, (await Balance(alice, "USDT")).Locked); await AssertConserved();
    }
    [Fact]
    public async Task SelfTradeCancelsIncomingRemainderAndKeepsMaker()
    {
        var maker = await Place(alice, "Sell", "Limit", 1, 100); var incoming = await Place(alice, "Buy", "Limit", 1, 100);
        Assert.Equal("Cancelled", incoming.Status); Assert.Equal("Self-trade prevention", incoming.CancelReason);
        await using var db = Db(); Assert.Empty(await db.Trades.ToListAsync()); Assert.Equal("Open", (await db.Orders.FindAsync(maker.Id))!.Status); Assert.Equal(0, (await Balance(alice, "USDT")).Locked); await AssertConserved();
    }
    [Fact]
    public async Task SeparateContextsRecoverPersistentBook()
    { await Place(bob, "Sell", "Limit", 1, 100); var result = await Place(alice, "Buy", "Market", 1); Assert.Equal("Filled", result.Status); await AssertConserved(); }
    [Fact]
    public async Task FractionalAmountsSettleWithoutRoundingLoss()
    {
        await Place(bob, "Sell", "Limit", 0.000001m, 12345.67m); await Place(alice, "Buy", "Market", 0.000001m);
        Assert.Equal(100.000001m, (await Balance(alice, "BTC")).Available); Assert.Equal(999999.98765433m, (await Balance(alice, "USDT")).Available); await AssertConserved();
    }
    [Fact]
    public async Task EmptyMarketAndInvalidTicksDoNotReserveFunds()
    {
        await Assert.ThrowsAsync<RuleException>(() => Place(alice, "Buy", "Market", 1));
        await Assert.ThrowsAsync<RuleException>(() => Place(alice, "Buy", "Limit", 0.0000001m, 100));
        await Assert.ThrowsAsync<RuleException>(() => Place(alice, "Buy", "Limit", 1, 100.001m)); await AssertConserved();
    }
    [Fact]
    public async Task CannotTransferReservedFundsOrCancelAnotherUsersOrder()
    {
        var order = await Place(alice, "Sell", "Limit", 100, 100);
        var request = new TransferRequest("BTC", "Unified Trading", "Funding", 1);
        await Assert.ThrowsAsync<RuleException>(() => Command(alice, "transfer", request, s => s.Transfer(alice, request)));
        await Assert.ThrowsAsync<RuleException>(() => Cancel(bob, order.Id)); await Cancel(alice, order.Id); await AssertConserved();
    }
}
