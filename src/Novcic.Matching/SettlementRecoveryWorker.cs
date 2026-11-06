using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;

namespace Novcic.Matching;

public class SettlementRecoveryWorker(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IConfiguration config, ILogger<SettlementRecoveryWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var retryAfter = new Dictionary<DateTime, DateTime>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ExchangeDb>();
                var cutoff = DateTime.UtcNow.AddMinutes(-1);
                var expiries = await db.Instruments.AsNoTracking()
                    .Where(i => (i.Status == "Trading" || i.Status == "AwaitingSettlement") && i.Expiry <= cutoff &&
                        !db.SettlementReferences.Any(r => r.Expiry == i.Expiry))
                    .Select(i => i.Expiry!.Value).Distinct().ToListAsync(stop);
                foreach (var old in retryAfter.Keys.Except(expiries).ToList()) retryAfter.Remove(old);
                // Fair bounded retries: an unavailable old expiry cannot starve later recoveries.
                foreach (var expiry in expiries.Where(e => retryAfter.GetValueOrDefault(e) <= DateTime.UtcNow)
                    .OrderBy(e => retryAfter.GetValueOrDefault(e)).ThenBy(e => e).Take(4))
                {
                    retryAfter[expiry] = DateTime.UtcNow.AddMinutes(1);
                    try
                    {
                        var start = expiry.AddMinutes(-30);
                        var samples = await db.IndexSamples.AsNoTracking()
                            .Where(s => s.Time >= start.AddSeconds(-5) && s.Time <= expiry).ToListAsync(stop);
                        if (PriceWindows.Twap(samples, start, expiry).HasValue) continue;
                        var from = new DateTimeOffset(start).ToUnixTimeMilliseconds();
                        var to = new DateTimeOffset(expiry).ToUnixTimeMilliseconds() - 1;
                        using var client = clients.CreateClient();
                        client.Timeout = TimeSpan.FromSeconds(5);
                        var endpoint = (config["BybitUrl"] ?? "https://api.bybit.com").TrimEnd('/') +
                            $"/v5/market/index-price-kline?category=linear&symbol=BTCUSDT&interval=1&start={from}&end={to}&limit=30";
                        // Network calls never hold the financial writer lock.
                        var response = await client.GetFromJsonAsync<JsonElement>(endpoint, stop);
                        var reference = SettlementHistory.Parse(response, expiry, DateTime.UtcNow);
                        using var writeScope = scopes.CreateScope();
                        var writeDb = writeScope.ServiceProvider.GetRequiredService<ExchangeDb>();
                        await using var tx = await writeDb.Database.BeginTransactionAsync(stop);
                        await writeDb.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001234)", stop);
                        var stored = false;
                        if (!await writeDb.SettlementReferences.AnyAsync(r => r.Expiry == expiry, stop) &&
                            await writeDb.Instruments.AnyAsync(i => i.Expiry == expiry && (i.Status == "Trading" || i.Status == "AwaitingSettlement"), stop))
                        {
                            writeDb.SettlementReferences.Add(reference);
                            await writeDb.SaveChangesAsync(stop);
                            stored = true;
                        }
                        await tx.CommitAsync(stop);
                        if (stored) log.LogInformation("Recovered expiry {Expiry} from {Source}; settlement index {Price}.", expiry, reference.Source, reference.Price);
                    }
                    catch (Exception e) when (!stop.IsCancellationRequested)
                    {
                        log.LogWarning(e, "Historical settlement recovery deferred for {Expiry}; retrying automatically.", expiry);
                    }
                }
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                log.LogWarning(e, "Settlement recovery scan deferred.");
            }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
