using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Novcic.Core;
using Novcic.Matching;

namespace Novcic.Tests;

public partial class ProductTests
{
    private sealed class HistoryHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(reply(request));
        }
    }
    private sealed class HistoryClients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private ServiceProvider RecoveryServices() => new ServiceCollection()
        .AddDbContext<ExchangeDb>(o => o.UseNpgsql(connection)).BuildServiceProvider();
    private static async Task Eventually(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!await condition()) await Task.Delay(25, timeout.Token);
    }

    [Fact] public async Task RecoveryWorkerRetriesAfterRestartAndStoresOneImmutableReferenceAcrossInstances()
    {
        var expiry = SettlementHistoryTests.Expiry;
        await using (var db = Db()) { (await db.Instruments.FindAsync("CALL"))!.Expiry = expiry; await db.SaveChangesAsync(); }
        using var services = RecoveryServices();
        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["BybitUrl"] = "https://history.test" }).Build();
        using var unavailable = new HistoryHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using (var worker = new SettlementRecoveryWorker(scopes, new HistoryClients(unavailable), config, NullLogger<SettlementRecoveryWorker>.Instance))
        {
            await worker.StartAsync(CancellationToken.None);
            await Eventually(() => Task.FromResult(unavailable.Calls == 1));
            await worker.StopAsync(CancellationToken.None);
            await using var db = Db(); Assert.Empty(await db.SettlementReferences.ToListAsync());
            Assert.NotEqual("Settled", (await db.Instruments.FindAsync("CALL"))!.Status);
        }
        var expectedStart = new DateTimeOffset(expiry.AddMinutes(-30)).ToUnixTimeMilliseconds();
        var expectedEnd = new DateTimeOffset(expiry).ToUnixTimeMilliseconds() - 1;
        var requests = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var available = new HistoryHandler(request =>
        {
            requests.Add(request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SettlementHistoryTests.Response(expiry).ToJsonString(), Encoding.UTF8, "application/json") };
        });
        using var first = new SettlementRecoveryWorker(scopes, new HistoryClients(available), config, NullLogger<SettlementRecoveryWorker>.Instance);
        using var second = new SettlementRecoveryWorker(scopes, new HistoryClients(available), config, NullLogger<SettlementRecoveryWorker>.Instance);
        await Task.WhenAll(first.StartAsync(CancellationToken.None), second.StartAsync(CancellationToken.None));
        await Eventually(async () => { await using var db = Db(); return await db.SettlementReferences.AnyAsync(); });
        await Task.WhenAll(first.StopAsync(CancellationToken.None), second.StopAsync(CancellationToken.None));
        await using (var db = Db())
        {
            var reference = Assert.Single(await db.SettlementReferences.ToListAsync());
            Assert.Equal(expiry, reference.Expiry); Assert.Equal(52000, reference.Price);
        }
        Assert.All(requests, uri =>
        {
            Assert.Contains("/v5/market/index-price-kline?category=linear&symbol=BTCUSDT&interval=1", uri);
            Assert.Contains($"start={expectedStart}&end={expectedEnd}&limit=30", uri);
        });
        using var revisedProvider = new HistoryHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SettlementHistoryTests.Response(expiry, 59000).ToJsonString(), Encoding.UTF8, "application/json") });
        using var restarted = new SettlementRecoveryWorker(scopes, new HistoryClients(revisedProvider), config, NullLogger<SettlementRecoveryWorker>.Instance);
        await restarted.StartAsync(CancellationToken.None);
        await restarted.StopAsync(CancellationToken.None);
        Assert.Equal(0, revisedProvider.Calls);
        await using var verify = Db(); Assert.Equal(52000, (await verify.SettlementReferences.SingleAsync()).Price);
    }

    [Fact] public async Task HistoricalSettlementRollsBackAtomicallyAndConcurrentRecoverySettlesOnce()
    {
        await Order(bob, "Sell", .1m, 1000, "CALL"); await Order(alice, "Buy", .1m, null, "CALL");
        var expiry = SettlementHistoryTests.Expiry;
        Dictionary<Guid, decimal> beforeCash;
        int beforeJournal;
        await using (var db = Db())
        {
            (await db.Instruments.FindAsync("CALL"))!.Expiry = expiry;
            db.SettlementReferences.Add(SettlementHistoryTests.Parse(SettlementHistoryTests.Response(expiry), expiry));
            await db.SaveChangesAsync();
            beforeCash = await db.MarginBalances.ToDictionaryAsync(x => x.UserId, x => x.Cash);
            beforeJournal = await db.Journal.CountAsync();
        }
        await Price(50000, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Cmd(alice, "recovery-rollback", new { }, async s => { await s.ProcessMarkets(); throw new InvalidOperationException("Test rollback"); }));
        await using (var db = Db())
        {
            Assert.Equal("Trading", (await db.Instruments.FindAsync("CALL"))!.Status);
            Assert.Equal(.1m, (await db.Positions.FindAsync(alice, "CALL"))!.Quantity);
            Assert.Null((await db.Instruments.FindAsync("CALL"))!.SettlementSource);
            Assert.Equal(beforeCash.OrderBy(x => x.Key), (await db.MarginBalances.ToDictionaryAsync(x => x.UserId, x => x.Cash)).OrderBy(x => x.Key));
            Assert.Equal(beforeJournal, await db.Journal.CountAsync());
        }
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Cmd(alice, "concurrent-recovery", new { }, async s => { await s.ProcessMarkets(); return new { }; })));
        await using (var db = Db())
        {
            Assert.Equal("Settled", (await db.Instruments.FindAsync("CALL"))!.Status);
            Assert.Equal(beforeCash.GetValueOrDefault(alice) + 200, (await db.MarginBalances.FindAsync(alice))!.Cash);
            Assert.Equal(beforeCash.GetValueOrDefault(bob) - 200, (await db.MarginBalances.FindAsync(bob))!.Cash);
        }
        await Conserve();
    }
}
